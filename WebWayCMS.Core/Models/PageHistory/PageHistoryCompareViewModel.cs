namespace WebWayCMS.Models.PageHistory;

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
