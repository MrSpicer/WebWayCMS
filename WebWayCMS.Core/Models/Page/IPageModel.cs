using WebWayCMS.Models.Shared;

namespace WebWayCMS.Models.Page;

public interface IPageModel
{
    Task<PageIndexViewModel> GetPageIndexAsync(CancellationToken ct = default);
    Task<PageUpsertViewModel?> GetPageUpsertAsync(Guid? nodeId, CancellationToken ct = default);
    Task<(bool Success, string? ErrorMessage)> SavePageUpsertAsync(PageUpsertViewModel model, CancellationToken ct = default);
    Task<bool> DeletePageAsync(Guid nodeId, CancellationToken ct = default);
    Task<VersionHistoryViewModel?> GetVersionHistoryAsync(Guid nodeId, CancellationToken ct = default);
    Task<bool> DeletePageVersionAsync(Guid id, CancellationToken ct = default);
    Task<(bool Success, string? ErrorMessage)> PublishPageAsync(Guid nodeId, CancellationToken ct = default);
    Task<(bool Success, string? ErrorMessage)> UnpublishPageAsync(Guid nodeId, CancellationToken ct = default);
}
