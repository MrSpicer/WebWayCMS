namespace WebWayCMS.Models.ContentZone;

/// <summary>A zone owned by a widget on the page, offered by the page zone editor's Unplaced panel when nothing rendered it.</summary>
public sealed record UnplacedZoneCandidate(Guid ZoneNodeId, Guid OwnerItemNodeId, string Label);
