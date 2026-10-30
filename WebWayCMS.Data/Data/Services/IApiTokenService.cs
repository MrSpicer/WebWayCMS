using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>A token row with its owner's email, for the admin listing.</summary>
public sealed record ApiTokenListItem(ApiTokenDTO Token, string? UserEmail);

/// <summary>
/// Issues, validates and revokes MCP personal access tokens. Only token hashes are stored.
/// </summary>
public interface IApiTokenService
{
    /// <summary>
    /// Issues a new token for <paramref name="userId"/>. The plaintext is returned once and never stored.
    /// A name is unique among the user's unexpired, unrevoked tokens; an expired token of the same name is
    /// revoked to free it.
    /// </summary>
    Task<(bool Success, string? ErrorMessage, string? PlainToken, ApiTokenDTO? Token)> CreateAsync(
        string userId, string name, DateTime? expiresUtc, CancellationToken ct = default);

    /// <summary>
    /// The token row for <paramref name="plainToken"/> when it is known, not revoked and not expired;
    /// otherwise null. Records the use (at most once per minute).
    /// </summary>
    Task<ApiTokenDTO?> ValidateAsync(string? plainToken, CancellationToken ct = default);

    /// <summary>The user's tokens, newest first.</summary>
    Task<IReadOnlyList<ApiTokenDTO>> ListForUserAsync(string userId, CancellationToken ct = default);

    /// <summary>Every user's tokens with the owner's email, newest first.</summary>
    Task<IReadOnlyList<ApiTokenListItem>> ListAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Revokes a token. The owner may revoke their own; <paramref name="actingIsAdmin"/> may revoke any.
    /// Revoking an already-revoked token succeeds without changing it.
    /// </summary>
    Task<(bool Success, string? ErrorMessage)> RevokeAsync(
        Guid tokenId, string actingUserId, bool actingIsAdmin, CancellationToken ct = default);

    /// <summary>
    /// Makes <paramref name="plainToken"/> a live token named <paramref name="name"/> for the user
    /// (development seeding). Idempotent: the row already holding that plaintext is renamed and revived
    /// (refused when it belongs to another user); otherwise an existing token of that name is re-pointed
    /// at the plaintext and un-revoked. Any other live token holding the name is revoked.
    /// </summary>
    Task<(bool Success, string? ErrorMessage)> UpsertSeedTokenAsync(
        string userId, string name, string plainToken, CancellationToken ct = default);
}
