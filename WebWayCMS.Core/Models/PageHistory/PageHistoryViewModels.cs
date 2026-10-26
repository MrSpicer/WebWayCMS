namespace WebWayCMS.Models.PageHistory;

/// <summary>How a zone item differs between two states of a page set.</summary>
public enum PageSetChange
{
    Unchanged = 0,
    Added = 1,
    Removed = 2,

    /// <summary>The widget, its configuration or its metadata changed.</summary>
    Changed = 3,

    /// <summary>Only its zone or position changed.</summary>
    Moved = 4
}

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

/// <summary>One zone item row in an entry's detail or a comparison.</summary>
public sealed class PageHistoryItemRow
{
    public Guid ItemNodeId { get; init; }

    /// <summary>The zone the item sits in on the side the row describes.</summary>
    public Guid ZoneNodeId { get; init; }

    /// <summary>The zone's name (its slot name), or a placeholder when the zone no longer exists.</summary>
    public string ZoneName { get; init; } = string.Empty;

    public string ComponentName { get; init; } = string.Empty;

    /// <summary>The item's admin label, falling back to the component name.</summary>
    public string Label { get; init; } = string.Empty;

    public int Ordinal { get; init; }
    public int VersionNumber { get; init; }
    public PageSetChange Change { get; init; }
}

/// <summary>One page of a page's history entries, newest first.</summary>
public sealed class PageHistoryIndexViewModel
{
    public Guid PageNodeId { get; init; }
    public string PageTitle { get; init; } = string.Empty;
    public List<PageHistoryEntrySummary> Entries { get; init; } = new();

    /// <summary>How many newer entries precede this page.</summary>
    public int Skip { get; init; }

    /// <summary>The page size this page was read with.</summary>
    public int Take { get; init; }

    /// <summary>The page's entry count across all pages.</summary>
    public int TotalCount { get; init; }

    /// <summary>Whether newer entries exist before this page.</summary>
    public bool HasNewer => Skip > 0;

    /// <summary>Whether older entries exist after this page.</summary>
    public bool HasOlder => Skip + Entries.Count < TotalCount;
}

/// <summary>An entry and what it changed relative to the entry before it.</summary>
public sealed class PageHistoryEntryViewModel
{
    public Guid PageNodeId { get; init; }
    public string PageTitle { get; init; } = string.Empty;
    public PageHistoryEntrySummary Entry { get; init; } = new();

    /// <summary>The page title this entry captured.</summary>
    public string CapturedPageTitle { get; init; } = string.Empty;

    /// <summary>The page's own fields differ from the previous entry's.</summary>
    public bool PageChanged { get; init; }

    public Guid? PreviousEntryId { get; init; }

    /// <summary>The entry's widgets plus those the previous entry had and this one dropped.</summary>
    public List<PageHistoryItemRow> Items { get; init; } = new();
}

/// <summary>
/// The current drafts compared with an entry: each row says how the page changed <em>since</em> the
/// entry, i.e. what a revert would undo.
/// </summary>
public sealed class PageHistoryCompareViewModel
{
    public Guid PageNodeId { get; init; }
    public string PageTitle { get; init; } = string.Empty;
    public PageHistoryEntrySummary Entry { get; init; } = new();
    public string CapturedPageTitle { get; init; } = string.Empty;

    /// <summary>The page's own fields (title, slug, page type, configuration) differ from the entry's.</summary>
    public bool PageChanged { get; init; }

    public List<PageHistoryItemRow> Items { get; init; } = new();

    /// <summary>True when a revert would change something.</summary>
    public bool HasChanges => PageChanged || Items.Any(i => i.Change != PageSetChange.Unchanged);

    /// <summary>The current drafts' token; a revert is refused when it no longer matches.</summary>
    public string StateToken { get; init; } = string.Empty;
}
