namespace WebWayCMS.Models.ContentZone;

public sealed class ContentZoneItemsIndexViewModel
{
    public Guid ZoneId { get; set; }
    public string ZoneName { get; set; } = string.Empty;
    public string ZoneDescription { get; set; } = string.Empty;
    public List<ContentZoneItemViewModel> Items { get; set; } = new();
}

/// <summary>
/// Flat projection of a content zone item, mirroring the shape the zone API returns so the client
/// (admin view and MCP <c>list_children</c>) never serializes the entire version/node graph. The
/// <see cref="Id"/> is the item's node id — the same id <c>get_child</c> accepts.
/// </summary>
public sealed class ContentZoneItemViewModel
{
    public Guid Id { get; set; }
    public Guid ZoneId { get; set; }
    public string ComponentName { get; set; } = string.Empty;
    public string ComponentPropertiesJson { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public bool IsActive { get; set; }
}
