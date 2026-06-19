namespace WebWayCMS.Data.Models;

public record ArticleDTO : IContent
{
    public Guid ContentId { get; set; }
    public ContentDTO ContentMeta { get; set; } = new();

    public string Body { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public Guid ArticleListMasterId { get; set; }
}
