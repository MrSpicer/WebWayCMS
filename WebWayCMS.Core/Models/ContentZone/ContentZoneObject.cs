using WebWayCMS.ContentZones;

namespace WebWayCMS.Models.ContentZone;

//probably move this to Data and use as dto
public class ContentZoneObject : IContentZoneObject
{
    /// <summary>
    /// The content node ID of this content zone item.
    /// </summary>
    public Guid Id { get; set; } = Guid.Empty;

    public int Ordinal { get; set; } = 0;
    public Guid ZoneId { get; set; }

    /// <summary>
    /// The version number of the row this object was mapped from, rendered as a data attribute so
    /// the zone editor's saves can carry it as the expected version (concurrency check).
    /// </summary>
    public int VersionNumber { get; set; }

    public string ComponentName { get; set; } = string.Empty;
    public object ComponentProperties { get; set; } = default!;

    /// <summary>
    /// The item's widget metadata (display mode + admin presentation fields), deserialized
    /// from the zone item's <c>MetadataJson</c> column at map time.
    /// </summary>
    public WidgetMetadata Metadata { get; set; } = new();
}
