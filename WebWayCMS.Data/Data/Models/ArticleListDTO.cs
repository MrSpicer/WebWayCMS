namespace WebWayCMS.Data.Models;

public record ArticleListDTO : IContent
{
    public Guid ContentId { get; set; }
    public ContentDTO ContentMeta { get; set; } = new();
}