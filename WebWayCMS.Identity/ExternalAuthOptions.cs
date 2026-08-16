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

/// <summary>
/// Configuration for Microsoft Account external login, bound from the "Authentication:Microsoft" section.
/// </summary>
public sealed class MicrosoftAuthOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Authentication:Microsoft";

    /// <summary>OAuth client id. Supply via user-secrets/environment, never source control.</summary>
    public string? ClientId { get; set; }

    /// <summary>OAuth client secret. Supply via user-secrets/environment, never source control.</summary>
    public string? ClientSecret { get; set; }
}

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
