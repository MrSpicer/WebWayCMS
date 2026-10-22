namespace WebWayCMS.Models.Article;

public sealed class ArticleItemViewModel
{
    public Guid NodeId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string AuthorName { get; init; } = string.Empty;
    public bool IsPublished { get; init; }
    public Guid ArticleListId { get; init; }
    public string ArticleListTitle { get; set; } = string.Empty;
    public DateTime CreationDate { get; init; }
    public DateTime ModificationDate { get; init; }
}
