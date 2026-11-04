using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

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
