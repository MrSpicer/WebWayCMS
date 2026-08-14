namespace WebWayCMS.Data.Models;

/// <summary>
/// Implemented by every versioned content type. Exposes the shared <see cref="ContentVersion"/> via
/// composition (has-a) rather than inheritance. <see cref="VersionId"/> is the shared primary key /
/// foreign key linking the content type's row to its <see cref="ContentVersion"/> row.
/// </summary>
public interface IVersionedContent
{
    Guid VersionId { get; set; }

    ContentVersion Version { get; set; }
}
