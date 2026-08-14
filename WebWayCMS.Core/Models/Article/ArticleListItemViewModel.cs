namespace WebWayCMS.Models.Article;

public sealed class ArticleListItemViewModel
{
    public Guid NodeId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public bool IsPublished { get; init; }
    public DateTime CreationDate { get; init; }
    public DateTime ModificationDate { get; init; }
    public int ArticleCount { get; set; }
}
