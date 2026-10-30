using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// Outcome of publishing a page's pending zone-item drafts: the items that went live (so the caller
/// can register their widget routes) and an error per item that could not be published.
/// </summary>
public sealed record PageZonePublishResult(
    IReadOnlyList<ContentZoneItemDTO> Published,
    IReadOnlyList<string> Errors)
{
    /// <summary>The node ids of the pending items that could not be published (one per entry in <see cref="Errors"/>).</summary>
    public IReadOnlyList<Guid> FailedItemNodeIds { get; init; } = [];
}

/// <summary>
/// The published zone items of a page's zone tree, split by what a widget-route pass does with them:
/// <see cref="Live"/> are the active items the page shows (their routes are re-derived from the
/// page's route), <see cref="Removed"/> are the published items the page does not show — removed
/// (inactive) ones and active ones nested under a removed container — whose stale route rows are swept,
/// so a removed widget's URLs die with the page publish that removes it.
/// </summary>
public sealed record PageTreeLiveItems(
    IReadOnlyList<ContentZoneItemDTO> Live,
    IReadOnlyList<ContentZoneItemDTO> Removed);
