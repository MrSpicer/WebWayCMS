namespace WebWayCMS.Models.ContentZone;

/// <summary>One zone slot of a page, as listed by the page zone editor.</summary>
public sealed record PageZoneSlot(string SlotName, Guid ZoneNodeId);

/// <summary>
/// Render model for the page zone editor: every zone assigned to the page, stacked in the zone
/// editor, under a toolbar that publishes or discards the page's drafts.
/// </summary>
public sealed class PageZonesEditViewModel
{
    public Guid PageNodeId { get; init; }

    public string PageTitle { get; init; } = string.Empty;

    /// <summary>True when the page has a published version (it is live).</summary>
    public bool IsPublished { get; init; }

    /// <summary>True when the page fields or any of its zone items have unpublished drafts.</summary>
    public bool HasPendingChanges { get; init; }

    /// <summary>True when the pending changes include zone-item drafts (what Discard throws away).</summary>
    public bool HasPendingZoneChanges { get; init; }

    public IReadOnlyList<PageZoneSlot> Slots { get; init; } = [];
}
