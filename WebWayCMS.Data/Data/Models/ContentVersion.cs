namespace WebWayCMS.Data.Models;

public enum ContentVersionState
{
    Draft = 0,
    InReview = 1,
    Approved = 2,
    Published = 3,
    Archived = 4
}

/// <summary>
/// One row per version, per variant. Identity and version are split: the shared identity lives on
/// <see cref="ContentNode"/> while the mutable, versioned data lives here.
/// </summary>
public record ContentVersion
{
    public Guid Id { get; set; }

    public Guid NodeId { get; set; }

    public ContentNode Node { get; set; } = null!;

    public int VersionNumber { get; set; }

    /// <summary>Variant axis. Non-nullable with "" sentinel — see the PostgreSQL NULLS DISTINCT note.</summary>
    public string Culture { get; set; } = string.Empty;

    /// <summary>Variant axis. "" = default segment.</summary>
    public string Segment { get; set; } = string.Empty;

    public ContentVersionState State { get; set; }

    /// <summary>Exactly one per (NodeId, Culture, Segment).</summary>
    public bool IsCurrentDraft { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public Guid? CreatedBy { get; set; }

    public DateTime CreatedUtc { get; set; }

    public Guid? PublishedBy { get; set; }

    public DateTime? PublishedUtc { get; set; }

    /// <summary>Scheduling seam — not filtered by the read context yet.</summary>
    public DateTime? PublishStartUtc { get; set; }

    public DateTime? PublishEndUtc { get; set; }

    public string? ChangeNote { get; set; }

    public Guid ChangeSetId { get; set; }

    public List<CustomField> CustomFields { get; set; } = new();
}
