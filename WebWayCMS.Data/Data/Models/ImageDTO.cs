namespace WebWayCMS.Data.Models;

/// <summary>
/// A versioned image content item. Holds only a reference to the stored bytes — the
/// <see cref="BlobHash"/> of a <see cref="MediaBlobDTO"/> — so that the same upload can back
/// many items and so that no view model ever has to carry binary data.
/// </summary>
public record ImageDTO : IVersionedContent
{
    public Guid VersionId { get; set; }
    public ContentVersion Version { get; set; } = new();

    /// <summary>Lowercase hex SHA-256 of the referenced media blob.</summary>
    public string BlobHash { get; set; } = string.Empty;

    public string AltText { get; set; } = string.Empty;

    public string? Caption { get; set; }
}
