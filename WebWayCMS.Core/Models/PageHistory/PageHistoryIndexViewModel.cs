namespace WebWayCMS.Models.PageHistory;

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
