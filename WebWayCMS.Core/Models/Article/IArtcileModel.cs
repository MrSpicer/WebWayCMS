using WebWayCMS.Models.Shared;

namespace WebWayCMS.Models.Article;

public interface IArticleModel
{
    Task<ArticleViewModel?> GetPostViewModelAsync(Guid id, CancellationToken ct = default);
    Task<ArticleViewModel?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<ArticleUpsertViewModel?> GetUpsertViewModelAsync(Guid? id, CancellationToken ct = default);
    Task<ArticleUpsertViewModel?> GetUpsertViewModelAsync(Guid? id, Guid articleListId, CancellationToken ct = default);
    Task<(bool Success, string? ErrorMessage)> SaveUpsertAsync(ArticleUpsertViewModel model, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<VersionHistoryViewModel?> GetVersionHistoryAsync(Guid masterId, string parentKey, CancellationToken ct = default);
    Task<ArticleUpsertViewModel?> GetUpsertModelForRestoreAsync(Guid historicalId, CancellationToken ct = default);
    Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default);
}