namespace WebWayCMS.Controllers.Api;

/// <summary>
/// Request model for saving a content zone item.
/// </summary>
public class SaveItemRequest
{
    /// <summary>
    /// The zone slot name (used as fallback when ZoneId is not provided).
    /// </summary>
    public string ZoneName { get; set; } = string.Empty;

    /// <summary>
    /// The human-readable slot name (e.g. "Main"). Used with ParentPageNodeId for assignment lookup.
    /// </summary>
    public string? SlotName { get; set; }

    /// <summary>
    /// The page node ID to scope zone creation via assignment lookup.
    /// </summary>
    public Guid? ParentPageNodeId { get; set; }

    /// <summary>
    /// The zone node ID if known (optional, zone will be looked up by assignment or name if not provided).
    /// This is set automatically and never displayed to users.
    /// </summary>
    public Guid? ZoneId { get; set; }

    /// <summary>
    /// The item node ID if updating an existing item.
    /// This is set automatically and never displayed to users.
    /// </summary>
    public Guid? ItemId { get; set; }

    /// <summary>
    /// The name of the ViewComponent to render.
    /// </summary>
    public string ComponentName { get; set; } = string.Empty;

    /// <summary>
    /// JSON-serialized configuration properties for the component.
    /// </summary>
    public string? ComponentPropertiesJson { get; set; }

    /// <summary>
    /// JSON-serialized <c>WidgetMetadata</c> for a new item (e.g. the Add Widget modal's display
    /// mode). Ignored on update.
    /// </summary>
    public string? MetadataJson { get; set; }

    /// <summary>
    /// The item's version number as the client last saw it (updates only). A mismatch fails the
    /// save with the "changed by someone else" message instead of silently overwriting the edit.
    /// </summary>
    public int? ExpectedVersionNumber { get; set; }
}
