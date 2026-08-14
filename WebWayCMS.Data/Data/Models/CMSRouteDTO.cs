namespace WebWayCMS.Data.Models;

/// <summary>
/// A CMS route row. Routes are not versioned — they are written by Publish and hard-deleted/replaced.
/// </summary>
public record CMSRouteDTO
{
    public Guid Id { get; set; }

    public string Pattern { get; set; } = string.Empty;

    public string DefaultsJson { get; set; } = "{}";

    public string ConstraintsJson { get; set; } = "{}";

    public string DataTokensJson { get; set; } = "{}";

    public int Order { get; set; }

    public Guid? OwningContentNodeId { get; set; }

    public string? OwningContentType { get; set; }

    public bool IsReserved { get; set; }
}
