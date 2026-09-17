using WebWayCMS.Models.Shared;

namespace WebWayCMS.Models.Image;

public interface IImageModel
{
    Task<ImageViewModel?> GetViewModelByNodeIdAsync(Guid nodeId, CancellationToken ct = default);
    Task<ImageIndexViewModel> GetImageIndexAsync(CancellationToken ct = default);
    Task<ImageUpsertViewModel?> GetUpsertModelAsync(Guid? nodeId, CancellationToken ct = default);
    Task<(bool Success, string? ErrorMessage)> SaveUpsertAsync(ImageUpsertViewModel model, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid nodeId, CancellationToken ct = default);
    Task<VersionHistoryViewModel?> GetVersionHistoryAsync(Guid nodeId, CancellationToken ct = default);
    Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default);
}
