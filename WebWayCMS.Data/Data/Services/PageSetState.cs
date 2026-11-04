using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>A page and its zone-tree items, as of a snapshot or as current drafts.</summary>
public sealed record PageSetState(PageDTO Page, List<ContentZoneItemDTO> Items);
