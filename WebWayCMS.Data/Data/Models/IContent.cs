namespace WebWayCMS.Data.Models;

/// <summary>
/// Implemented by every content type. Exposes the shared <see cref="ContentDTO"/> fields via
/// composition (has-a) rather than inheritance. <see cref="ContentId"/> is the shared primary
/// key / foreign key linking the content type's row to its <see cref="ContentMeta"/> row.
/// </summary>
public interface IContent
{
    Guid ContentId { get; set; }

    ContentDTO ContentMeta { get; set; }
}
