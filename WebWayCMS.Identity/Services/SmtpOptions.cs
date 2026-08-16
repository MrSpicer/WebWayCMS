namespace WebWayCMS.Identity;

/// <summary>
/// Configuration for the built-in SMTP email sender, bound from the "Smtp" configuration section.
/// </summary>
public sealed class SmtpOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Smtp";

    /// <summary>SMTP server host. Empty/omitted means email is not deliverable; sending then throws.</summary>
    public string? Host { get; set; }

    /// <summary>SMTP server port. Defaults to 587 (submission over TLS).</summary>
    public int Port { get; set; } = 587;

    /// <summary>Whether to use TLS (STARTTLS/SMTPS). Defaults to true.</summary>
    public bool EnableSsl { get; set; } = true;

    /// <summary>Optional SMTP user name for authenticated relays.</summary>
    public string? UserName { get; set; }

    /// <summary>Optional SMTP password. Supply via user-secrets/environment, never source control.</summary>
    public string? Password { get; set; }

    /// <summary>From address used on outgoing mail.</summary>
    public string? FromAddress { get; set; }

    /// <summary>Optional display name for the from address.</summary>
    public string? FromName { get; set; }
}
