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

    /// <summary>The relations this version owns (auto-included with the version).</summary>
    public List<ContentRelation> Relations { get; set; } = new();

    /// <summary>
    /// Not mapped. The relations a caller wrote for the next save, by name: the store starts the new
    /// version from a baseline (the current draft's relations on an edit, none on a create) and replaces
    /// only the names written here, so a write path that never mentions a relation can't drop it. Written
    /// through <see cref="ContentVersionRelations"/>, which replaces the dictionary rather than mutating
    /// it, so a <c>with</c> copy never shares writes with its original.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<RelationWrite>> RelationWrites { get; set; }
        = new Dictionary<string, IReadOnlyList<RelationWrite>>();
}
