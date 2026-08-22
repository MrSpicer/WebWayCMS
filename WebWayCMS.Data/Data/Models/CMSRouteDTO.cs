namespace WebWayCMS.Data.Models;

/// <summary>
/// A CMS route row. Routes are not versioned — they are written by Publish and hard-deleted/replaced.
/// </summary>
public record CMSRouteDTO
{
    public Guid Id { get; set; }

    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// Maximum stored length of <see cref="NavigationName"/>. Mirrored by the EF configuration —
    /// callers that derive a navigation name from longer text (a page title) must truncate to it.
    /// </summary>
    public const int NavigationNameMaxLength = 256;

    /// <summary>
    /// Human-readable link text for this route in navigation widgets. Routes without one are not shown.
    /// </summary>
    public string? NavigationName { get; set; }

    public string DefaultsJson { get; set; } = "{}";

    public string ConstraintsJson { get; set; } = "{}";

    public string DataTokensJson { get; set; } = "{}";

    public int Order { get; set; }

    public Guid? OwningContentNodeId { get; set; }

    public string? OwningContentType { get; set; }

    public bool IsReserved { get; set; }
}
