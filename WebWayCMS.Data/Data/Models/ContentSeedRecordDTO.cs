namespace WebWayCMS.Data.Models;

/// <summary>
/// Ledger row mapping a JSON content seed item's stable <see cref="SeedId"/> key to the
/// CMS-generated <see cref="NodeId"/> it produced (or updated), plus the content hash last applied.
/// Not versioned — it is written by <c>JsonContentSeeder</c> and read back on every boot.
/// </summary>
public record ContentSeedRecordDTO
{
    /// <summary>The stable seed key carried in the JSON file's <c>id</c> field.</summary>
    public Guid SeedId { get; set; }

    public string ContentTypeKey { get; set; } = string.Empty;

    /// <summary>The <see cref="ContentNode.Id"/> the seed key maps to.</summary>
    public Guid NodeId { get; set; }

    /// <summary>SHA-256 hash of the last applied item, used to skip unchanged items on reboot.</summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>Name of the source (embedded resource or file path) the item came from.</summary>
    public string Source { get; set; } = string.Empty;

    public DateTime AppliedUtc { get; set; }
}
