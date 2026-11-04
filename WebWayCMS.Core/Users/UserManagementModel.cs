using Microsoft.AspNetCore.Identity;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services.Users;

namespace WebWayCMS.Users;

public sealed class UserManagementModel : IUserManagementModel
{
    internal const string NoPasswordMessage =
        "Set a password on your account (Account → Password) before you delete users or grant the Admin role.";
    internal const string WrongPasswordMessage = "Your password is incorrect.";
    internal const string ActorLockedMessage = "Your account is locked. Contact an administrator.";
    internal const string EmailFailedSuffix = " The email could not be sent; check the mail settings and try again.";

    private readonly IUserAdminService _service;
    private readonly IUserAccountEmails _emails;
    private readonly UserManager<IdentityUser> _users;
    private readonly SignInManager<IdentityUser> _signIn;

    public UserManagementModel(
        IUserAdminService service,
        IUserAccountEmails emails,
        UserManager<IdentityUser> users,
        SignInManager<IdentityUser> signIn)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _emails = emails ?? throw new ArgumentNullException(nameof(emails));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _signIn = signIn ?? throw new ArgumentNullException(nameof(signIn));
    }

    public async Task<UserManagementResult> InviteAsync(UserInvite invite, UserActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(invite);
        if (invite.Roles.Contains(UserProfileRules.AdminRole, StringComparer.OrdinalIgnoreCase)
            && await VerifyPasswordAsync(actor) is { } denied)
            return denied;

        var result = await _service.InviteAsync(invite, actor.UserId, ct);
        if (!result.Success)
            return Failed(result);

        var user = await _users.FindByIdAsync(result.UserId!);
        var sent = await _emails.SendInvitationAsync(user!);
        return Sent(sent, $"Invited {user!.Email}.", user.Id);
    }

    public async Task<UserManagementResult> ResendInvitationAsync(string userId, UserActor actor, CancellationToken ct = default)
    {
        var detail = await _service.GetAsync(userId, ct);
        if (detail == null)
            return new UserManagementResult(false, UserAdminService.NotFoundMessage);
        if (!detail.IsInvitePending || detail.Status == UserStatus.Deleted)
            return new UserManagementResult(false, "This user has no pending invitation.");

        var user = await _users.FindByIdAsync(userId);
        return Sent(await _emails.SendInvitationAsync(user!), $"Sent a new invitation to {user!.Email}.", userId);
    }

    public async Task<UserManagementResult> UpdateAsync(string userId, UserUpdate update, UserActor actor, CancellationToken ct = default)
    {
        var result = await _service.UpdateAsync(userId, update, actor.UserId, ct);
        if (!result.Success)
            return Failed(result);
        if (result.PreviousEmail == null)
            return new UserManagementResult(true, "Saved.", userId);

        var user = await _users.FindByIdAsync(userId);
        var detail = await _service.GetAsync(userId, ct);
        if (detail?.IsInvitePending == true)
        {
            // The email change rotated the security stamp, which killed the old invitation link, and a plain
            // confirmation would strand the user without a password. Re-invite instead. The old address was never
            // confirmed (often the typo being fixed), so it gets no change notice.
            var invited = await _emails.SendInvitationAsync(user!);
            return Sent(invited, $"Saved. A new invitation was sent to {user!.Email}.", userId);
        }

        var confirmed = await _emails.SendConfirmationAsync(user!);
        var noticed = await _emails.SendEmailChangedNoticeAsync(result.PreviousEmail, user!.Email!);
        var message = $"Saved. {user.Email} must be confirmed before the user can sign in."
            + (confirmed
                ? " A confirmation email was sent."
                : " The confirmation email could not be sent; check the mail settings and try again.")
            + (noticed
                ? $" A notification was sent to {result.PreviousEmail}."
                : $" The notification to {result.PreviousEmail} could not be sent; check the mail settings and try again.");
        return new UserManagementResult(true, message, userId);
    }

    public async Task<UserManagementResult> SetRolesAsync(string userId, IReadOnlyCollection<string> roles, UserActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(roles);
        if (roles.Contains(UserProfileRules.AdminRole, StringComparer.OrdinalIgnoreCase))
        {
            var user = await _users.FindByIdAsync(userId);
            if (user != null && !await _users.IsInRoleAsync(user, UserProfileRules.AdminRole)
                && await VerifyPasswordAsync(actor) is { } denied)
                return denied;
        }

        return Done(await _service.SetRolesAsync(userId, roles, actor.UserId, ct), "Roles saved.");
    }

    public async Task<UserManagementResult> SendPasswordResetAsync(string userId, UserActor actor, CancellationToken ct = default)
    {
        var detail = await _service.GetAsync(userId, ct);
        if (detail == null)
            return new UserManagementResult(false, UserAdminService.NotFoundMessage);
        if (detail.Status == UserStatus.Deleted)
            return new UserManagementResult(false, UserAdminService.DeletedMessage);
        if (detail.IsInvitePending)
            return new UserManagementResult(false, "This user hasn't accepted their invitation yet. Resend the invitation instead.");

        var user = await _users.FindByIdAsync(userId);
        return Sent(await _emails.SendPasswordResetAsync(user!), $"Sent a password reset email to {user!.Email}.", userId);
    }

    public async Task<UserManagementResult> SendConfirmationAsync(string userId, UserActor actor, CancellationToken ct = default)
    {
        var detail = await _service.GetAsync(userId, ct);
        if (detail == null)
            return new UserManagementResult(false, UserAdminService.NotFoundMessage);
        if (detail.Status == UserStatus.Deleted)
            return new UserManagementResult(false, UserAdminService.DeletedMessage);
        if (detail.EmailConfirmed)
            return new UserManagementResult(false, "This email is already confirmed.");

        var user = await _users.FindByIdAsync(userId);
        return Sent(await _emails.SendConfirmationAsync(user!), $"Sent a confirmation email to {user!.Email}.", userId);
    }

    public async Task<UserManagementResult> ConfirmEmailAsync(string userId, UserActor actor, CancellationToken ct = default) =>
        Done(await _service.ConfirmEmailAsync(userId, actor.UserId, ct), "Email marked as confirmed.");

    public async Task<UserManagementResult> LockAsync(string userId, UserActor actor, CancellationToken ct = default) =>
        Done(await _service.LockAsync(userId, actor.UserId, ct), "Account locked. The user can't sign in until it is unlocked.");

    public async Task<UserManagementResult> UnlockAsync(string userId, UserActor actor, CancellationToken ct = default) =>
        Done(await _service.UnlockAsync(userId, actor.UserId, ct), "Account unlocked.");

    public async Task<UserManagementResult> SignOutEverywhereAsync(string userId, UserActor actor, CancellationToken ct = default) =>
        Done(await _service.SignOutEverywhereAsync(userId, actor.UserId, ct), "The user will be signed out of every session.");

    public async Task<UserManagementResult> ResetTwoFactorAsync(string userId, UserActor actor, CancellationToken ct = default) =>
        Done(await _service.ResetTwoFactorAsync(userId, actor.UserId, ct), "Two-factor authentication was reset.");

    public async Task<UserManagementResult> RevokeApiTokensAsync(string userId, UserActor actor, CancellationToken ct = default) =>
        Done(await _service.RevokeApiTokensAsync(userId, actor.UserId, ct), "API tokens revoked.");

    public async Task<UserManagementResult> RemovePasskeysAsync(string userId, UserActor actor, CancellationToken ct = default) =>
        Done(await _service.RemovePasskeysAsync(userId, actor.UserId, ct), "Passkeys removed.");

    public async Task<UserManagementResult> RemoveExternalLoginAsync(
        string userId, string loginProvider, string providerKey, UserActor actor, CancellationToken ct = default) =>
        Done(await _service.RemoveExternalLoginAsync(userId, loginProvider, providerKey, actor.UserId, ct), $"{loginProvider} login removed.");

    public async Task<UserManagementResult> DeleteAsync(string userId, UserActor actor, CancellationToken ct = default)
    {
        if (await VerifyPasswordAsync(actor) is { } denied)
            return denied;
        return Done(await _service.SoftDeleteAsync(userId, actor.UserId, ct), "User deleted. Restore it, or delete it permanently.");
    }

    public async Task<UserManagementResult> RestoreAsync(string userId, UserActor actor, CancellationToken ct = default) =>
        Done(await _service.RestoreAsync(userId, actor.UserId, ct), "User restored.");

    public async Task<UserManagementResult> PurgeAsync(string userId, UserActor actor, CancellationToken ct = default)
    {
        if (await VerifyPasswordAsync(actor) is { } denied)
            return denied;
        return Done(await _service.PurgeAsync(userId, actor.UserId, ct), "User permanently deleted.");
    }

    public async Task<UserManagementResult> CreateRoleAsync(string name, UserActor actor, CancellationToken ct = default) =>
        Done(await _service.CreateRoleAsync(name, actor.UserId, ct), $"Role '{name?.Trim()}' created.");

    public async Task<UserManagementResult> RenameRoleAsync(string name, string newName, UserActor actor, CancellationToken ct = default) =>
        Done(await _service.RenameRoleAsync(name, newName, actor.UserId, ct), $"Role renamed to '{newName?.Trim()}'.");

    public async Task<UserManagementResult> DeleteRoleAsync(string name, UserActor actor, CancellationToken ct = default) =>
        Done(await _service.DeleteRoleAsync(name, actor.UserId, ct), $"Role '{name}' deleted.");

    /// <summary>Null when the actor may proceed; otherwise the refusal.</summary>
    private async Task<UserManagementResult?> VerifyPasswordAsync(UserActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.RequirePassword)
            return null;

        var user = await _users.FindByIdAsync(actor.UserId);
        if (user == null || !await _users.HasPasswordAsync(user))
            return new UserManagementResult(false, NoPasswordMessage);

        var check = await _signIn.CheckPasswordSignInAsync(user, actor.Password ?? string.Empty, lockoutOnFailure: true);
        if (check.Succeeded)
            return null;
        return new UserManagementResult(false, check.IsLockedOut ? ActorLockedMessage : WrongPasswordMessage);
    }

    private static UserManagementResult Done(UserAdminResult result, string successMessage) =>
        result.Success ? new UserManagementResult(true, successMessage, result.UserId) : Failed(result);

    private static UserManagementResult Failed(UserAdminResult result) =>
        new(false, result.ErrorMessage ?? "The action failed.", result.UserId);

    /// <summary>The action itself succeeded; the message says whether its email went out.</summary>
    private static UserManagementResult Sent(bool sent, string message, string userId) =>
        new(true, sent ? message : message + EmailFailedSuffix, userId);
}
