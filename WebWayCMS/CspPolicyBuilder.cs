namespace WebWayCMS;

/// <summary>
/// Builds the Content-Security-Policy header value from <see cref="CspOptions"/>, merging host-supplied
/// directives over the CMS defaults. Pure logic so it can be unit-tested; the header write itself lives
/// in the middleware pipeline.
/// </summary>
public static class CspPolicyBuilder
{
    /// <summary>
    /// CMS default directives, in emission order. Chosen so the admin UI works out of the box:
    /// scripts are restricted to self + the CKEditor CDN (no <c>'unsafe-inline'</c>); styles allow
    /// <c>'unsafe-inline'</c> because CKEditor injects styles at runtime and views use inline style
    /// attributes; the CDNs used by the admin layout are permitted for styles/fonts/images.
    /// </summary>
    private static readonly (string Directive, string Value)[] Defaults =
    {
        ("default-src", "'self'"),
        ("script-src", "'self' https://cdn.ckeditor.com"),
        ("style-src", "'self' 'unsafe-inline' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://cdn.ckeditor.com"),
        ("img-src", "'self' data: https://cdn.ckeditor.com"),
        ("font-src", "'self' https://cdnjs.cloudflare.com https://cdn.jsdelivr.net"),
        ("connect-src", "'self'"),
        ("object-src", "'none'"),
        ("base-uri", "'self'"),
        ("frame-ancestors", "'none'"),
    };

    /// <summary>The header name to emit for the given options.</summary>
    public static string HeaderName(CspOptions options) =>
        options.ReportOnly ? "Content-Security-Policy-Report-Only" : "Content-Security-Policy";

    /// <summary>
    /// Builds the policy string. Returns an empty string when disabled or when the merged policy has no
    /// directives (so the caller can skip emitting the header).
    /// </summary>
    public static string Build(CspOptions options)
    {
        if (!options.Enabled)
            return string.Empty;

        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var (directive, value) in Defaults)
        {
            merged[directive] = value;
            order.Add(directive);
        }

        foreach (var (directive, value) in options.Directives)
        {
            if (!merged.ContainsKey(directive))
                order.Add(directive);
            merged[directive] = value;
        }

        return string.Join("; ", order
            .Where(d => !string.IsNullOrWhiteSpace(merged[d]))
            .Select(d => $"{d} {merged[d].Trim()}"));
    }
}
