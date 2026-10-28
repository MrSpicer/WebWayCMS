using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Core.Tests;

/// <summary>Zone placement for test items (the item's <see cref="ZoneRelations.Zone"/> relation).</summary>
internal static class ZoneFixture
{
    /// <summary>Places <paramref name="item"/> in <paramref name="zoneNodeId"/> at <paramref name="ordinal"/>; returns it.</summary>
    public static ContentZoneItemDTO In(this ContentZoneItemDTO item, Guid zoneNodeId, int ordinal = 0)
    {
        ZoneRelations.Place(item.Version, zoneNodeId, ordinal);
        return item;
    }
}
