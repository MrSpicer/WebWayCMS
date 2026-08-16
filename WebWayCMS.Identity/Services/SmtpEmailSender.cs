using System.Net;
using System.Net.Mail;

using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;

namespace WebWayCMS.Identity;

/// <summary>
/// Real <see cref="IEmailSender"/> built on the BCL's <see cref="SmtpClient"/> (no extra package).
/// Registered unconditionally; it only fails if a send is attempted while <c>Smtp:Host</c> is unset.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    private readonly Func<SmtpClient> _clientFactory;

    public SmtpEmailSender(IOptions<SmtpOptions> options)
        : this(options, static () => new SmtpClient())
    {
    }

    internal SmtpEmailSender(IOptions<SmtpOptions> options, Func<SmtpClient> clientFactory)
    {
        _options = options.Value;
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            throw new InvalidOperationException(
                "SMTP is not configured. Set the 'Smtp:Host' configuration key to enable email delivery.");
        }

        if (string.IsNullOrWhiteSpace(_options.FromAddress))
        {
            throw new InvalidOperationException(
                "SMTP 'FromAddress' is not configured. Set the 'Smtp:FromAddress' configuration key to enable email delivery.");
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress!, _options.FromName),
            Subject = subject,
            Body = htmlMessage,
            IsBodyHtml = true,
        };
        message.To.Add(email);

        using var client = _clientFactory();
        client.Host = _options.Host;
        client.Port = _options.Port;
        client.EnableSsl = _options.EnableSsl;

        if (!string.IsNullOrEmpty(_options.UserName))
        {
            client.Credentials = new NetworkCredential(_options.UserName, _options.Password);
        }

        await client.SendMailAsync(message);
    }
}
