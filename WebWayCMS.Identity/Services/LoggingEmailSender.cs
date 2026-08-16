using Microsoft.AspNetCore.Identity.UI.Services;

using Serilog;

namespace WebWayCMS.Identity;

/// <summary>
/// Fallback <see cref="IEmailSender"/> used when SMTP is unconfigured (<c>Smtp:Host</c> is blank). It
/// never throws — it logs the message to Serilog so a developer can follow the confirmation link in the
/// log, and records a warning that the message was not actually delivered.
/// </summary>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger _logger = Log.ForContext<LoggingEmailSender>();

    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        _logger.Information("[Email] To: {Email} Subject: {Subject}", email, subject);
        _logger.Information("[Email] Message: {Message}", htmlMessage);
        _logger.Warning(
            "Email was not delivered: SMTP is not configured (set the 'Smtp:Host' configuration key). " +
            "The message was written to the log instead.");

        return Task.CompletedTask;
    }
}
