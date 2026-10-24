using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>Moves the content of a container's <paramref name="FromSlot"/> zone into its <paramref name="ToSlot"/> zone.</summary>
public sealed record SlotMove(string FromSlot, string ToSlot);

/// <summary>
/// The nested-zone moves that go with a container's view change (its old view's slots mapped onto the
/// new view's), applied atomically with the container's save and recorded under
/// <paramref name="HistoryNote"/>.
/// </summary>
public sealed record SlotRemapPlan(IReadOnlyList<SlotMove> Moves, string HistoryNote);

/// <summary>A zone owned by a container item (one of its slots), with the owning item's current draft.</summary>
public sealed record ItemOwnedZone(Guid ZoneNodeId, string SlotName, ContentZoneItemDTO Owner);
