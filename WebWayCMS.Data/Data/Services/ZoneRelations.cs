using System.Linq.Expressions;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// The zone structure as relations: an item sits in a zone through an ordered <see cref="Zone"/>
/// containment owned by the item's version (the row's <c>Ordinal</c> is its position), and a page's or a
/// container item's zone is owned through an <see cref="Owner"/> containment on the zone's version (the
/// row's <c>Qualifier</c> is the slot). The expressions here are the EF-translatable forms; the item's
/// <c>ContentZoneNodeId</c>/<c>Ordinal</c> accessors are in-memory only and must not appear in a query.
/// </summary>
public static class ZoneRelations
{
    /// <summary>
    /// Item → zone (ordered containment). Named after the zone item upsert view model's
    /// <c>ContentZoneNodeId</c> property, which declares it.
    /// </summary>
    public const string Zone = "ContentZoneNodeId";

    /// <summary>Zone → page or container item (containment, slot in the qualifier).</summary>
    public const string Owner = "Owner";

    /// <summary>The content type key of a zone item node: an owner of this type is a container item, any other a page.</summary>
    public const string ItemContentType = "contentzoneitems";

    /// <summary>Writes the item version's placement: <paramref name="zoneNodeId"/> at <paramref name="ordinal"/>.</summary>
    public static void Place(ContentVersion version, Guid zoneNodeId, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(version);
        version.SetRelation(Zone, zoneNodeId, RelationKind.Containment, ordinal: ordinal);
    }

    /// <summary>A copy of <paramref name="item"/> (and its version) placed in <paramref name="zoneNodeId"/> at <paramref name="ordinal"/>.</summary>
    public static ContentZoneItemDTO Placed(ContentZoneItemDTO item, Guid zoneNodeId, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(item);
        var copy = item with { Version = item.Version with { } };
        Place(copy.Version, zoneNodeId, ordinal);
        return copy;
    }

    /// <summary>Writes the zone version's owner: <paramref name="ownerNodeId"/>'s slot <paramref name="slot"/>.</summary>
    public static void Own(ContentVersion version, Guid ownerNodeId, string slot)
    {
        ArgumentNullException.ThrowIfNull(version);
        version.SetRelation(Owner, ownerNodeId, RelationKind.Containment, slot);
    }

    /// <summary>The item's version sits in <paramref name="zoneNodeId"/>.</summary>
    public static Expression<Func<ContentZoneItemDTO, bool>> InZone(Guid zoneNodeId)
        => i => i.Version.Relations.Any(r => r.Name == Zone && r.TargetNodeId == zoneNodeId);

    /// <summary>The item's version sits in one of <paramref name="zoneNodeIds"/>.</summary>
    public static Expression<Func<ContentZoneItemDTO, bool>> InZones(IEnumerable<Guid> zoneNodeIds)
    {
        var ids = zoneNodeIds.ToList();
        return i => i.Version.Relations.Any(r => r.Name == Zone && ids.Contains(r.TargetNodeId));
    }

    /// <summary>The item's position in its zone, for ordering.</summary>
    public static readonly Expression<Func<ContentZoneItemDTO, int>> ZoneOrdinal
        = i => i.Version.Relations.Where(r => r.Name == Zone).Select(r => r.Ordinal).FirstOrDefault();

    /// <summary>The item's zone, for projection.</summary>
    public static readonly Expression<Func<ContentZoneItemDTO, Guid>> ZoneOf
        = i => i.Version.Relations.Where(r => r.Name == Zone).Select(r => r.TargetNodeId).FirstOrDefault();
}

/// <summary>
/// A zone's owner, read from the current draft of its <see cref="ZoneRelations.Owner"/> relation:
/// exactly one of <see cref="ParentPageNodeId"/> (a page's slot) or <see cref="ParentItemNodeId"/> (a
/// container item's slot) is set. Ownership never changes, so every version of the zone agrees.
/// </summary>
public sealed record OwnedZone
{
    public Guid ZoneNodeId { get; init; }
    public string SlotName { get; init; } = string.Empty;
    public Guid? ParentPageNodeId { get; init; }
    public Guid? ParentItemNodeId { get; init; }
}
