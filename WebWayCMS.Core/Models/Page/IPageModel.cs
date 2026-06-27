using WebWayCMS.Data.Models;
using WebWayCMS.Models.Shared;

namespace WebWayCMS.Models.Page;

/// <summary>
/// Model interface for page operations.
/// </summary>
public interface IPageModel
{
    Task<PageDTO?> GetByRouteAsync(string route, CancellationToken ct = default);
    Task<PageIndexViewModel> GetPageIndexAsync(CancellationToken ct = default);
    Task<PageUpsertViewModel?> GetPageUpsertAsync(Guid? id, CancellationToken ct = default);
    Task<(bool Success, string? ErrorMessage)> SavePageUpsertAsync(PageUpsertViewModel model, CancellationToken ct = default);
    Task<bool> DeletePageAsync(Guid id, CancellationToken ct = default);
    Task<bool> IsRouteAvailableAsync(string route, Guid? excludeMasterId = null, CancellationToken ct = default);
    Task<VersionHistoryViewModel?> GetVersionHistoryAsync(Guid masterId, CancellationToken ct = default);
    Task<PageUpsertViewModel?> GetPageUpsertForRestoreAsync(Guid historicalId, CancellationToken ct = default);
    Task<bool> DeletePageVersionAsync(Guid id, CancellationToken ct = default);
}