namespace WebWayCMS.Data.Services.Users;

/// <summary>
/// Admin user management over ASP.NET Identity plus the CMS <c>UserProfiles</c> table: listing, editing, roles,
/// account security actions, invitations and soft delete. Every write takes the acting Admin's user id, runs the
/// <see cref="UserManagementGuards"/> (never leave no active Admin, no self-destructive actions) and logs one audit
/// line. Emails and password re-entry are the caller's concern (the Core <c>UserManagementModel</c>). A
/// soft-deleted user refuses every write except <see cref="RestoreAsync"/> and <see cref="PurgeAsync"/>.
/// </summary>
public interface IUserAdminService
{
    Task<UserListPage> ListAsync(UserListQuery query, CancellationToken ct = default);

    Task<UserDetail?> GetAsync(string userId, CancellationToken ct = default);

    Task<IReadOnlyList<RoleSummary>> ListRolesAsync(CancellationToken ct = default);

    /// <summary>Creates a passwordless, unconfirmed user with the profile and roles; the caller sends the invitation.</summary>
    Task<UserAdminResult> InviteAsync(UserInvite invite, string actorId, CancellationToken ct = default);

    /// <summary>Saves email, phone and profile. A changed email is set as the username too and becomes unconfirmed.</summary>
    Task<UserAdminResult> UpdateAsync(string userId, UserUpdate update, string actorId, CancellationToken ct = default);

    /// <summary>Makes the user's roles exactly <paramref name="roles"/>.</summary>
    Task<UserAdminResult> SetRolesAsync(string userId, IReadOnlyCollection<string> roles, string actorId, CancellationToken ct = default);

    Task<UserAdminResult> ConfirmEmailAsync(string userId, string actorId, CancellationToken ct = default);

    /// <summary>Locks the account until an Admin unlocks it, and ends its sessions.</summary>
    Task<UserAdminResult> LockAsync(string userId, string actorId, CancellationToken ct = default);

    /// <summary>Clears any lockout (an Admin's or a failed-attempts one) and the failed-attempt count.</summary>
    Task<UserAdminResult> UnlockAsync(string userId, string actorId, CancellationToken ct = default);

    /// <summary>Rotates the security stamp, so every existing session and remembered 2FA browser is signed out.</summary>
    Task<UserAdminResult> SignOutEverywhereAsync(string userId, string actorId, CancellationToken ct = default);

    /// <summary>Turns 2FA off and discards the authenticator key and recovery codes.</summary>
    Task<UserAdminResult> ResetTwoFactorAsync(string userId, string actorId, CancellationToken ct = default);

    Task<UserAdminResult> RevokeApiTokensAsync(string userId, string actorId, CancellationToken ct = default);

    Task<UserAdminResult> RemovePasskeysAsync(string userId, string actorId, CancellationToken ct = default);

    Task<UserAdminResult> RemoveExternalLoginAsync(string userId, string loginProvider, string providerKey, string actorId, CancellationToken ct = default);

    /// <summary>Marks the user deleted, locks the account, ends its sessions and revokes its API tokens.</summary>
    Task<UserAdminResult> SoftDeleteAsync(string userId, string actorId, CancellationToken ct = default);

    Task<UserAdminResult> RestoreAsync(string userId, string actorId, CancellationToken ct = default);

    /// <summary>Hard-deletes a soft-deleted user (roles, logins, passkeys, tokens and profile go with it), freeing the email.</summary>
    Task<UserAdminResult> PurgeAsync(string userId, string actorId, CancellationToken ct = default);

    Task<UserAdminResult> CreateRoleAsync(string name, string actorId, CancellationToken ct = default);

    Task<UserAdminResult> RenameRoleAsync(string name, string newName, string actorId, CancellationToken ct = default);

    /// <summary>Deletes a custom role that has no members.</summary>
    Task<UserAdminResult> DeleteRoleAsync(string name, string actorId, CancellationToken ct = default);

    /// <summary>Stamps the profile's last sign-in (called by the sign-in manager on every successful sign-in).</summary>
    Task RecordLoginAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Whether the user is soft-deleted (false for an unknown id). A cheap check for the public account pages,
    /// which treat a deleted user as absent.
    /// </summary>
    Task<bool> IsDeletedAsync(string userId, CancellationToken ct = default);
}
