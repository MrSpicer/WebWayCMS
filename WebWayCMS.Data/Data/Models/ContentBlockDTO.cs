namespace WebWayCMS.Data.Models;

public record ContentBlockDTO : IVersionedContent
{
    public Guid VersionId { get; set; }
    public ContentVersion Version { get; set; } = new();

    public string Content { get; set; } = string.Empty;
}
