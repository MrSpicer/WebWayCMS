using Microsoft.AspNetCore.Identity.UI.Services;

using Serilog;

namespace WebWayCMS.Services;

// Simple development email sender that logs email content to Serilog and to Logs/email.log
public class DevEmailSender : IEmailSender
{
    private readonly Serilog.ILogger _logger = Log.ForContext<DevEmailSender>();

    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        // Log a concise entry for easy discovery
        _logger.Information("[DevEmail] To: {Email} Subject: {Subject}", email, subject);
        // Also log the full message at Debug level so it's available when needed
        _logger.Debug("[DevEmail] FullMessage: {Message}", htmlMessage);

        // Rely on Serilog sinks to persist logs (file/console/etc.). No direct file I/O here.
        return Task.CompletedTask;
    }
}