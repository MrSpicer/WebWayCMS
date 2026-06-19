namespace WebWayCMS.Data.Models;

/// <summary>
/// Join record linking a content zone to its parent (page or zone) via a named slot.
/// Exactly one of ParentPageMasterId or ParentZoneId must be non-null.
/// </summary>
public record ContentZoneAssignmentDTO
{
    public Guid Id { get; set; }

    /// <summary>Human-readable slot name, e.g. "Main", "Sidebar".</summary>
    public string SlotName { get; set; } = string.Empty;

    /// <summary>FK to ContentZones.Id — the zone assigned to this slot.</summary>
    public Guid ContentZoneId { get; set; }

    /// <summary>Non-null when the parent is a page (references Page.MasterId).</summary>
    public Guid? ParentPageMasterId { get; set; }

    /// <summary>Non-null when the parent is another content zone (FK to ContentZones.Id).</summary>
    public Guid? ParentZoneId { get; set; }

    // Navigation properties
    public ContentZoneDTO ContentZone { get; set; } = null!;
    public ContentZoneDTO? ParentZone { get; set; }
}
