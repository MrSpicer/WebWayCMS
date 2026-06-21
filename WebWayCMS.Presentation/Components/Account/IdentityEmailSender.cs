using Microsoft.AspNetCore.Identity;

using UIEmailSender = Microsoft.AspNetCore.Identity.UI.Services.IEmailSender;

namespace WebWayCMS.Presentation.Components.Account;

/// <summary>
/// Adapts the application's simple <see cref="UIEmailSender"/> (e.g. DevEmailSender) to the typed
/// <see cref="IEmailSender{TUser}"/> the Blazor Identity components use, composing the confirmation
/// and password-reset messages. Mirrors the standard template's IdentityNoOpEmailSender but delegates
/// to the configured sender so the messages are actually delivered/logged.
/// </summary>
internal sealed class IdentityEmailSender(UIEmailSender emailSender) : IEmailSender<IdentityUser>
{
    public Task SendConfirmationLinkAsync(IdentityUser user, string email, string confirmationLink) =>
        emailSender.SendEmailAsync(email, "Confirm your email",
            $"Please confirm your account by <a href='{confirmationLink}'>clicking here</a>.");

    public Task SendPasswordResetLinkAsync(IdentityUser user, string email, string resetLink) =>
        emailSender.SendEmailAsync(email, "Reset your password",
            $"Please reset your password by <a href='{resetLink}'>clicking here</a>.");

    public Task SendPasswordResetCodeAsync(IdentityUser user, string email, string resetCode) =>
        emailSender.SendEmailAsync(email, "Reset your password",
            $"Please reset your password using the following code: {resetCode}");
}
