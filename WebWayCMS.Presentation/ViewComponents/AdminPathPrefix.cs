namespace WebWayCMS.ViewComponents;

/// <summary>
/// Shared "is this path inside the admin area?" test for the navigation view components.
/// A plain <c>StartsWith</c> would also match sibling paths that merely share the prefix
/// (<c>/wadmin-guide</c>, <c>/wadministration</c>), pulling a public route into the admin navbar,
/// so the match must land on a segment boundary.
/// </summary>
internal static class AdminPathPrefix
{
    public const string Value = "/wadmin";

    public static bool Matches(string path)
        => path.StartsWith(Value, StringComparison.OrdinalIgnoreCase)
            && (path.Length == Value.Length || path[Value.Length] == '/');
}
