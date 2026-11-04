namespace WebWayCMS.Services.ContentSeeding;

/// <summary>
/// A content seed document: a list of content items (top-level items and zone items) to create or update.
/// </summary>
public sealed class ContentSeedDocument
{
    public List<ContentSeedItem>? Items { get; set; } = new();
}
