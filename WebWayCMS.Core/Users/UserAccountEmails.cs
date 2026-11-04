using System.Text;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

using WebWayCMS.Identity;

namespace WebWayCMS.Users;

public sealed class UserAccountEmails : IUserAccountEmails
{
    public const string InvitationSubject = "You're invited";
    public const string ConfirmationSubject = "Confirm your email";
    public const string ResetSubject = "Reset Password";
    public const string EmailChangedSubject = "Your account email was changed";

    private readonly UserManager<IdentityUser> _users;
    private readonly IEmailSender _sender;
    private readonly IUserAccountLinks _links;
    private readonly ILogger<UserAccountEmails> _logger;

    public UserAccountEmails(
        UserManager<IdentityUser> users,
        IEmailSender sender,
        IUserAccountLinks links,
        ILogger<UserAccountEmails> logger)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _links = links ?? throw new ArgumentNullException(nameof(links));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> SendInvitationAsync(IdentityUser user)
    {
        var code = Encode(await _users.GenerateUserTokenAsync(user, InvitationTokenProvider.ProviderName, InvitationTokenProvider.Purpose));
        var link = _links.AcceptInvitation(user.Id, code);
        return await SendAsync(user.Email!, InvitationSubject,
            $"An administrator created an account for you. Set your password by <a href='{HtmlEncoder.Default.Encode(link)}'>clicking here</a>.");
    }

    public async Task<bool> SendConfirmationAsync(IdentityUser user)
    {
        var code = Encode(await _users.GenerateEmailConfirmationTokenAsync(user));
        var link = _links.ConfirmEmail(user.Id, code);
        return await SendAsync(user.Email!, ConfirmationSubject,
            $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(link)}'>clicking here</a>.");
    }

    public async Task<bool> SendPasswordResetAsync(IdentityUser user)
    {
        var code = Encode(await _users.GeneratePasswordResetTokenAsync(user));
        var link = _links.ResetPassword(code);
        return await SendAsync(user.Email!, ResetSubject,
            $"Please reset your password by <a href='{HtmlEncoder.Default.Encode(link)}'>clicking here</a>.");
    }

    public Task<bool> SendEmailChangedNoticeAsync(string previousEmail, string newEmail) =>
        SendAsync(previousEmail, EmailChangedSubject,
            $"An administrator changed your account's email address to {HtmlEncoder.Default.Encode(newEmail)}. " +
            "If you didn't expect this, contact the site administrator.");

    private static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    private Task<bool> SendAsync(string email, string subject, string html) =>
        _sender.TrySendEmailAsync(email, subject, html, _logger);
}
