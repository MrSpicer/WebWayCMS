namespace WebWayCMS.Data.Models;

/// <summary>
/// Database entity representing an item within a content zone.
/// Each item references a view component to render with specific properties.
/// </summary>
public record ContentZoneItemDTO : IVersionedContent
{
    public Guid VersionId { get; set; }
    public ContentVersion Version { get; set; } = new();

    /// <summary>Foreign key to the parent zone's <see cref="ContentNode.Id"/>.</summary>
    public Guid ContentZoneNodeId { get; set; }

    /// <summary>Display order within the content zone (lower numbers render first).</summary>
    public int Ordinal { get; set; }

    /// <summary>Name of the ViewComponent to render (e.g., "ContentBlock", "Article").</summary>
    public string ComponentName { get; set; } = string.Empty;

    /// <summary>JSON-serialized properties to pass to the ViewComponent.</summary>
    public string ComponentPropertiesJson { get; set; } = "{}";

    /// <summary>Whether this item is active and should be rendered.</summary>
    public bool IsActive { get; set; } = true;
}
