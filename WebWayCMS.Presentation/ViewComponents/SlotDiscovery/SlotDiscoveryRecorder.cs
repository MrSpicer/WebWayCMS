namespace WebWayCMS.ViewComponents.SlotDiscovery;

/// <summary>
/// Placed in a discovery render's ViewData: tells <see cref="ContentZoneViewComponent"/> to record the slot
/// names the container widget renders instead of rendering them, and <see cref="WidgetViewComponent"/> to
/// render the public variant (so the slots follow the configured view, host overrides included).
/// </summary>
public sealed class SlotDiscoveryRecorder
{
    /// <summary>The ViewData key the recorder travels under (ViewData is copied into nested renders).</summary>
    public const string ViewDataKey = "ContentZone:SlotDiscovery";

    private readonly List<string> _slots = [];
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    public SlotDiscoveryRecorder(Guid itemNodeId)
    {
        ItemNodeId = itemNodeId;
    }

    /// <summary>The container item being discovered; only zones it owns are recorded.</summary>
    public Guid ItemNodeId { get; }

    /// <summary>The recorded slot names, in render order, without duplicates.</summary>
    public IReadOnlyList<string> Slots => _slots;

    public void Record(string slotName)
    {
        if (_seen.Add(slotName))
            _slots.Add(slotName);
    }
}
