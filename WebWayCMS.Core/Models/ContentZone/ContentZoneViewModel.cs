using System.Threading;
using System.Threading.Tasks;

namespace WebWayCMS.Models.ContentZone;

public class ContentZoneViewModel
{
    /// <summary>
    /// The database ID of the content zone (Guid.Empty if the zone doesn't exist yet).
    /// Used internally for saving items but never displayed to users.
    /// </summary>
    public Guid Id { get; set; } = Guid.Empty;

    /// <summary>
    /// The unique path/name that identifies this content zone (includes ordinal suffix).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The raw zone name passed to the ViewComponent (without ordinal suffix).
    /// Used for display purposes in admin view.
    /// </summary>
    public string RawZoneName { get; set; } = string.Empty;

    /// <summary>
    /// The zone objects (items) to render in this zone.
    /// </summary>
    public List<ContentZoneObject> ZoneObjects { get; set; } = new();

    /// <summary>
    /// The page MasterId this zone belongs to (if page-scoped). Used for assignment-based zone creation fallback.
    /// </summary>
    public Guid? ParentPageMasterId { get; set; }

    /// <summary>
    /// Indicates whether the current user can edit this zone.
    /// </summary>
    public bool CanEdit { get; set; } = false;
}