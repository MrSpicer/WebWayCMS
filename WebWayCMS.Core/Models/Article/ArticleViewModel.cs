namespace WebWayCMS.Models.Article;


public sealed class ArticleViewModel : BaseContentViewModel
{
    public string Body { get; init; } = string.Empty;
    public string AuthorName { get; init; } = string.Empty;
    public Guid ArticleListId { get; init; }
}