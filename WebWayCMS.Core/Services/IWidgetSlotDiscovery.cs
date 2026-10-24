namespace WebWayCMS.Services;

/// <summary>
/// Finds the nested-zone slots a container widget renders with a given configuration, by rendering it
/// in a discovery mode that records each top-level <c>ContentZone</c> it would show instead of showing it.
/// Nothing is declared anywhere, so a host-added view's slots are found too.
/// </summary>
public interface IWidgetSlotDiscovery
{
    /// <summary>
    /// The slot names, in render order and de-duplicated; null when the render failed (the slots are unknown).
    /// </summary>
    Task<IReadOnlyList<string>?> DiscoverSlotsAsync(Guid itemNodeId, string componentName, object componentProperties, CancellationToken ct = default);
}
