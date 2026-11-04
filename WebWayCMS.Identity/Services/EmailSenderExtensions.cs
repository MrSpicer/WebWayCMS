using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;

namespace WebWayCMS.Identity;

/// <summary>
/// Non-fatal sending for the account pages: an unreachable or misconfigured SMTP server must not turn a
/// registration or password reset into an error page after the account change already happened. The user
/// can retry through "Resend email confirmation" / "Forgot password" once mail works again.
/// </summary>
public static class EmailSenderExtensions
{
    /// <summary>
    /// Sends the message and returns <c>true</c>, or logs the failure (recipient and subject only — the
    /// body carries a confirmation or reset token) and returns <c>false</c>. Never throws.
    /// </summary>
    public static async Task<bool> TrySendEmailAsync(
        this IEmailSender sender,
        string email,
        string subject,
        string htmlMessage,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            await sender.SendEmailAsync(email, subject, htmlMessage);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email '{Subject}' to {Email}.", subject, email);
            return false;
        }
    }
}
