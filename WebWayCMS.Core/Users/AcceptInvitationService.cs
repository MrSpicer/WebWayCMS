using System.Text;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

using Serilog;

namespace WebWayCMS.Users;

/// <summary>
/// The default <see cref="IAcceptInvitationService"/>. The link carries an <see cref="InvitationTokenProvider"/>
/// token; it stops working once a password is set (that rotates the security stamp the token is bound to),
/// when it expires, or when the account is locked or deleted. Every bad link gets the same treatment.
/// </summary>
public sealed class AcceptInvitationService : IAcceptInvitationService
{
    /// <summary>The password was saved but the email-confirm write failed, and the link is now dead.</summary>
    internal const string ConfirmationFailedMessage =
        "Your password was saved, but your email could not be marked as confirmed. Ask an administrator to send you a confirmation email.";

    private readonly UserManager<IdentityUser> _users;
    private readonly ILogger _logger = Log.ForContext<AcceptInvitationService>();

    public AcceptInvitationService(UserManager<IdentityUser> users)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
    }

    public async Task<InvitationPreview?> PreviewAsync(string? userId, string? code, CancellationToken ct = default)
    {
        var user = await UsableInvitationAsync(userId, code);
        return user == null ? null : new InvitationPreview(user.Email!);
    }

    public async Task<AcceptInvitationResult> AcceptAsync(string? userId, string? code, string password, CancellationToken ct = default)
    {
        var user = await UsableInvitationAsync(userId, code);
        if (user == null)
            return AcceptInvitationResult.InvalidLink();

        var added = await _users.AddPasswordAsync(user, password);
        if (!added.Succeeded)
            return AcceptInvitationResult.Rejected(
                user.Email!, added.Errors.Select(e => e.Description).ToList());

        user.EmailConfirmed = true;
        var confirmed = await _users.UpdateAsync(user);
        if (!confirmed.Succeeded)
        {
            _logger.Error(
                "Invited user {UserId} set a password, but the email-confirmation write failed: {Errors}",
                user.Id, string.Join("; ", confirmed.Errors.Select(e => e.Description)));
            return AcceptInvitationResult.ConfirmFailed(user.Email!, ConfirmationFailedMessage);
        }

        _logger.Information("Invited user {UserId} accepted the invitation.", user.Id);
        return AcceptInvitationResult.Accept(user.Email!);
    }

    /// <summary>The invited user when the link is still good: a passwordless, unlocked account and a valid token.</summary>
    private async Task<IdentityUser?> UsableInvitationAsync(string? userId, string? code)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(code))
            return null;

        var user = await _users.FindByIdAsync(userId);
        if (user == null || await _users.HasPasswordAsync(user) || await _users.IsLockedOutAsync(user))
            return null;

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        }
        catch (FormatException)
        {
            return null;
        }

        return await _users.VerifyUserTokenAsync(user, InvitationTokenProvider.ProviderName, InvitationTokenProvider.Purpose, token)
            ? user
            : null;
    }
}
