namespace WebWayCMS.Models.Article;

public sealed class ArticleListViewModel
{
    public Guid ArticleListId { get; set; }
    public string ArticleListTitle { get; set; } = string.Empty;
    public string ArticleListSlug { get; set; } = string.Empty;
    public List<ArticleViewModel> Articles { get; set; } = new();
}
