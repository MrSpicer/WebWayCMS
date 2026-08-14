using WebWayCMS.Data.Models;

namespace WebWayCMS.Models.ContentZone;

public sealed class ContentZoneItemsIndexViewModel
{
    public ContentZoneDTO Zone { get; set; } = new();
    public List<ContentZoneItemDTO> Items { get; set; } = new();
}
