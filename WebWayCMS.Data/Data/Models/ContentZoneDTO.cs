namespace WebWayCMS.Data.Models;

/// <summary>
/// Database entity representing a named content zone. Items are no longer a static navigation —
/// which item <em>versions</em> belong to a zone depends on the read context, so they are resolved
/// through <c>IContentZoneService.GetItemsAsync</c> instead.
/// </summary>
public record ContentZoneDTO : IVersionedContent
{
    public Guid VersionId { get; set; }
    public ContentVersion Version { get; set; } = new();

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}
