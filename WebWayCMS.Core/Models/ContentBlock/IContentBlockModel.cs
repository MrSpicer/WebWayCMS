using WebWayCMS.Models.Shared;

namespace WebWayCMS.Models.ContentBlock;

public interface IContentBlockModel
{
    Task<ContentBlockViewModel?> GetViewModelByNodeIdAsync(Guid nodeId, CancellationToken ct = default);
    Task<ContentBlockIndexViewModel> GetContentBlockIndexAsync(CancellationToken ct = default);
    Task<ContentBlockUpsertViewModel?> GetUpsertModelAsync(Guid? nodeId, CancellationToken ct = default);
    Task<(bool Success, string? ErrorMessage)> SaveUpsertAsync(ContentBlockUpsertViewModel model, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid nodeId, CancellationToken ct = default);
    Task<VersionHistoryViewModel?> GetVersionHistoryAsync(Guid nodeId, CancellationToken ct = default);
    Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default);
}
