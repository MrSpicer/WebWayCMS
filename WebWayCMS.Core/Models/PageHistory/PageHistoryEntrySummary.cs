namespace WebWayCMS.Models.PageHistory;

/// <summary>One page-history entry in a listing.</summary>
public sealed class PageHistoryEntrySummary
{
    public Guid Id { get; init; }
    public DateTime CreatedUtc { get; init; }
    public Guid? CreatedBy { get; init; }
    public int ItemCount { get; init; }

    /// <summary>Draft (a change while editing) or Published (what went live).</summary>
    public Data.Models.PageSetSnapshotKind Kind { get; init; }

    /// <summary>What the change was, e.g. "Added ContentBlock to Main".</summary>
    public string? Note { get; init; }

    /// <summary>Widgets in this entry that were not in the previous one.</summary>
    public int Added { get; init; }

    /// <summary>Widgets in both entries at a different version in the same place (edited).</summary>
    public int Updated { get; init; }

    /// <summary>Widgets in both entries in a different zone or position.</summary>
    public int Moved { get; init; }

    /// <summary>Widgets in the previous entry that are not in this one.</summary>
    public int Removed { get; init; }

    /// <summary>The page's own fields went live at a different version than in the previous entry.</summary>
    public bool PageUpdated { get; init; }

    /// <summary>True for the newest entry (what is live now, unless the page was unpublished since).</summary>
    public bool IsLatest { get; init; }
}
