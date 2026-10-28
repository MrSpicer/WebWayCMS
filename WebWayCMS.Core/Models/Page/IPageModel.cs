using WebWayCMS.Data.Services;
using WebWayCMS.Models.Shared;

namespace WebWayCMS.Models.Page;

public interface IPageModel
{
    Task<PageIndexViewModel> GetPageIndexAsync(CancellationToken ct = default);
    Task<(bool Success, string? ErrorMessage)> SavePageUpsertAsync(PageUpsertViewModel model, CancellationToken ct = default);
    Task<bool> DeletePageAsync(Guid nodeId, CancellationToken ct = default);
    Task<VersionHistoryViewModel?> GetVersionHistoryAsync(Guid nodeId, CancellationToken ct = default);
    Task<bool> DeletePageVersionAsync(Guid id, CancellationToken ct = default);
    Task<(bool Success, string? ErrorMessage, int DescendantsUpdated)> PublishPageAsync(Guid nodeId, CancellationToken ct = default);
    Task<(bool Success, string? ErrorMessage)> UnpublishPageAsync(Guid nodeId, CancellationToken ct = default);

    /// <summary>
    /// Publishes only the listed pending zone items of an already-published page (registering their widget
    /// routes), leaving the page and its other drafts untouched. <see cref="PageZonePublishResult.Errors"/>
    /// also carries route-registration failures for items that did go live.
    /// </summary>
    Task<PageZonePublishResult> PublishZoneItemsAsync(Guid pageNodeId, IReadOnlyCollection<Guid> itemNodeIds, CancellationToken ct = default);
}
