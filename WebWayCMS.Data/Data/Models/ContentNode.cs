namespace WebWayCMS.Data.Models;

/// <summary>
/// One row per logical content item — the stable identity that never changes across versions.
/// Every cross-entity foreign key points here, never at a version row.
/// </summary>
public record ContentNode
{
    /// <summary>Stable identity (replaces the old MasterId).</summary>
    public Guid Id { get; set; }

    /// <summary>The content type this node belongs to, e.g. "pages", "articles", "contentzones".</summary>
    public string ContentTypeKey { get; set; } = string.Empty;

    /// <summary>Identity parentage. Moving a page is not an edit, so this lives on the node.</summary>
    public Guid? ParentNodeId { get; set; }

    /// <summary>Multi-site seam; null = default site.</summary>
    public Guid? SiteId { get; set; }

    public DateTime CreatedUtc { get; set; }

    public Guid? CreatedBy { get; set; }

    public bool IsDeleted { get; set; }

    public bool IsArchived { get; set; }

    public bool IsHidden { get; set; }
}
