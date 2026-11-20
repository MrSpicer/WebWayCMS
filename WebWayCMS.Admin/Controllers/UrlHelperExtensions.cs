using Microsoft.AspNetCore.Mvc;

namespace WebWayCMS.Controllers.Admin;

/// <summary>The one local-URL check for admin return URLs (never an open redirect).</summary>
public static class UrlHelperExtensions
{
    /// <summary><paramref name="candidate"/> when it is a non-empty URL local to this site, else <paramref name="fallback"/>.</summary>
    public static string? LocalOrDefault(this IUrlHelper url, string? candidate, string? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        return !string.IsNullOrWhiteSpace(candidate) && url.IsLocalUrl(candidate) ? candidate : fallback;
    }
}
