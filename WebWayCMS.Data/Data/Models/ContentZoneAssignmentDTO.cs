namespace WebWayCMS.Data.Models;

/// <summary>
/// Join record linking a content zone to its parent (a page, or the zone item that hosts it) via a
/// named slot. Exactly one of <see cref="ParentPageNodeId"/> or <see cref="ParentItemNodeId"/> must be
/// non-null.
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

    /// <summary>
    /// Non-null for a nested zone: the node id of the zone item (e.g. a Layout widget) that renders
    /// it. Keying by the item rather than its zone means the nested content moves with the item.
    /// </summary>
    public Guid? ParentItemNodeId { get; set; }

    public ContentNode ContentZoneNode { get; set; } = null!;

    public ContentNode? ParentItemNode { get; set; }
}
