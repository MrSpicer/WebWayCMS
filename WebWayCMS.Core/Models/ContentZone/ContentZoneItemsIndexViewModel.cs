namespace WebWayCMS.Models.ContentZone;

public sealed class ContentZoneItemsIndexViewModel
{
    public Guid ZoneId { get; set; }
    public string ZoneName { get; set; } = string.Empty;
    public string ZoneDescription { get; set; } = string.Empty;
    public List<ContentZoneItemViewModel> Items { get; set; } = new();
}
