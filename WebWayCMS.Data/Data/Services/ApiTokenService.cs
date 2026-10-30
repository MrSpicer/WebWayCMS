using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// The default <see cref="IApiTokenService"/>. Tokens are 32 random bytes (base64url) behind
/// <see cref="ApiTokenRules.TokenPrefix"/>; the table keeps only their SHA-256, which is enough for a
/// high-entropy secret (no slow hash needed) and makes validation one indexed lookup.
/// </summary>
public sealed class ApiTokenService : IApiTokenService
{
    /// <summary>A use is recorded only when the last one is older than this, so a busy agent doesn't write per call.</summary>
    internal static readonly TimeSpan LastUsedResolution = TimeSpan.FromMinutes(1);

    private readonly CmsDbContext _context;

    public ApiTokenService(CmsDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<(bool Success, string? ErrorMessage, string? PlainToken, ApiTokenDTO? Token)> CreateAsync(
        string userId, string name, DateTime? expiresUtc, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return (false, "A user is required.", null, null);

        var nameError = ValidateName(name);
        if (nameError != null) return (false, nameError, null, null);
        name = name.Trim();

        var now = DateTime.UtcNow;
        if (expiresUtc.HasValue && expiresUtc.Value <= now)
            return (false, "The expiry must be in the future.", null, null);

        var sameName = await LiveTokensNamedAsync(userId, name, ct);
        if (sameName.Any(t => !(t.ExpiresUtc <= now))) return (false, NameTakenMessage(name), null, null);

        // An expired token still holds its name in the unique index (its filter can't see the clock);
        // it can never be used again, so revoking it frees the name. Saved on its own: EF can't tell the
        // revoke releases a filtered index value, so it could otherwise order the insert first.
        if (sameName.Count > 0)
        {
            foreach (var expired in sameName) expired.RevokedUtc = now;
            await _context.SaveChangesAsync(ct);
        }

        var plain = ApiTokenRules.TokenPrefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var token = new ApiTokenDTO
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            TokenHash = Hash(plain),
            Prefix = DisplayPrefix(plain),
            CreatedUtc = now,
            ExpiresUtc = expiresUtc,
        };

        _context.Set<ApiTokenDTO>().Add(token);
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent create (e.g. a double-submitted form) took the name between the check and the insert.
            _context.ChangeTracker.Clear();
            return (false, NameTakenMessage(name), null, null);
        }

