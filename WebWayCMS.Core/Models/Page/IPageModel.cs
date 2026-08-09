using WebWayCMS.Data.Models;
using WebWayCMS.Models.Shared;

namespace WebWayCMS.Models.Page;

public interface IPageModel
{
    Task<PageIndexViewModel> GetPageIndexAsync(CancellationToken ct = default);
    Task<PageUpsertViewModel?> GetPageUpsertAsync(Guid? id, CancellationToken ct = default);
    Task<(bool Success, string? ErrorMessage)> SavePageUpsertAsync(PageUpsertViewModel model, CancellationToken ct = default);
    Task<bool> DeletePageAsync(Guid id, CancellationToken ct = default);
    Task<VersionHistoryViewModel?> GetVersionHistoryAsync(Guid masterId, CancellationToken ct = default);
    Task<PageUpsertViewModel?> GetPageUpsertForRestoreAsync(Guid historicalId, CancellationToken ct = default);
    Task<bool> DeletePageVersionAsync(Guid id, CancellationToken ct = default);
}
