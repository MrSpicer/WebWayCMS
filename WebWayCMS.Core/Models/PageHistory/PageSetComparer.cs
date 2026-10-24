using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Models.PageHistory;

/// <summary>One item's comparison: the version on the side the row describes and how it changed.</summary>
public sealed record PageSetItemDiff(ContentZoneItemDTO Item, PageSetChange Change);

/// <summary>The difference between two states of a page set.</summary>
public sealed record PageSetDiff(bool PageChanged, List<PageSetItemDiff> Items);

/// <summary>
/// Compares two states of a page set by content (<see cref="PageSetEquality"/>), not by version id, so a
/// restored copy identical to the original reads as unchanged. Only active items count as present.
/// </summary>
public static class PageSetComparer
{
    /// <summary>
    /// How <paramref name="to"/> differs from <paramref name="from"/> (null: everything in
    /// <paramref name="to"/> is new). A row describes the <paramref name="to"/> version, or the
    /// <paramref name="from"/> version for a removed item.
    /// </summary>
    public static PageSetDiff Compare(PageSetState? from, PageSetState to)
    {
        var before = Active(from?.Items);
        var after = Active(to.Items);

        var rows = new List<PageSetItemDiff>();
        foreach (var (nodeId, item) in after)
        {
            var change = !before.TryGetValue(nodeId, out var previous) ? PageSetChange.Added
                : !PageSetEquality.ItemContentEqual(previous, item) ? PageSetChange.Changed
                : previous.ContentZoneNodeId != item.ContentZoneNodeId || previous.Ordinal != item.Ordinal ? PageSetChange.Moved
                : PageSetChange.Unchanged;
            rows.Add(new PageSetItemDiff(item, change));
        }

        rows.AddRange(before
            .Where(kv => !after.ContainsKey(kv.Key))
            .Select(kv => new PageSetItemDiff(kv.Value, PageSetChange.Removed)));

        var pageChanged = from != null && !PageSetEquality.PagesEqual(from.Page, to.Page);
        return new PageSetDiff(pageChanged, rows);
    }

    private static Dictionary<Guid, ContentZoneItemDTO> Active(IEnumerable<ContentZoneItemDTO>? items)
        => (items ?? []).Where(i => i.IsActive).ToDictionary(i => i.Version.NodeId);
}
