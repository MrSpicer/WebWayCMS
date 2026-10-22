using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebWayCMS.Services.ContentSeeding;

/// <summary>
/// A content seed document: a list of content items (top-level items and zone items) to create or update.
/// </summary>
public sealed class ContentSeedDocument
{
    public List<ContentSeedItem>? Items { get; set; } = new();
}

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

/// <summary>
/// Names the zone a seeded zone item goes into: exactly one of <see cref="Page"/> or <see cref="Item"/>
/// (each with a <see cref="Slot"/>), or <see cref="Name"/> for a global zone. <see cref="Page"/> and
/// <see cref="Item"/> take a node id or an <c>@seed:{guid}</c> reference. The zone is created when it
/// does not exist yet, exactly as rendering the page would create it.
/// </summary>
public sealed class ContentSeedZoneLocator
{
    /// <summary>The owning page, for a zone a page template renders.</summary>
    public string? Page { get; set; }

    /// <summary>The owning zone item, for a nested zone (e.g. a Layout column).</summary>
    public string? Item { get; set; }

    /// <summary>A global, name-based zone.</summary>
    public string? Name { get; set; }

    /// <summary>The slot name the template renders, e.g. <c>Main</c>. Required with Page or Item.</summary>
    public string? Slot { get; set; }
}
