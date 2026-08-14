using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// The single write/read engine for a versioned content type. Replaces the per-type services and the
/// old copy-on-write <c>ContentService&lt;T&gt;</c>.
/// </summary>
public interface IContentStore<T> where T : class, IVersionedContent
{
    // read-context aware (public rendering path)
    Task<T?> GetAsync(Guid nodeId, CancellationToken ct = default);
    Task<List<T>> GetAllAsync(CancellationToken ct = default);
    Task<T?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<List<T>> GetChildrenAsync(Guid parentNodeId, CancellationToken ct = default);
    Task<List<T>> GetRootsAsync(CancellationToken ct = default);

    // version-explicit (admin/history)
    Task<T?> GetVersionAsync(Guid versionId, CancellationToken ct = default);
    Task<List<T>> GetAllVersionsAsync(Guid nodeId, CancellationToken ct = default);

    // current-draft reads (admin editing/listing)
    Task<T?> GetCurrentDraftAsync(Guid nodeId, CancellationToken ct = default);
    Task<List<T>> GetAllCurrentDraftsAsync(CancellationToken ct = default);

    // writes
    Task<ContentWriteResult> SaveDraftAsync(T entity, int? expectedVersionNumber, CancellationToken ct = default);
    Task<ContentWriteResult> PublishAsync(Guid nodeId, CancellationToken ct = default);
    Task<ContentWriteResult> UnpublishAsync(Guid nodeId, CancellationToken ct = default);
    Task<ContentWriteResult> RestoreAsync(Guid versionId, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid nodeId, bool softDelete, CancellationToken ct = default);
    Task<bool> DeleteVersionAsync(Guid versionId, CancellationToken ct = default);
}
