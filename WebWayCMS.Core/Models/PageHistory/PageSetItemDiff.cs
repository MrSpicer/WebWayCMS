using WebWayCMS.Data.Models;

namespace WebWayCMS.Models.PageHistory;

/// <summary>One item's comparison: the version on the side the row describes and how it changed.</summary>
public sealed record PageSetItemDiff(ContentZoneItemDTO Item, PageSetChange Change);
