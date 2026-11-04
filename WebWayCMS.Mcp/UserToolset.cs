using System.ComponentModel;
using System.Security.Claims;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using WebWayCMS.Data.Services.Users;
using WebWayCMS.Users;

namespace WebWayCMS.Mcp;

/// <summary>
/// MCP tools for user and role management: everything the Users / User Roles widgets do. Admin only, acting as
/// the token's user. The token counts as authentication, so destructive tools (delete, purge, granting Admin)
/// don't ask for a password the way the admin UI does; the safety rules still apply (never leave no active
/// Admin, no self-destructive actions). A refused action is a protocol error carrying the reason.
/// </summary>
[McpServerToolType]
public sealed class UserToolset
{
    private readonly IUserAdminService _users;
    private readonly IUserManagementModel _model;
    private readonly IMcpCaller _caller;

    public UserToolset(IUserAdminService users, IUserManagementModel model, IMcpCaller caller)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _caller = caller ?? throw new ArgumentNullException(nameof(caller));
    }

    // ----- reads -------------------------------------------------------------------------------------------

    [McpServerTool(Name = "list_users", ReadOnly = true, OpenWorld = false)]
    [Description("Lists users by email, with their roles, status (active, unconfirmed, locked, deleted), whether an invitation is pending, and last login. Deleted users are left out unless status is \"deleted\". Paged: total is the match count.")]
    public async Task<UserListPage> ListUsers(
        [Description("Only members of this role.")] string? role = null,
        [Description("Only users with this status: active, unconfirmed, locked or deleted.")] string? status = null,
        [Description("How many to skip (default 0).")] int skip = 0,
        [Description("How many to return (default 25, max 200).")] int take = UserListQuery.DefaultTake,
        CancellationToken ct = default)
    {
        McpToolHelpers.RequireAdmin(_caller);
        return await _users.ListAsync(new UserListQuery(role, ParseStatus(status), skip, take), ct);
    }

    [McpServerTool(Name = "get_user", ReadOnly = true, OpenWorld = false)]
    [Description("Gets one user: email, phone, profile names, roles, status, and sign-in details (password set, 2FA, passkey count, external logins, active API tokens, lockout, last login).")]
    public async Task<UserDetail> GetUser(
        [Description("The user's id (from list_users).")] string userId,
        CancellationToken ct = default)
    {
        McpToolHelpers.RequireAdmin(_caller);
        return await _users.GetAsync(userId, ct) ?? throw new McpException($"No user found with id '{userId}'.");
    }

    [McpServerTool(Name = "list_roles", ReadOnly = true, OpenWorld = false)]
    [Description("Lists roles with their member counts. Built-in roles (Admin, Editor, User) can't be renamed or deleted.")]
    public async Task<IReadOnlyList<RoleSummary>> ListRoles(CancellationToken ct = default)
    {
        McpToolHelpers.RequireAdmin(_caller);
        return await _users.ListRolesAsync(ct);
    }

    // ----- users -------------------------------------------------------------------------------------------

    [McpServerTool(Name = "invite_user", Destructive = false, OpenWorld = true)]
    [Description("Invites a user: creates the account (no password, unconfirmed) with the profile and roles, and emails a link to set a password, which also confirms the email. The link is valid for a limited time (7 days by default); resend_invitation sends a new one.")]
    public async Task<McpUserActionResult> InviteUser(
        [Description("The new user's email address (also their username).")] string email,
        [Description("Roles to grant, e.g. [\"User\"].")] string[]? roles = null,
        [Description("Display name.")] string? displayName = null,
        [Description("First name.")] string? firstName = null,
        [Description("Last name.")] string? lastName = null,
        CancellationToken ct = default)
        => Done(await _model.InviteAsync(
            new UserInvite(email, new UserProfileInput(displayName, firstName, lastName), roles ?? []), Actor(), ct));

    [McpServerTool(Name = "update_user", Destructive = false, OpenWorld = true)]
    [Description("Updates a user's email, phone or profile names. Omitted fields keep their value; an empty string clears a phone or name. A new email also becomes the username and must be confirmed: a confirmation is sent to it and the old address is notified.")]
    public async Task<McpUserActionResult> UpdateUser(
        [Description("The user's id.")] string userId,
        [Description("New email address.")] string? email = null,
        [Description("Phone number (empty clears it).")] string? phoneNumber = null,
        [Description("Display name (empty clears it).")] string? displayName = null,
        [Description("First name (empty clears it).")] string? firstName = null,
        [Description("Last name (empty clears it).")] string? lastName = null,
        CancellationToken ct = default)
    {
        var actor = Actor();
        var current = await _users.GetAsync(userId, ct) ?? throw new McpException($"No user found with id '{userId}'.");
        var update = new UserUpdate(
            email ?? current.Email,
            phoneNumber ?? current.PhoneNumber,
            new UserProfileInput(displayName ?? current.DisplayName, firstName ?? current.FirstName, lastName ?? current.LastName));
        return Done(await _model.UpdateAsync(userId, update, actor, ct));
    }

    [McpServerTool(Name = "set_user_roles", Destructive = false, OpenWorld = false)]
    [Description("Makes a user's roles exactly the given list (an empty list removes all). Refused if it would remove the Admin role from your own account or leave no active Admin.")]
    public async Task<McpUserActionResult> SetUserRoles(
        [Description("The user's id.")] string userId,
        [Description("The complete list of roles the user should have.")] string[] roles,
        CancellationToken ct = default)
        => Done(await _model.SetRolesAsync(userId, roles ?? [], Actor(), ct));

    [McpServerTool(Name = "send_password_reset", Destructive = false, OpenWorld = true)]
    [Description("Emails the user a password reset link (the same email as Forgot password). For a user who hasn't accepted an invitation yet, use resend_invitation.")]
    public async Task<McpUserActionResult> SendPasswordReset([Description("The user's id.")] string userId, CancellationToken ct = default)
        => Done(await _model.SendPasswordResetAsync(userId, Actor(), ct));

    [McpServerTool(Name = "send_confirmation_email", Destructive = false, OpenWorld = true)]
    [Description("Emails the user a new email confirmation link (only while the email is unconfirmed).")]
    public async Task<McpUserActionResult> SendConfirmationEmail([Description("The user's id.")] string userId, CancellationToken ct = default)
        => Done(await _model.SendConfirmationAsync(userId, Actor(), ct));

    [McpServerTool(Name = "resend_invitation", Destructive = false, OpenWorld = true)]
    [Description("Emails a new invitation link to a user who hasn't accepted their invitation yet.")]
    public async Task<McpUserActionResult> ResendInvitation([Description("The user's id.")] string userId, CancellationToken ct = default)
        => Done(await _model.ResendInvitationAsync(userId, Actor(), ct));

    [McpServerTool(Name = "confirm_user_email", Destructive = false, OpenWorld = false)]
    [Description("Marks the user's email as confirmed without sending an email.")]
    public async Task<McpUserActionResult> ConfirmUserEmail([Description("The user's id.")] string userId, CancellationToken ct = default)
        => Done(await _model.ConfirmEmailAsync(userId, Actor(), ct));

    [McpServerTool(Name = "lock_user", Destructive = false, OpenWorld = false)]
    [Description("Locks the account until it is unlocked (the user can't sign in) and ends its sessions at their next check. Refused for your own account or the last active Admin.")]
    public async Task<McpUserActionResult> LockUser([Description("The user's id.")] string userId, CancellationToken ct = default)
        => Done(await _model.LockAsync(userId, Actor(), ct));

    [McpServerTool(Name = "unlock_user", Destructive = false, OpenWorld = false)]
    [Description("Clears any lockout (an Admin's lock or one from failed sign-in attempts) and the failed-attempt count.")]
    public async Task<McpUserActionResult> UnlockUser([Description("The user's id.")] string userId, CancellationToken ct = default)
        => Done(await _model.UnlockAsync(userId, Actor(), ct));

    [McpServerTool(Name = "sign_out_user_everywhere", Destructive = false, OpenWorld = false)]
    [Description("Invalidates every session of the user (they are signed out when their session is next validated).")]
    public async Task<McpUserActionResult> SignOutUserEverywhere([Description("The user's id.")] string userId, CancellationToken ct = default)
        => Done(await _model.SignOutEverywhereAsync(userId, Actor(), ct));

    [McpServerTool(Name = "reset_user_2fa", Destructive = true, OpenWorld = false)]
    [Description("Turns off the user's two-factor authentication and discards their authenticator key and recovery codes.")]
    public async Task<McpUserActionResult> ResetUserTwoFactor([Description("The user's id.")] string userId, CancellationToken ct = default)
        => Done(await _model.ResetTwoFactorAsync(userId, Actor(), ct));

    [McpServerTool(Name = "revoke_user_api_tokens", Destructive = true, OpenWorld = false)]
    [Description("Revokes every API token the user holds.")]
    public async Task<McpUserActionResult> RevokeUserApiTokens([Description("The user's id.")] string userId, CancellationToken ct = default)
        => Done(await _model.RevokeApiTokensAsync(userId, Actor(), ct));

    [McpServerTool(Name = "remove_user_passkeys", Destructive = true, OpenWorld = false)]
    [Description("Removes every passkey registered to the user.")]
    public async Task<McpUserActionResult> RemoveUserPasskeys([Description("The user's id.")] string userId, CancellationToken ct = default)
        => Done(await _model.RemovePasskeysAsync(userId, Actor(), ct));

    [McpServerTool(Name = "remove_user_external_login", Destructive = true, OpenWorld = false)]
    [Description("Unlinks one external login (e.g. GitHub) from the user; get_user lists them.")]
    public async Task<McpUserActionResult> RemoveUserExternalLogin(
        [Description("The user's id.")] string userId,
        [Description("The login's provider (loginProvider from get_user).")] string loginProvider,
        [Description("The login's provider key (providerKey from get_user).")] string providerKey,
        CancellationToken ct = default)
        => Done(await _model.RemoveExternalLoginAsync(userId, loginProvider, providerKey, Actor(), ct));

    [McpServerTool(Name = "delete_user", Destructive = true, OpenWorld = false)]
    [Description("Deletes a user softly: locked, hidden from list_users, API tokens revoked; restore_user brings it back and purge_user removes it for good. Refused for your own account or the last active Admin.")]
    public async Task<McpUserActionResult> DeleteUser([Description("The user's id.")] string userId, CancellationToken ct = default)
        => Done(await _model.DeleteAsync(userId, Actor(), ct));

    [McpServerTool(Name = "restore_user", Destructive = false, OpenWorld = false)]
    [Description("Restores a deleted user and unlocks the account.")]
    public async Task<McpUserActionResult> RestoreUser([Description("The user's id.")] string userId, CancellationToken ct = default)
        => Done(await _model.RestoreAsync(userId, Actor(), ct));

    [McpServerTool(Name = "purge_user", Destructive = true, OpenWorld = false)]
    [Description("Permanently deletes a user that delete_user already deleted: the account, roles, logins, passkeys, API tokens and profile. Frees the email. Cannot be undone.")]
    public async Task<McpUserActionResult> PurgeUser([Description("The user's id.")] string userId, CancellationToken ct = default)
        => Done(await _model.PurgeAsync(userId, Actor(), ct));

    // ----- roles -------------------------------------------------------------------------------------------

    [McpServerTool(Name = "create_role", Destructive = false, OpenWorld = false)]
    [Description("Creates a custom role.")]
    public async Task<McpUserActionResult> CreateRole([Description("The role's name.")] string name, CancellationToken ct = default)
        => Done(await _model.CreateRoleAsync(name, Actor(), ct));

    [McpServerTool(Name = "rename_role", Destructive = false, OpenWorld = false)]
    [Description("Renames a custom role (members keep it). Built-in roles can't be renamed.")]
    public async Task<McpUserActionResult> RenameRole(
        [Description("The role's current name.")] string name,
        [Description("The new name.")] string newName,
        CancellationToken ct = default)
        => Done(await _model.RenameRoleAsync(name, newName, Actor(), ct));

    [McpServerTool(Name = "delete_role", Destructive = true, OpenWorld = false)]
    [Description("Deletes a custom role that has no members. Built-in roles can't be deleted.")]
    public async Task<McpUserActionResult> DeleteRole([Description("The role's name.")] string name, CancellationToken ct = default)
        => Done(await _model.DeleteRoleAsync(name, Actor(), ct));

    /// <summary>The calling Admin, acting without password re-entry (the token authenticated the call).</summary>
    private UserActor Actor()
    {
        McpToolHelpers.RequireAdmin(_caller);
        return new UserActor(_caller.User!.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty, RequirePassword: false);
    }

    private static UserStatus? ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return null;
        return Enum.TryParse<UserStatus>(status, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new McpException($"Unknown status '{status}'. Use active, unconfirmed, locked or deleted.");
    }

    private static McpUserActionResult Done(UserManagementResult result) =>
        result.Success ? new McpUserActionResult(result.Message, result.UserId) : throw new McpException(result.Message);
}
