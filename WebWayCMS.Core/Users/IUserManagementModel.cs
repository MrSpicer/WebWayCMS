using WebWayCMS.Data.Services.Users;

namespace WebWayCMS.Users;

/// <summary>
/// The user-management actions behind the Users / User Roles widgets' forms and the MCP user tools:
/// <see cref="IUserAdminService"/> plus the account emails and the password re-entry for destructive actions.
/// </summary>
public interface IUserManagementModel
{
    Task<UserManagementResult> InviteAsync(UserInvite invite, UserActor actor, CancellationToken ct = default);

    Task<UserManagementResult> ResendInvitationAsync(string userId, UserActor actor, CancellationToken ct = default);

    Task<UserManagementResult> UpdateAsync(string userId, UserUpdate update, UserActor actor, CancellationToken ct = default);

    Task<UserManagementResult> SetRolesAsync(string userId, IReadOnlyCollection<string> roles, UserActor actor, CancellationToken ct = default);

    Task<UserManagementResult> SendPasswordResetAsync(string userId, UserActor actor, CancellationToken ct = default);

    Task<UserManagementResult> SendConfirmationAsync(string userId, UserActor actor, CancellationToken ct = default);

    Task<UserManagementResult> ConfirmEmailAsync(string userId, UserActor actor, CancellationToken ct = default);

    Task<UserManagementResult> LockAsync(string userId, UserActor actor, CancellationToken ct = default);

    Task<UserManagementResult> UnlockAsync(string userId, UserActor actor, CancellationToken ct = default);

    Task<UserManagementResult> SignOutEverywhereAsync(string userId, UserActor actor, CancellationToken ct = default);

    Task<UserManagementResult> ResetTwoFactorAsync(string userId, UserActor actor, CancellationToken ct = default);

    Task<UserManagementResult> RevokeApiTokensAsync(string userId, UserActor actor, CancellationToken ct = default);

    Task<UserManagementResult> RemovePasskeysAsync(string userId, UserActor actor, CancellationToken ct = default);

    Task<UserManagementResult> RemoveExternalLoginAsync(string userId, string loginProvider, string providerKey, UserActor actor, CancellationToken ct = default);

    Task<UserManagementResult> DeleteAsync(string userId, UserActor actor, CancellationToken ct = default);

    Task<UserManagementResult> RestoreAsync(string userId, UserActor actor, CancellationToken ct = default);

    Task<UserManagementResult> PurgeAsync(string userId, UserActor actor, CancellationToken ct = default);

    Task<UserManagementResult> CreateRoleAsync(string name, UserActor actor, CancellationToken ct = default);

    Task<UserManagementResult> RenameRoleAsync(string name, string newName, UserActor actor, CancellationToken ct = default);

    Task<UserManagementResult> DeleteRoleAsync(string name, UserActor actor, CancellationToken ct = default);
}
