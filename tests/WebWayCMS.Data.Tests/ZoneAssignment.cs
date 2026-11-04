using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

/// <summary>
/// A zone's owner and slot in the shape of the retired <c>ContentZoneAssignments</c> row, so the zone tests
/// keep their arrange blocks: seeding one writes the zone version's <see cref="ZoneRelations.Owner"/> relation.
/// </summary>
internal sealed record ZoneAssignment
{
    public Guid Id { get; init; }
    public string SlotName { get; init; } = string.Empty;
    public Guid ContentZoneNodeId { get; init; }
    public Guid? ParentPageNodeId { get; init; }
    public Guid? ParentItemNodeId { get; init; }
}
