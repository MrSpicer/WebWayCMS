using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Serilog;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services.Users;

/// <summary>
/// The default <see cref="IUserAdminService"/>: Identity's <see cref="UserManager{TUser}"/> /
/// <see cref="RoleManager{TRole}"/> for account writes, the CMS context for listing and the
/// <see cref="UserProfileDTO"/> rows. Every successful write logs one audit line.
/// </summary>
public sealed class UserAdminService : IUserAdminService
{
    /// <summary>
    /// "Locked until an Admin unlocks it". A fixed far-future date rather than <see cref="DateTimeOffset.MaxValue"/>,
    /// which PostgreSQL's microsecond <c>timestamptz</c> would round past its range.
    /// </summary>
    public static readonly DateTimeOffset IndefiniteLockoutEnd = new(9999, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public const string NotFoundMessage = "User not found.";
    public const string DeletedMessage = "This user is deleted. Restore the user first.";

    private readonly CmsDbContext _context;
    private readonly UserManager<IdentityUser> _users;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly ILogger _logger = Log.ForContext<UserAdminService>();

    public UserAdminService(CmsDbContext context, UserManager<IdentityUser> users, RoleManager<IdentityRole> roles)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
    }

    // ----- reads -------------------------------------------------------------------------------------------

    public async Task<UserListPage> ListAsync(UserListQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var skip = Math.Max(0, query.Skip);
        var take = Math.Clamp(query.Take, 1, UserListQuery.MaxTake);
        var now = DateTimeOffset.UtcNow;

        var rows =
            from u in _context.Set<IdentityUser>().AsNoTracking()
            join p in _context.Set<UserProfileDTO>().AsNoTracking() on u.Id equals p.UserId into profiles
            from p in profiles.DefaultIfEmpty()
            select new { User = u, Profile = p };

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var roleId = await RoleIdAsync(query.Role, ct);
            if (roleId == null)
                return new UserListPage([], 0, skip, take);

            var members = _context.Set<IdentityUserRole<string>>().Where(ur => ur.RoleId == roleId).Select(ur => ur.UserId);
            rows = rows.Where(r => members.Contains(r.User.Id));
        }

        rows = query.Status switch
        {
            UserStatus.Deleted => rows.Where(r => r.Profile != null && r.Profile.IsDeleted),
            UserStatus.Locked => rows.Where(r => (r.Profile == null || !r.Profile.IsDeleted)
                && r.User.LockoutEnd != null && r.User.LockoutEnd > now),
            UserStatus.Unconfirmed => rows.Where(r => (r.Profile == null || !r.Profile.IsDeleted)
                && (r.User.LockoutEnd == null || r.User.LockoutEnd <= now) && !r.User.EmailConfirmed),
            UserStatus.Active => rows.Where(r => (r.Profile == null || !r.Profile.IsDeleted)
                && (r.User.LockoutEnd == null || r.User.LockoutEnd <= now) && r.User.EmailConfirmed),
            _ => rows.Where(r => r.Profile == null || !r.Profile.IsDeleted),
        };

        var total = await rows.CountAsync(ct);
        var page = await rows.OrderBy(r => r.User.NormalizedEmail).ThenBy(r => r.User.Id).Skip(skip).Take(take).ToListAsync(ct);
        var roles = await RolesByUserAsync(page.Select(r => r.User.Id).ToList(), ct);

        var items = page.Select(r => new UserListItem(
                r.User.Id,
                r.User.Email ?? string.Empty,
                r.Profile?.DisplayName,
                r.Profile?.FirstName,
                r.Profile?.LastName,
                roles.GetValueOrDefault(r.User.Id) ?? [],
                StatusOf(r.User, r.Profile, now),
                IsInvitePending(r.User, r.Profile),
                r.Profile?.LastLoginUtc))
            .ToList();

