using System.Text.RegularExpressions;

namespace WebWayCMS.Data.Services;

/// <summary>
/// Route pattern normalization and validation shared by the route content model (save-time) and
/// <see cref="CMSRouteService"/> (projection-time), so both reject the same patterns.
/// </summary>
public static class RoutePatternRules
{
    // ASP.NET Core's reserved routing names. A route value with one of these names drives endpoint
    // selection itself (a non-empty "page" makes MVC look for a Razor Page), so the dynamic CMS route
    // would 404 instead of reaching the page controller.
    private static readonly HashSet<string> ReservedParameterNames =
        new(StringComparer.OrdinalIgnoreCase) { "action", "area", "controller", "handler", "page" };

    // The name of each {parameter} in a pattern: after any catch-all '*'s, up to a constraint,
    // default or optional marker.
    private static readonly Regex ParameterNamePattern = new(@"\{\**([^}:=?]+)", RegexOptions.Compiled);

    /// <summary>The first route parameter in <paramref name="pattern"/> with a reserved name, or null.</summary>
    public static string? FindReservedParameter(string pattern)
        => ParameterNamePattern.Matches(pattern)
            .Select(m => m.Groups[1].Value.Trim())
            .FirstOrDefault(ReservedParameterNames.Contains);

    /// <summary>Normalizes a pattern to lower case with a leading slash and no trailing slash.</summary>
    public static string Normalize(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return "/";

        pattern = pattern.Trim().ToLowerInvariant();

        if (!pattern.StartsWith('/'))
            pattern = "/" + pattern;

        if (pattern.Length > 1 && pattern.EndsWith('/'))
            pattern = pattern.TrimEnd('/');

        return pattern;
    }

    /// <summary>A user-facing error for a reserved parameter name, or null when the pattern is valid.</summary>
    public static string? Validate(string pattern)
    {
        pattern = Normalize(pattern);
        if (FindReservedParameter(pattern) is not { } reserved)
            return null;

        return $"Route pattern '{pattern}' uses the parameter name '{reserved}', which ASP.NET Core reserves for routing " +
               "(action, area, controller, handler, page); the request would 404. Rename the parameter, e.g. '{" + reserved + "Number}'.";
    }
}
