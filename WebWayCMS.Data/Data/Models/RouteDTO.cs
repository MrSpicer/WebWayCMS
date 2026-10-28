namespace WebWayCMS.Data.Models;

/// <summary>
/// A versioned route item owned by routable content (for a page, the page's <c>routes</c> owned
/// relation). Published route versions are projected into <see cref="CMSRouteDTO"/> for the transformer;
/// the route item itself is the editable, versioned source.
/// </summary>
public record RouteDTO : IVersionedContent
{
    public Guid VersionId { get; set; }

    public ContentVersion Version { get; set; } = new();

    /// <summary>Derived (from the owner's slug/parent) or Explicit (a fixed pattern).</summary>
    public RouteMode Mode { get; set; }

    /// <summary>
    /// The explicit pattern. For a Derived route it holds the last projected pattern (informational).
    /// </summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>Navigation link text; routes without one are not shown in navigation widgets.</summary>
    public string? NavigationName { get; set; }

    public int Order { get; set; }
}