        return new UserListPage(items, total, skip, take);
    }

    public async Task<UserDetail?> GetAsync(string userId, CancellationToken ct = default)
    {
        var user = await FindUserAsync(userId);
        if (user == null)
            return null;

        var profile = await ProfileAsync(user.Id, ct);
        var roles = (await _users.GetRolesAsync(user)).OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList();
        var logins = (await _users.GetLoginsAsync(user))
            .Select(l => new UserExternalLogin(l.LoginProvider, l.ProviderKey, l.ProviderDisplayName))
            .ToList();
        var passkeys = await _users.GetPasskeysAsync(user);
        var now = DateTime.UtcNow;
        var tokens = await _context.Set<ApiTokenDTO>().CountAsync(
            t => t.UserId == user.Id && t.RevokedUtc == null && (t.ExpiresUtc == null || t.ExpiresUtc > now), ct);

        return new UserDetail(
            user.Id,
            user.Email ?? string.Empty,
            user.EmailConfirmed,
            user.PhoneNumber,
            profile?.DisplayName,
            profile?.FirstName,
            profile?.LastName,
            roles,
            StatusOf(user, profile, DateTimeOffset.UtcNow),
            IsInvitePending(user, profile),
            user.PasswordHash != null,
            user.TwoFactorEnabled,
            passkeys.Count,
            logins,
            tokens,
            user.LockoutEnd,
            user.AccessFailedCount,
            profile?.LastLoginUtc,
            profile?.InvitedUtc,
            profile?.DeletedUtc);
    }

    public async Task<IReadOnlyList<RoleSummary>> ListRolesAsync(CancellationToken ct = default)
    {
        var counts = await _context.Set<IdentityUserRole<string>>()
            .GroupBy(ur => ur.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.RoleId, g => g.Count, ct);

        var roles = await _context.Set<IdentityRole>().AsNoTracking().ToListAsync(ct);
        return roles
            .Where(r => r.Name != null)
            .Select(r => new RoleSummary(r.Name!, counts.GetValueOrDefault(r.Id), UserProfileRules.IsBuiltInRole(r.Name)))
            .OrderBy(r => r.IsBuiltIn ? 0 : 1)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ----- invite / edit -------------------------------------------------------------------------------------

    public async Task<UserAdminResult> InviteAsync(UserInvite invite, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(invite);
        var email = invite.Email?.Trim() ?? string.Empty;
        if (ValidateEmail(email) is { } emailError)
            return UserAdminResult.Fail(emailError);
        if (ValidateProfile(invite.Profile) is { } profileError)
            return UserAdminResult.Fail(profileError);

        var existing = await _users.FindByEmailAsync(email);
        if (existing != null)
        {
            var existingProfile = await ProfileAsync(existing.Id, ct);
            return UserAdminResult.Fail(existingProfile?.IsDeleted == true
                ? "A deleted user has this email. Restore it, or delete it permanently first."
                : "A user with this email already exists.");
        }

        var (roles, roleError) = await ResolveRolesAsync(invite.Roles, ct);
        if (roleError != null)
            return UserAdminResult.Fail(roleError);

        await using var tx = await _context.Database.BeginTransactionAsync(ct);
        var user = new IdentityUser { UserName = email, Email = email };
        var created = await _users.CreateAsync(user);
        if (!created.Succeeded)
            return await RolledBackAsync(created, tx, ct);

        if (roles.Count > 0)
        {
            var added = await _users.AddToRolesAsync(user, roles);
            if (!added.Succeeded)
                return await RolledBackAsync(added, tx, ct);
        }

        var profile = new UserProfileDTO { UserId = user.Id, InvitedUtc = DateTime.UtcNow, InvitedBy = actorId };
        ApplyProfile(profile, invite.Profile);
        _context.Set<UserProfileDTO>().Add(profile);
        await _context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        Audit(actorId, "invited", user);
        return UserAdminResult.Ok(user.Id);
    }

    public async Task<UserAdminResult> UpdateAsync(string userId, UserUpdate update, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        var (user, profile, error) = await LoadLiveAsync(userId, ct);
        if (error != null)
            return UserAdminResult.Fail(error);

        var email = update.Email?.Trim() ?? string.Empty;
        if (ValidateEmail(email) is { } emailError)
            return UserAdminResult.Fail(emailError);
        if (ValidateProfile(update.Profile) is { } profileError)
            return UserAdminResult.Fail(profileError);

        var phone = Blank(update.PhoneNumber);
        if (phone != null && !new PhoneAttribute().IsValid(phone))
            return UserAdminResult.Fail("Enter a valid phone number.");

        string? previousEmail = null;
        if (!string.Equals(email, user!.Email, StringComparison.OrdinalIgnoreCase))
        {
            // A changed email is unconfirmed, and RequireConfirmedAccount then refuses the user's sign-in: the
            // acting Admin changes their own through Account → Email (which keeps the old one until the new one
            // is confirmed), and the last active Admin is never taken out of service.
            if ((UserManagementGuards.SelfEmailChange(actorId, user.Id)
                 ?? await LastActiveAdminAsync(user, profile, ct)) is { } guard)
                return UserAdminResult.Fail(guard);

            var owner = await _users.FindByEmailAsync(email);
            if (owner != null && owner.Id != user.Id)
                return UserAdminResult.Fail("Another user already has this email.");
        }

        await using var tx = await _context.Database.BeginTransactionAsync(ct);
        if (!string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            previousEmail = user.Email;
            // SetEmailAsync clears EmailConfirmed and rotates the security stamp; the username follows the email.
            var setEmail = await _users.SetEmailAsync(user, email);
            if (!setEmail.Succeeded)
                return await RolledBackAsync(setEmail, tx, ct);
            var setName = await _users.SetUserNameAsync(user, email);
            if (!setName.Succeeded)
                return await RolledBackAsync(setName, tx, ct);
        }

        if (!string.Equals(phone, Blank(user.PhoneNumber), StringComparison.Ordinal))
        {
            var setPhone = await _users.SetPhoneNumberAsync(user, phone);
            if (!setPhone.Succeeded)
                return await RolledBackAsync(setPhone, tx, ct);
        }

        if (profile == null)
        {
            profile = new UserProfileDTO { UserId = user.Id };
            _context.Set<UserProfileDTO>().Add(profile);
        }
        ApplyProfile(profile, update.Profile);
        await _context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        Audit(actorId, previousEmail == null ? "edited" : $"edited (email changed from {previousEmail})", user);
        return UserAdminResult.Ok(user.Id, previousEmail);
    }

    public async Task<UserAdminResult> SetRolesAsync(string userId, IReadOnlyCollection<string> roles, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var (user, profile, error) = await LoadLiveAsync(userId, ct);
        if (error != null)
            return UserAdminResult.Fail(error);

        var (wanted, roleError) = await ResolveRolesAsync(roles, ct);
        if (roleError != null)
            return UserAdminResult.Fail(roleError);

        var current = await _users.GetRolesAsync(user!);
        var toRemove = current.Where(r => !wanted.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();
        var toAdd = wanted.Where(r => !current.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();

        if (toRemove.Contains(UserProfileRules.AdminRole, StringComparer.OrdinalIgnoreCase)
            && (UserManagementGuards.SelfAction(actorId, user!.Id, "remove the Admin role from")
                ?? await LastActiveAdminAsync(user, profile, ct)) is { } guard)
            return UserAdminResult.Fail(guard);

        if (toRemove.Count > 0)
        {
            var removed = await _users.RemoveFromRolesAsync(user!, toRemove);
            if (!removed.Succeeded)
                return Fail(removed);
        }

        if (toAdd.Count > 0)
        {
            var added = await _users.AddToRolesAsync(user!, toAdd);
            if (!added.Succeeded)
                return Fail(added);
        }

        Audit(actorId, $"set roles [{string.Join(", ", wanted)}] on", user!);
        return UserAdminResult.Ok(user!.Id);
    }

    public async Task<UserAdminResult> ConfirmEmailAsync(string userId, string actorId, CancellationToken ct = default)
    {
        var (user, _, error) = await LoadLiveAsync(userId, ct);
        if (error != null)
            return UserAdminResult.Fail(error);

        if (!user!.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            var updated = await _users.UpdateAsync(user);
            if (!updated.Succeeded)
                return Fail(updated);
        }

        Audit(actorId, "confirmed the email of", user);
        return UserAdminResult.Ok(user.Id);
    }

    // ----- security ------------------------------------------------------------------------------------------

    public async Task<UserAdminResult> LockAsync(string userId, string actorId, CancellationToken ct = default)
    {
        var (user, profile, error) = await LoadLiveAsync(userId, ct);
        if (error != null)
            return UserAdminResult.Fail(error);

        if ((UserManagementGuards.SelfAction(actorId, user!.Id, "lock")
             ?? await LastActiveAdminAsync(user, profile, ct)) is { } guard)
            return UserAdminResult.Fail(guard);

        if (await LockOutAsync(user) is { } lockError)
            return lockError;

        Audit(actorId, "locked", user);
        return UserAdminResult.Ok(user.Id);
    }

    public async Task<UserAdminResult> UnlockAsync(string userId, string actorId, CancellationToken ct = default)
    {
        var (user, _, error) = await LoadLiveAsync(userId, ct);
        if (error != null)
            return UserAdminResult.Fail(error);

        var unlocked = await _users.SetLockoutEndDateAsync(user!, null);
        if (!unlocked.Succeeded)
            return Fail(unlocked);
        var reset = await _users.ResetAccessFailedCountAsync(user!);
        if (!reset.Succeeded)
            return Fail(reset);

        Audit(actorId, "unlocked", user!);
        return UserAdminResult.Ok(user!.Id);
    }

    public async Task<UserAdminResult> SignOutEverywhereAsync(string userId, string actorId, CancellationToken ct = default)
    {
        var (user, _, error) = await LoadLiveAsync(userId, ct);
        if (error != null)
            return UserAdminResult.Fail(error);

        var stamped = await _users.UpdateSecurityStampAsync(user!);
        if (!stamped.Succeeded)
            return Fail(stamped);

        Audit(actorId, "signed out everywhere", user!);
        return UserAdminResult.Ok(user!.Id);
    }

    public async Task<UserAdminResult> ResetTwoFactorAsync(string userId, string actorId, CancellationToken ct = default)
    {
        var (user, _, error) = await LoadLiveAsync(userId, ct);
        if (error != null)
            return UserAdminResult.Fail(error);

        var disabled = await _users.SetTwoFactorEnabledAsync(user!, false);
        if (!disabled.Succeeded)
            return Fail(disabled);
        // A new key invalidates the old authenticator app registration (and rotates the security stamp).
        var keyReset = await _users.ResetAuthenticatorKeyAsync(user!);
        if (!keyReset.Succeeded)
            return Fail(keyReset);
        await _users.GenerateNewTwoFactorRecoveryCodesAsync(user!, 0);

        Audit(actorId, "reset two-factor authentication for", user!);
        return UserAdminResult.Ok(user!.Id);
    }

    public async Task<UserAdminResult> RevokeApiTokensAsync(string userId, string actorId, CancellationToken ct = default)
    {
        var (user, _, error) = await LoadLiveAsync(userId, ct);
        if (error != null)
            return UserAdminResult.Fail(error);

        var count = await RevokeTokensAsync(user!.Id, ct);
        Audit(actorId, $"revoked {count} API token(s) of", user);
        return UserAdminResult.Ok(user.Id);
    }

    public async Task<UserAdminResult> RemovePasskeysAsync(string userId, string actorId, CancellationToken ct = default)
    {
        var (user, _, error) = await LoadLiveAsync(userId, ct);
        if (error != null)
            return UserAdminResult.Fail(error);

        var passkeys = await _users.GetPasskeysAsync(user!);
        foreach (var passkey in passkeys)
        {
            var removed = await _users.RemovePasskeyAsync(user!, passkey.CredentialId);
            if (!removed.Succeeded)
                return Fail(removed);
        }

        Audit(actorId, $"removed {passkeys.Count} passkey(s) of", user!);
        return UserAdminResult.Ok(user!.Id);
    }

    public async Task<UserAdminResult> RemoveExternalLoginAsync(
        string userId, string loginProvider, string providerKey, string actorId, CancellationToken ct = default)
    {
        var (user, _, error) = await LoadLiveAsync(userId, ct);
        if (error != null)
            return UserAdminResult.Fail(error);

        var logins = await _users.GetLoginsAsync(user!);
        if (!logins.Any(l => l.LoginProvider == loginProvider && l.ProviderKey == providerKey))
            return UserAdminResult.Fail("That external login is not linked to this user.");

        var removed = await _users.RemoveLoginAsync(user!, loginProvider, providerKey);
        if (!removed.Succeeded)
            return Fail(removed);

        Audit(actorId, $"removed the {loginProvider} login of", user!);
        return UserAdminResult.Ok(user!.Id);
    }

    // ----- delete ----------------------------------------------------------------------------------------------

    public async Task<UserAdminResult> SoftDeleteAsync(string userId, string actorId, CancellationToken ct = default)
    {
        var (user, profile, error) = await LoadLiveAsync(userId, ct);
        if (error != null)
            return UserAdminResult.Fail(error);

        if ((UserManagementGuards.SelfAction(actorId, user!.Id, "delete")
             ?? await LastActiveAdminAsync(user, profile, ct)) is { } guard)
            return UserAdminResult.Fail(guard);

        if (await LockOutAsync(user) is { } lockError)
            return lockError;

        if (profile == null)
        {
            profile = new UserProfileDTO { UserId = user.Id };
            _context.Set<UserProfileDTO>().Add(profile);
        }
        profile.IsDeleted = true;
        profile.DeletedUtc = DateTime.UtcNow;
        profile.DeletedBy = actorId;
        await _context.SaveChangesAsync(ct);
        await RevokeTokensAsync(user.Id, ct);

        Audit(actorId, "deleted", user);
        return UserAdminResult.Ok(user.Id);
    }

    public async Task<UserAdminResult> RestoreAsync(string userId, string actorId, CancellationToken ct = default)
    {
        var user = await FindUserAsync(userId);
        if (user == null)
            return UserAdminResult.Fail(NotFoundMessage);
        var profile = await ProfileAsync(user.Id, ct);
        if (profile?.IsDeleted != true)
            return UserAdminResult.Fail("This user is not deleted.");

        profile.IsDeleted = false;
        profile.DeletedUtc = null;
        profile.DeletedBy = null;
        await _context.SaveChangesAsync(ct);

        var unlocked = await _users.SetLockoutEndDateAsync(user, null);
        if (!unlocked.Succeeded)
            return Fail(unlocked);

        Audit(actorId, "restored", user);
        return UserAdminResult.Ok(user.Id);
    }

    public async Task<UserAdminResult> PurgeAsync(string userId, string actorId, CancellationToken ct = default)
    {
        var user = await FindUserAsync(userId);
        if (user == null)
            return UserAdminResult.Fail(NotFoundMessage);
        if (UserManagementGuards.SelfAction(actorId, user.Id, "delete") is { } self)
            return UserAdminResult.Fail(self);
        var profile = await ProfileAsync(user.Id, ct);
        if (profile?.IsDeleted != true)
            return UserAdminResult.Fail("Delete the user first; only a deleted user can be deleted permanently.");

        var email = user.Email;
        var deleted = await _users.DeleteAsync(user);
        if (!deleted.Succeeded)
            return Fail(deleted);

        _logger.Information("User management: {Actor} permanently deleted {TargetEmail} ({Target})", actorId, email, user.Id);
        return UserAdminResult.Ok(user.Id);
    }

    // ----- roles -----------------------------------------------------------------------------------------------

    public async Task<UserAdminResult> CreateRoleAsync(string name, string actorId, CancellationToken ct = default)
    {
        name = name?.Trim() ?? string.Empty;
        if (ValidateRoleName(name) is { } nameError)
            return UserAdminResult.Fail(nameError);
        if (await _roles.RoleExistsAsync(name))
            return UserAdminResult.Fail($"A role named '{name}' already exists.");

        var created = await _roles.CreateAsync(new IdentityRole(name));
        if (!created.Succeeded)
            return Fail(created);

        _logger.Information("User management: {Actor} created role {Role}", actorId, name);
        return UserAdminResult.Ok();
    }

    public async Task<UserAdminResult> RenameRoleAsync(string name, string newName, string actorId, CancellationToken ct = default)
    {
        newName = newName?.Trim() ?? string.Empty;
        var role = await _roles.FindByNameAsync(name ?? string.Empty);
        if (role == null)
            return UserAdminResult.Fail("Role not found.");
        if (UserProfileRules.IsBuiltInRole(role.Name))
            return UserAdminResult.Fail($"'{role.Name}' is a built-in role and can't be renamed.");
        if (ValidateRoleName(newName) is { } nameError)
            return UserAdminResult.Fail(nameError);

        var other = await _roles.FindByNameAsync(newName);
        if (other != null && other.Id != role.Id)
            return UserAdminResult.Fail($"A role named '{newName}' already exists.");

        var oldName = role.Name;
        var renamed = await _roles.SetRoleNameAsync(role, newName);
        if (renamed.Succeeded)
            renamed = await _roles.UpdateAsync(role);
        if (!renamed.Succeeded)
            return Fail(renamed);

        _logger.Information("User management: {Actor} renamed role {Role} to {NewRole}", actorId, oldName, newName);
        return UserAdminResult.Ok();
    }

    public async Task<UserAdminResult> DeleteRoleAsync(string name, string actorId, CancellationToken ct = default)
    {
        var role = await _roles.FindByNameAsync(name ?? string.Empty);
        if (role == null)
            return UserAdminResult.Fail("Role not found.");
        if (UserProfileRules.IsBuiltInRole(role.Name))
            return UserAdminResult.Fail($"'{role.Name}' is a built-in role and can't be deleted.");

        var members = await _context.Set<IdentityUserRole<string>>().CountAsync(ur => ur.RoleId == role.Id, ct);
        if (members > 0)
            return UserAdminResult.Fail($"'{role.Name}' still has {members} member(s). Remove them from the role first.");

        var deleted = await _roles.DeleteAsync(role);
        if (!deleted.Succeeded)
            return Fail(deleted);

        _logger.Information("User management: {Actor} deleted role {Role}", actorId, role.Name);
        return UserAdminResult.Ok();
    }

    public async Task RecordLoginAsync(string userId, CancellationToken ct = default)
    {
        var profile = await ProfileAsync(userId, ct);
        if (profile == null)
        {
            profile = new UserProfileDTO { UserId = userId };
            _context.Set<UserProfileDTO>().Add(profile);
        }
        profile.LastLoginUtc = DateTime.UtcNow;
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // The scoped context lives on in this request (an external-login or register flow saves again after
            // the sign-in), so the failed insert must not stay tracked or the next save retries it.
            _context.Entry(profile).State = EntityState.Detached;
            _logger.Debug(ex, "Two concurrent first sign-ins of {UserId} raced on the profile row.", userId);
        }
    }

    public async Task<bool> IsDeletedAsync(string userId, CancellationToken ct = default) =>
        !string.IsNullOrWhiteSpace(userId)
        && await _context.Set<UserProfileDTO>().AsNoTracking().AnyAsync(p => p.UserId == userId && p.IsDeleted, ct);

    // ----- helpers ---------------------------------------------------------------------------------------------

    internal static UserStatus StatusOf(IdentityUser user, UserProfileDTO? profile, DateTimeOffset now)
    {
        if (profile?.IsDeleted == true)
            return UserStatus.Deleted;
        if (user.LockoutEnd > now)
            return UserStatus.Locked;
        return user.EmailConfirmed ? UserStatus.Active : UserStatus.Unconfirmed;
    }

    private static bool IsInvitePending(IdentityUser user, UserProfileDTO? profile) =>
        profile?.InvitedUtc != null && user.PasswordHash == null && !user.EmailConfirmed;

    private Task<IdentityUser?> FindUserAsync(string? userId) =>
        string.IsNullOrWhiteSpace(userId) ? Task.FromResult<IdentityUser?>(null) : _users.FindByIdAsync(userId);

    private async Task<UserProfileDTO?> ProfileAsync(string userId, CancellationToken ct) =>
        await _context.Set<UserProfileDTO>().FirstOrDefaultAsync(p => p.UserId == userId, ct);

    /// <summary>The user and profile for a write that a soft-deleted user refuses.</summary>
    private async Task<(IdentityUser? User, UserProfileDTO? Profile, string? Error)> LoadLiveAsync(string userId, CancellationToken ct)
    {
        var user = await FindUserAsync(userId);
        if (user == null)
            return (null, null, NotFoundMessage);

        var profile = await ProfileAsync(user.Id, ct);
        return profile?.IsDeleted == true ? (user, profile, DeletedMessage) : (user, profile, null);
    }

    private async Task<UserAdminResult?> LockOutAsync(IdentityUser user)
    {
        var enabled = await _users.SetLockoutEnabledAsync(user, true);
        if (!enabled.Succeeded)
            return Fail(enabled);
        var locked = await _users.SetLockoutEndDateAsync(user, IndefiniteLockoutEnd);
        if (!locked.Succeeded)
            return Fail(locked);
        var stamped = await _users.UpdateSecurityStampAsync(user);
        return stamped.Succeeded ? null : Fail(stamped);
    }

    /// <summary>
    /// The last-Admin refusal when taking <paramref name="user"/> out of service would leave no active Admin.
    /// Active means <see cref="UserStatus.Active"/>: an unconfirmed Admin (an invitation not yet accepted)
    /// can't sign in under RequireConfirmedAccount, so it never counts.
    /// </summary>
    private async Task<string?> LastActiveAdminAsync(IdentityUser user, UserProfileDTO? profile, CancellationToken ct)
    {
        var isActiveAdmin = StatusOf(user, profile, DateTimeOffset.UtcNow) == UserStatus.Active
            && await _users.IsInRoleAsync(user, UserProfileRules.AdminRole);
        if (!isActiveAdmin)
            return null;

        return UserManagementGuards.LastActiveAdmin(true, await CountActiveAdminsAsync(ct));
    }

    private async Task<int> CountActiveAdminsAsync(CancellationToken ct)
    {
        var adminRoleId = await RoleIdAsync(UserProfileRules.AdminRole, ct);
        var now = DateTimeOffset.UtcNow;
        return await (
                from ur in _context.Set<IdentityUserRole<string>>()
                where ur.RoleId == adminRoleId
                join u in _context.Set<IdentityUser>() on ur.UserId equals u.Id
                join p in _context.Set<UserProfileDTO>() on u.Id equals p.UserId into profiles
                from p in profiles.DefaultIfEmpty()
                where (p == null || !p.IsDeleted) && (u.LockoutEnd == null || u.LockoutEnd <= now) && u.EmailConfirmed
                select u.Id)
            .CountAsync(ct);
    }

    private async Task<string?> RoleIdAsync(string role, CancellationToken ct)
    {
        var normalized = _roles.NormalizeKey(role);
        return await _context.Set<IdentityRole>()
            .Where(r => r.NormalizedName == normalized)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<Dictionary<string, IReadOnlyList<string>>> RolesByUserAsync(List<string> userIds, CancellationToken ct)
    {
        var pairs = await (
                from ur in _context.Set<IdentityUserRole<string>>().AsNoTracking()
                where userIds.Contains(ur.UserId)
                join r in _context.Set<IdentityRole>().AsNoTracking() on ur.RoleId equals r.Id
                select new { ur.UserId, r.Name })
            .ToListAsync(ct);

        return pairs
            .GroupBy(p => p.UserId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(p => p.Name!).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>The stored names of <paramref name="requested"/> (case-insensitive), or an error naming an unknown role.</summary>
    private async Task<(List<string> Roles, string? Error)> ResolveRolesAsync(IEnumerable<string> requested, CancellationToken ct)
    {
        var names = requested
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0)
            return ([], null);

        var normalizedNames = names.Select(_roles.NormalizeKey).ToList();
        var stored = await _context.Set<IdentityRole>().AsNoTracking()
            .Where(r => normalizedNames.Contains(r.NormalizedName))
            .Select(r => new { r.NormalizedName, r.Name })
            .ToDictionaryAsync(r => r.NormalizedName, r => r.Name, ct);

        var resolved = new List<string>();
        foreach (var (name, normalized) in names.Select(n => (Name: n, Normalized: _roles.NormalizeKey(n))))
        {
            if (!stored.TryGetValue(normalized, out var roleName) || roleName == null)
                return ([], $"Role '{name}' does not exist.");
            resolved.Add(roleName);
        }

        return (resolved, null);
    }

    /// <summary>Rolls the invite/edit transaction back and reports <paramref name="failure"/>.</summary>
    private static async Task<UserAdminResult> RolledBackAsync(IdentityResult failure, IDbContextTransaction tx, CancellationToken ct)
    {
        await tx.RollbackAsync(ct);
        return Fail(failure);
    }

    private async Task<int> RevokeTokensAsync(string userId, CancellationToken ct)
    {
        var live = await _context.Set<ApiTokenDTO>().Where(t => t.UserId == userId && t.RevokedUtc == null).ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var token in live)
            token.RevokedUtc = now;
        await _context.SaveChangesAsync(ct);
        return live.Count;
    }

    private static void ApplyProfile(UserProfileDTO profile, UserProfileInput? input)
    {
        profile.DisplayName = Blank(input?.DisplayName);
        profile.FirstName = Blank(input?.FirstName);
        profile.LastName = Blank(input?.LastName);
    }

    private static string? ValidateEmail(string email) =>
        email.Length == 0 || !new EmailAddressAttribute().IsValid(email) ? "Enter a valid email address." : null;

    private static string? ValidateProfile(UserProfileInput? profile) =>
        new[] { profile?.DisplayName, profile?.FirstName, profile?.LastName }
            .Any(v => Blank(v)?.Length > UserProfileRules.MaxNameLength)
            ? $"Names can be at most {UserProfileRules.MaxNameLength} characters."
            : null;

    private static string? ValidateRoleName(string name)
    {
        if (name.Length == 0)
            return "Enter a role name.";
        return name.Length > UserProfileRules.MaxRoleNameLength
            ? $"A role name can be at most {UserProfileRules.MaxRoleNameLength} characters."
            : null;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static UserAdminResult Fail(IdentityResult result) =>
        UserAdminResult.Fail(string.Join(" ", result.Errors.Select(e => e.Description)));

    private void Audit(string actorId, string action, IdentityUser target) =>
        _logger.Information("User management: {Actor} {Action} {TargetEmail} ({Target})", actorId, action, target.Email, target.Id);
}
