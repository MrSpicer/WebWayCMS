using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebWayCMS.Services.ContentSeeding;

/// <summary>
/// A single content item carried in a seed document: a top-level content item, or — when
/// <see cref="Zone"/> is set — a zone item placed in the zone that locator names.
/// </summary>
public sealed class ContentSeedItem
{
    public Guid Id { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public bool Publish { get; set; } = true;

    public JsonElement Fields { get; set; }

    /// <summary>
    /// The zone a zone item belongs to; null for a top-level item. Omitted from the serialized item when
    /// null, so adding it left every existing item's content hash unchanged.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentSeedZoneLocator? Zone { get; set; }
}
