namespace WebWayCMS.Data.Models;

/// <summary>
/// Database entity representing a named content zone that can contain multiple zone items.
/// </summary>
public record ContentZoneDTO : IContent
{
    public Guid ContentId { get; set; }
    public ContentDTO ContentMeta { get; set; } = new();

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public List<ContentZoneItemDTO> Items { get; set; } = new();
}