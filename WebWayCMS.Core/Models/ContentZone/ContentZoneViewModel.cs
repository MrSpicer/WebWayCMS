namespace WebWayCMS.Models.ContentZone;

public class ContentZoneViewModel
{
    /// <summary>
    /// The content node ID of the content zone (Guid.Empty if the zone doesn't exist yet).
    /// </summary>
    public Guid Id { get; set; } = Guid.Empty;

    /// <summary>
    /// The unique path/name that identifies this content zone (includes ordinal suffix).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The raw zone name passed to the ViewComponent (without ordinal suffix).
    /// </summary>
    public string RawZoneName { get; set; } = string.Empty;

    /// <summary>
    /// The zone objects (items) to render in this zone.
    /// </summary>
    public List<ContentZoneObject> ZoneObjects { get; set; } = new();

    /// <summary>
    /// The page node ID this zone belongs to (if page-scoped).
    /// </summary>
    public Guid? ParentPageNodeId { get; set; }

    /// <summary>
    /// Indicates whether the current user can edit this zone.
    /// </summary>
    public bool CanEdit { get; set; } = false;
}
