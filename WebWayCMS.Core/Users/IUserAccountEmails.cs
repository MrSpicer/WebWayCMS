using Microsoft.AspNetCore.Identity;

using WebWayCMS.Identity;

namespace WebWayCMS.Users;

/// <summary>
/// The account emails an Admin's user-management actions send. Each returns whether the message went out; a
/// send failure is logged and never thrown (<see cref="EmailSenderExtensions.TrySendEmailAsync"/>). The
/// confirmation and reset emails match the scaffolded Register / ForgotPassword pages' wording, so a user's
/// mail looks the same however it was triggered.
/// </summary>
public interface IUserAccountEmails
{
    Task<bool> SendInvitationAsync(IdentityUser user);

    Task<bool> SendConfirmationAsync(IdentityUser user);

    Task<bool> SendPasswordResetAsync(IdentityUser user);

    /// <summary>Tells <paramref name="previousEmail"/> that an administrator changed the account's email.</summary>
    Task<bool> SendEmailChangedNoticeAsync(string previousEmail, string newEmail);
}
