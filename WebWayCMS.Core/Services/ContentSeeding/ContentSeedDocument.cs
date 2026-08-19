using System.Text.Json;

namespace WebWayCMS.Services.ContentSeeding;

/// <summary>
/// A content seed document: a list of top-level content items to create or update.
/// </summary>
public sealed class ContentSeedDocument
{
    public List<ContentSeedItem>? Items { get; set; } = new();
}

/// <summary>A single top-level content item carried in a seed document.</summary>
public sealed class ContentSeedItem
{
    public Guid Id { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public bool Publish { get; set; } = true;

    public JsonElement Fields { get; set; }
}
