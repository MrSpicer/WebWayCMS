namespace WebWayCMS.Data.Services;

/// <summary>
/// The nested-zone moves that go with a container's view change (its old view's slots mapped onto the
/// new view's), applied atomically with the container's save and recorded under
/// <paramref name="HistoryNote"/>.
/// </summary>
public sealed record SlotRemapPlan(IReadOnlyList<SlotMove> Moves, string HistoryNote);
