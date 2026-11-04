namespace WebWayCMS.Models.PageHistory;

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
