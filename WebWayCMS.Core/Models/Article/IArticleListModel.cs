using WebWayCMS.Models.Shared;

namespace WebWayCMS.Models.Article;

public interface IArticleListModel
{
    Task<ArticleListViewModel> GetIndexViewModelAsync(CancellationToken ct = default);
    Task<ArticleListIndexViewModel> GetArticleListIndexAsync(CancellationToken ct = default);
    Task<ArticleListUpsertViewModel?> GetArticleListUpsertAsync(Guid? nodeId, CancellationToken ct = default);
    Task<(bool Success, string? ErrorMessage)> SaveArticleListUpsertAsync(ArticleListUpsertViewModel model, CancellationToken ct = default);
    Task<bool> DeleteArticleListAsync(Guid nodeId, CancellationToken ct = default);
    Task<ArticleListViewModel?> GetArticlesForListAsync(Guid articleListNodeId, CancellationToken ct = default);
    Task<ArticleListViewModel?> GetArticlesForListBySlugAsync(string slug, CancellationToken ct = default);
    Task<VersionHistoryViewModel?> GetVersionHistoryAsync(Guid nodeId, CancellationToken ct = default);
    Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default);
}
