namespace WebWayCMS.Controllers.Api;

/// <summary>
/// Request model for moving a content zone item.
/// </summary>
public class MoveItemRequest
{
    /// <summary>
    /// The node id of the zone to move the item into (its own zone for a reorder).
    /// </summary>
    public Guid TargetZoneId { get; set; }

    /// <summary>
    /// The 0-based position among the target zone's items, excluding the moved item. Clamped.
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// The moved item's version number as the client last saw it. A mismatch fails the move with
    /// the "changed by someone else" message instead of silently overwriting a concurrent edit.
    /// </summary>
    public int? ExpectedVersionNumber { get; set; }
}
