namespace WebWayCMS.Identity;

/// <summary>
/// Configuration for GitHub external login, bound from the "Authentication:GitHub" section.
/// </summary>
public sealed class GitHubAuthOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Authentication:GitHub";

    /// <summary>OAuth client id. Supply via user-secrets/environment, never source control.</summary>
    public string? ClientId { get; set; }

    /// <summary>OAuth client secret. Supply via user-secrets/environment, never source control.</summary>
    public string? ClientSecret { get; set; }
}
