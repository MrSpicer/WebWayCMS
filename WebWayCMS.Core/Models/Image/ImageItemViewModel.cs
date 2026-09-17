namespace WebWayCMS.Models.Image;

public sealed class ImageItemViewModel
{
    public Guid NodeId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string BlobHash { get; init; } = string.Empty;
    public string AltText { get; init; } = string.Empty;
    public bool IsPublished { get; init; }
    public DateTime CreationDate { get; init; }
    public DateTime ModificationDate { get; init; }
}
