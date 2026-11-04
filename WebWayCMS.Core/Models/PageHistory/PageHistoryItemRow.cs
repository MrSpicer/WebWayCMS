namespace WebWayCMS.Models.PageHistory;

/// <summary>One zone item row in an entry's detail or a comparison.</summary>
public sealed class PageHistoryItemRow
{
    public Guid ItemNodeId { get; init; }

    /// <summary>The zone the item sits in on the side the row describes.</summary>
    public Guid ZoneNodeId { get; init; }

    /// <summary>The zone's name (its slot name), or a placeholder when the zone no longer exists.</summary>
    public string ZoneName { get; init; } = string.Empty;

    public string ComponentName { get; init; } = string.Empty;

    /// <summary>The item's admin label, falling back to the component name.</summary>
    public string Label { get; init; } = string.Empty;

    public int Ordinal { get; init; }
    public int VersionNumber { get; init; }
    public PageSetChange Change { get; init; }
}
