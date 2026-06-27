using WebWayCMS.Models.Shared;

namespace WebWayCMS.Models.ContentBlock;

public interface IContentBlockModel
{
    Task<ContentBlockViewModel?> GetViewModelByMasterIdAsync(Guid masterId, CancellationToken ct = default);
    Task<ContentBlockIndexViewModel> GetContentBlockIndexAsync(CancellationToken ct = default);
    Task<ContentBlockUpsertViewModel?> GetUpsertModelAsync(Guid? id, CancellationToken ct = default);
    Task<(bool Success, string? ErrorMessage)> SaveUpsertAsync(ContentBlockUpsertViewModel model, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<VersionHistoryViewModel?> GetVersionHistoryAsync(Guid masterId, CancellationToken ct = default);
    Task<ContentBlockUpsertViewModel?> GetUpsertModelForRestoreAsync(Guid historicalId, CancellationToken ct = default);
    Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default);
}