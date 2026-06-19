using WebWayCMS.Data.Models;

namespace WebWayCMS.Models.ContentZone;

public class ContentZoneIndexViewModel
{
    public List<ContentZoneDTO> Zones { get; set; } = [];
    public Guid? FilterPageId { get; set; }
    public string? FilterPageRoute { get; set; }
    public Guid? FilterParentZoneId { get; set; }
    public string? FilterParentZoneName { get; set; }
    public HashSet<Guid> ZoneIdsWithChildren { get; set; } = [];
    public Dictionary<Guid, int> AssignmentCountsByMasterId { get; set; } = [];
}
