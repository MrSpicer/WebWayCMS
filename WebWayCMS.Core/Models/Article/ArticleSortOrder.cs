namespace WebWayCMS.Models.Article;

/// <summary>The sort orders the Article widget's list mode offers (stored as these string values).</summary>
public static class ArticleSortOrder
{
    public const string Newest = "newest";
    public const string Oldest = "oldest";
    public const string Title = "title";

    /// <summary>
    /// Sorts by publication date (falling back to creation date) or title. Anything unrecognized —
    /// including null — sorts newest first.
    /// </summary>
    public static IEnumerable<ArticleViewModel> Apply(IEnumerable<ArticleViewModel> articles, string? sortOrder)
        => sortOrder switch
        {
            Oldest => articles.OrderBy(PublishedOn),
            Title => articles.OrderBy(a => a.Title, StringComparer.OrdinalIgnoreCase),
            _ => articles.OrderByDescending(PublishedOn)
        };

    private static DateTime? PublishedOn(ArticleViewModel article)
        => article.PublicationDate ?? article.CreationDate;
}