        return (true, null, plain, token);
    }

    public async Task<ApiTokenDTO?> ValidateAsync(string? plainToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(plainToken)) return null;

        var hash = Hash(plainToken);
        var token = await _context.Set<ApiTokenDTO>().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        var now = DateTime.UtcNow;
        if (token == null || token.RevokedUtc.HasValue || token.ExpiresUtc <= now) return null;

        if (token.LastUsedUtc == null || now - token.LastUsedUtc.Value >= LastUsedResolution)
        {
            token.LastUsedUtc = now;
            await _context.SaveChangesAsync(ct);
        }

        return token;
    }

    public async Task<IReadOnlyList<ApiTokenDTO>> ListForUserAsync(string userId, CancellationToken ct = default)
    {
        return await _context.Set<ApiTokenDTO>()
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedUtc)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ApiTokenListItem>> ListAllAsync(CancellationToken ct = default)
    {
        var rows = await (
                from t in _context.Set<ApiTokenDTO>().AsNoTracking()
                join u in _context.Set<IdentityUser>().AsNoTracking() on t.UserId equals u.Id into users
                from u in users.DefaultIfEmpty()
                orderby t.CreatedUtc descending
                select new { Token = t, Email = u == null ? null : u.Email })
            .ToListAsync(ct);

        return rows.Select(r => new ApiTokenListItem(r.Token, r.Email)).ToList();
    }

    public async Task<(bool Success, string? ErrorMessage)> RevokeAsync(
        Guid tokenId, string actingUserId, bool actingIsAdmin, CancellationToken ct = default)
    {
        var token = await _context.Set<ApiTokenDTO>().FirstOrDefaultAsync(t => t.Id == tokenId, ct);
        // An owner check that fails reads the same as a missing token, so ids of other users' tokens don't leak.
        if (token == null || (!actingIsAdmin && token.UserId != actingUserId))
            return (false, "Token not found.");

        if (!token.RevokedUtc.HasValue)
        {
            token.RevokedUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }

        return (true, null);
    }

    public async Task<(bool Success, string? ErrorMessage)> UpsertSeedTokenAsync(
        string userId, string name, string plainToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return (false, "A user is required.");

        var nameError = ValidateName(name);
        if (nameError != null) return (false, nameError);
        name = name.Trim();

        if (string.IsNullOrWhiteSpace(plainToken) || plainToken.Length < ApiTokenRules.MinSeedTokenLength)
            return (false, $"A seed token must be at least {ApiTokenRules.MinSeedTokenLength} characters.");

        var hash = Hash(plainToken);
        // The plaintext's own row comes first: a seed entry renamed in config keeps its token (inserting
        // a second row with the same hash would break the unique TokenHash index and fail the boot).
        var existing = await _context.Set<ApiTokenDTO>().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (existing != null && existing.UserId != userId)
            return (false, "That seed token is already issued to another user.");

        // Otherwise a live token of that name wins over revoked ones, which the unique index lets share the name.
        existing ??= await _context.Set<ApiTokenDTO>()
            .Where(t => t.UserId == userId && t.Name == name)
            .OrderBy(t => t.RevokedUtc != null)
            .ThenByDescending(t => t.CreatedUtc)
            .FirstOrDefaultAsync(ct);

        if (existing != null && (existing.Name != name || existing.RevokedUtc.HasValue))
        {
            // The row is about to hold the name live: retire any other live token holding it first, in its
            // own save, so the (UserId, Name) unique index never sees both.
            var holders = (await LiveTokensNamedAsync(userId, name, ct)).Where(t => t.Id != existing.Id).ToList();
            if (holders.Count > 0)
            {
                foreach (var holder in holders) holder.RevokedUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync(ct);
            }
        }

        if (existing == null)
        {
            _context.Set<ApiTokenDTO>().Add(new ApiTokenDTO
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = name,
                TokenHash = hash,
                Prefix = DisplayPrefix(plainToken),
                CreatedUtc = DateTime.UtcNow,
            });
        }
        else if (existing.Name != name || existing.TokenHash != hash || existing.RevokedUtc.HasValue
                 || existing.ExpiresUtc.HasValue)
        {
            existing.Name = name;
            existing.TokenHash = hash;
            existing.Prefix = DisplayPrefix(plainToken);
            existing.RevokedUtc = null;
            existing.ExpiresUtc = null;
        }
        else
        {
            return (true, null);
        }

        await _context.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>Lowercase hex SHA-256 of the token's UTF-8 bytes.</summary>
    internal static string Hash(string plainToken) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plainToken)));

    // Every stored token is longer than the prefix (issued ones by construction, seeded ones by MinSeedTokenLength).
    private static string DisplayPrefix(string plainToken) => plainToken[..ApiTokenRules.DisplayPrefixLength];

    private static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "A token name is required.";
        if (name.Trim().Length > ApiTokenRules.MaxNameLength)
            return $"A token name can be at most {ApiTokenRules.MaxNameLength} characters.";
        return null;
    }

    private static string NameTakenMessage(string name) => $"You already have a token named '{name}'.";

    /// <summary>The user's unrevoked tokens named <paramref name="name"/> (expired ones included), tracked.</summary>
    private Task<List<ApiTokenDTO>> LiveTokensNamedAsync(string userId, string name, CancellationToken ct) =>
        _context.Set<ApiTokenDTO>().Where(t => t.UserId == userId && t.Name == name && t.RevokedUtc == null).ToListAsync(ct);
}
