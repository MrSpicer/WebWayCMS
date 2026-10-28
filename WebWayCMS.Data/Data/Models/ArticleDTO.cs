namespace WebWayCMS.Data.Models;

public record ArticleDTO : IVersionedContent
{
    public Guid VersionId { get; set; }
    public ContentVersion Version { get; set; } = new();

    public string Body { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;

    // The article list it belongs to is a containment relation on the version ("ArticleListId"), not a column.
}
