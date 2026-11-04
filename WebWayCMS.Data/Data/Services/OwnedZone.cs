namespace WebWayCMS.Data.Services;

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
