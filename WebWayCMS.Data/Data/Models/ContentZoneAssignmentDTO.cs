namespace WebWayCMS.Data.Models;

/// <summary>
/// Join record linking a content zone to its parent (page or zone) via a named slot.
/// Exactly one of <see cref="ParentPageNodeId"/> or <see cref="ParentZoneNodeId"/> must be non-null.
/// </summary>
public record ContentZoneAssignmentDTO
{
    public Guid Id { get; set; }

    /// <summary>Human-readable slot name, e.g. "Main", "Sidebar".</summary>
    public string SlotName { get; set; } = string.Empty;

    /// <summary>FK to ContentNodes.Id — the zone node assigned to this slot.</summary>
    public Guid ContentZoneNodeId { get; set; }

    /// <summary>Non-null when the parent is a page (references ContentNode.Id).</summary>
    public Guid? ParentPageNodeId { get; set; }

    /// <summary>Non-null when the parent is another content zone (FK to ContentNode.Id).</summary>
    public Guid? ParentZoneNodeId { get; set; }

    public ContentNode ContentZoneNode { get; set; } = null!;

    public ContentNode? ParentZoneNode { get; set; }
}
