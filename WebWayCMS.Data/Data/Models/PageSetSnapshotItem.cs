namespace WebWayCMS.Data.Models;

/// <summary>One zone item version captured by a <see cref="PageSetSnapshot"/>.</summary>
public record PageSetSnapshotItem
{
    public Guid SnapshotId { get; set; }

    public Guid ItemNodeId { get; set; }

    public Guid ItemVersionId { get; set; }

    /// <summary>The zone the version sat in (denormalized from the item version for listings).</summary>
    public Guid ZoneNodeId { get; set; }

    /// <summary>The version's ordinal (denormalized from the item version for listings).</summary>
    public int Ordinal { get; set; }
}
