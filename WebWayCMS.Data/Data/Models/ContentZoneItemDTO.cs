using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Models;

/// <summary>
/// Database entity representing an item within a content zone.
/// Each item references a view component to render with specific properties.
/// </summary>
public record ContentZoneItemDTO : IVersionedContent
{
    public Guid VersionId { get; set; }
    public ContentVersion Version { get; set; } = new();

    /// <summary>
    /// The zone this version sits in: its <see cref="ZoneRelations.Zone"/> relation. In-memory only (ignored
    /// by EF): a query uses <see cref="ZoneRelations.InZone"/> / <see cref="ZoneRelations.ZoneOf"/>, and a
    /// write uses <see cref="ZoneRelations.Place"/>.
    /// </summary>
    public Guid ContentZoneNodeId => Version.TargetOf(ZoneRelations.Zone) ?? Guid.Empty;

    /// <summary>
    /// Display order within the zone (lower numbers render first): the <see cref="ZoneRelations.Zone"/>
    /// relation's ordinal. In-memory only, like <see cref="ContentZoneNodeId"/> (query with
    /// <see cref="ZoneRelations.ZoneOrdinal"/>).
    /// </summary>
    public int Ordinal => Version.OrdinalOf(ZoneRelations.Zone);

    /// <summary>Name of the ViewComponent to render (e.g., "ContentBlock", "Article").</summary>
    public string ComponentName { get; set; } = string.Empty;

    /// <summary>JSON-serialized properties to pass to the ViewComponent.</summary>
    public string ComponentPropertiesJson { get; set; } = "{}";

    /// <summary>
    /// JSON-serialized per-item widget metadata (<c>WidgetMetadata</c>: the Single/List display
    /// mode plus admin label/note/CSS class/anchor id), edited through the zone editor's settings
    /// (cog) dialog. A null value on an update means "keep the existing metadata" so a
    /// content-config save never wipes it; new items normalize to <c>"{}"</c> on write.
    /// </summary>
    public string? MetadataJson { get; set; }

    /// <summary>Whether this item is active and should be rendered.</summary>
    public bool IsActive { get; set; } = true;
}
