using WebWayCMS.Models.Shared;

namespace WebWayCMS.Models.Article;

public interface IArticleModel
{
    Task<ArticleViewModel?> GetPostViewModelAsync(Guid id, CancellationToken ct = default);
    Task<ArticleViewModel?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<ArticleUpsertViewModel?> GetUpsertViewModelAsync(Guid? nodeId, CancellationToken ct = default);
    Task<ArticleUpsertViewModel?> GetUpsertViewModelAsync(Guid? nodeId, Guid articleListNodeId, CancellationToken ct = default);
    Task<(bool Success, string? ErrorMessage)> SaveUpsertAsync(ArticleUpsertViewModel model, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid nodeId, CancellationToken ct = default);
    Task<VersionHistoryViewModel?> GetVersionHistoryAsync(Guid nodeId, string parentKey, CancellationToken ct = default);
    Task<ArticleUpsertViewModel?> GetUpsertModelForRestoreAsync(Guid historicalId, CancellationToken ct = default);
    Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default);
}
