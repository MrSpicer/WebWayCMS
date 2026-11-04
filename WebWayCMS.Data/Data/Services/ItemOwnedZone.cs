using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>A zone owned by a container item (one of its slots), with the owning item's current draft.</summary>
public sealed record ItemOwnedZone(Guid ZoneNodeId, string SlotName, ContentZoneItemDTO Owner);
