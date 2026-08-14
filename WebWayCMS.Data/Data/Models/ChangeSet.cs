namespace WebWayCMS.Data.Models;

public enum ChangeSetKind
{
    Save = 0,
    Publish = 1,
    Unpublish = 2,
    Restore = 3,
    Delete = 4
}

/// <summary>
/// Groups every version written by one save/publish/restore operation, so a composite save
/// (page + its zones + items + route) can be read back as a single history entry.
/// </summary>
public record ChangeSet
{
    public Guid Id { get; set; }

    public DateTime CreatedUtc { get; set; }

    public Guid? CreatedBy { get; set; }

    public ChangeSetKind Kind { get; set; }

    /// <summary>The item the user acted on.</summary>
    public Guid? RootNodeId { get; set; }

    public string? Note { get; set; }
}
