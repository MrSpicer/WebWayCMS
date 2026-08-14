namespace WebWayCMS.Data.Models;

public record ArticleListDTO : IVersionedContent
{
    public Guid VersionId { get; set; }
    public ContentVersion Version { get; set; } = new();
}
