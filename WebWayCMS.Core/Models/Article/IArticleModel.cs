namespace WebWayCMS.Models.Article;

/// <summary>
/// Public (read-context aware) article reads used by the Article widget. An article is only visible
/// while its article list is visible in the same read context.
/// </summary>
public interface IArticleModel
{
    /// <summary>The article with this node id, or null when it (or its list) is not visible.</summary>
    Task<ArticleViewModel?> GetViewModelAsync(Guid nodeId, CancellationToken ct = default);

    /// <summary>The article with this slug, or null when it (or its list) is not visible.</summary>
    Task<ArticleViewModel?> GetBySlugAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// The visible articles of a list, sorted by <paramref name="sortOrder"/> (<see cref="ArticleSortOrder"/>)
    /// and capped at <paramref name="maxItems"/> when positive; null when the list is not visible.
    /// </summary>
    Task<ArticleListViewModel?> GetListAsync(Guid articleListNodeId, int? maxItems, string? sortOrder, CancellationToken ct = default);
}
