namespace WebWayCMS.Data.Models;

/// <summary>
/// Shared content fields common to every content type. Persisted to a single shared "Content"
/// table; each content type references its row via a 1:1 relationship (see <see cref="IContent"/>).
/// </summary>
public record ContentDTO
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public Guid CreatedBy { get; set; }

    public Guid LastModifiedBy { get; set; }

    public DateTime PublicationDate { get; set; }

    public DateTime? PublicationEndDate { get; set; }

    public DateTime ModificationDate { get; set; }

    public DateTime CreationDate { get; set; }

    public bool IsPublished { get; set; }

    public bool IsArchived { get; set; }

    public bool IsHidden { get; set; }

    public bool IsDeleted { get; set; }

    public Guid MasterId { get; set; }

    public Guid? ParentMasterId { get; set; }

    public int Version { get; set; }

    public List<CustomField> CustomFields { get; set; } = new();
}
