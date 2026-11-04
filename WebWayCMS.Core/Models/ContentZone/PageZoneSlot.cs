namespace WebWayCMS.Models.ContentZone;

/// <summary>One zone slot of a page, as listed by the page zone editor.</summary>
public sealed record PageZoneSlot(string SlotName, Guid ZoneNodeId);
