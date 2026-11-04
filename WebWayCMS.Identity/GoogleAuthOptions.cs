namespace WebWayCMS.Identity;

/// <summary>
/// Configuration for Google external login, bound from the "Authentication:Google" section.
/// </summary>
public sealed class GoogleAuthOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Authentication:Google";

    /// <summary>OAuth client id. Supply via user-secrets/environment, never source control.</summary>
    public string? ClientId { get; set; }

    /// <summary>OAuth client secret. Supply via user-secrets/environment, never source control.</summary>
    public string? ClientSecret { get; set; }
}
