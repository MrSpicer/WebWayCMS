namespace WebWayCMS.Models.PageHistory;

/// <summary>The difference between two states of a page set.</summary>
public sealed record PageSetDiff(bool PageChanged, List<PageSetItemDiff> Items);
