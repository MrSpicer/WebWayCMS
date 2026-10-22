namespace WebWayCMS.Models.Article;

public sealed class ArticleListViewModel
{
    public Guid ArticleListId { get; set; }
    public string ArticleListTitle { get; set; } = string.Empty;
    public string ArticleListSlug { get; set; } = string.Empty;
    public List<ArticleViewModel> Articles { get; set; } = new();

    /// <summary>
    /// The URL the list's cards link under (<c>{DetailBaseUrl}/{slug}</c>), set by the Article widget; null
    /// when there is no public URL to link to (a never-published page in draft preview).
    /// </summary>
    public string? DetailBaseUrl { get; set; } = string.Empty;
}