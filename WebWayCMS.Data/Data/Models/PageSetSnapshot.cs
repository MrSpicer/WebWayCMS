namespace WebWayCMS.Data.Models;

/// <summary>Whether a page-history entry records the page's draft or what went live.</summary>
public enum PageSetSnapshotKind
{
    /// <summary>The current drafts after a change (a save, add, remove, move, revert, discard…).</summary>
    Draft = 0,

    /// <summary>What was live right after a publish.</summary>
    Published = 1
}

/// <summary>
/// One entry in a page's page-set history: the exact versions of a page and every active zone item in
/// its zone tree (nested zones included), recorded by <c>IPageHistoryRecorder</c> after every change to
/// the page set (<see cref="PageSetSnapshotKind.Draft"/>) and after every publish
/// (<see cref="PageSetSnapshotKind.Published"/>). Versions are never rewritten, so an entry stays
/// faithful; a version an entry references cannot be deleted on its own.
/// </summary>
public record PageSetSnapshot
{
    public Guid Id { get; set; }

    /// <summary>The page's <see cref="ContentNode.Id"/>.</summary>
    public Guid PageNodeId { get; set; }

    /// <summary>The page version the entry captured.</summary>
    public Guid PageVersionId { get; set; }

    public PageSetSnapshotKind Kind { get; set; }

    /// <summary>What the change was, e.g. "Added ContentBlock to Main" or "Published".</summary>
    public string? Note { get; set; }

    /// <summary>
    /// <c>PageSetEquality.ContentHash</c> of the captured state (content only, no version ids), used to
    /// skip an entry identical to the previous one.
    /// </summary>
    public string StateHash { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }

    public Guid? CreatedBy { get; set; }

    /// <summary>The change set that produced this entry.</summary>
    public Guid ChangeSetId { get; set; }

    public int ItemCount { get; set; }

    public List<PageSetSnapshotItem> Items { get; set; } = new();
}

/// <summary>One zone item version captured by a <see cref="PageSetSnapshot"/>.</summary>
public record PageSetSnapshotItem
{
    public Guid SnapshotId { get; set; }

    public Guid ItemNodeId { get; set; }

    public Guid ItemVersionId { get; set; }

    /// <summary>The zone the version sat in (denormalized from the item version for listings).</summary>
    public Guid ZoneNodeId { get; set; }

    /// <summary>The version's ordinal (denormalized from the item version for listings).</summary>
    public int Ordinal { get; set; }
}
