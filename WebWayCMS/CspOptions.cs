namespace WebWayCMS;

/// <summary>
/// Content-Security-Policy configuration, bound from the "Csp" configuration section. The CMS ships
/// secure defaults that keep its own admin UI working (CKEditor / Bulma / FontAwesome CDNs); a host
/// overrides or adds individual directives via <see cref="Directives"/>. Directives the host does not
/// mention keep the CMS default. Set a directive to an empty value to drop it entirely.
/// </summary>
public sealed class CspOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Csp";

    /// <summary>Whether the CSP header is emitted. Defaults to true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When true, the policy is emitted as <c>Content-Security-Policy-Report-Only</c> (monitored but
    /// not enforced) instead of <c>Content-Security-Policy</c>. Defaults to false.
    /// </summary>
    public bool ReportOnly { get; set; }

    /// <summary>
    /// Per-directive overrides merged over the CMS defaults, e.g. <c>{ "script-src": "'self'
    /// https://my-cdn.example" }</c>. Keys are directive names; values are the space-separated source
    /// lists.
    /// </summary>
    public Dictionary<string, string> Directives { get; set; } = new();
}
