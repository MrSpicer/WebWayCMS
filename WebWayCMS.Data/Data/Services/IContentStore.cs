using System.Linq.Expressions;

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

    /// <summary>The items at the read context that match <paramref name="predicate"/>, newest first.</summary>
    Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    /// <summary>
    /// The items whose read-context version has relation <paramref name="name"/> to
    /// <paramref name="targetNodeId"/>, by that relation's ordinal, then newest first.
    /// </summary>
    Task<List<T>> FindRelatedAsync(string name, Guid targetNodeId, CancellationToken ct = default);

    // version-explicit (admin/history)
    Task<T?> GetVersionAsync(Guid versionId, CancellationToken ct = default);
    Task<List<T>> GetAllVersionsAsync(Guid nodeId, CancellationToken ct = default);

    // current-draft reads (admin editing/listing)
    Task<T?> GetCurrentDraftAsync(Guid nodeId, CancellationToken ct = default);
    Task<T?> GetCurrentDraftBySlugAsync(string slug, CancellationToken ct = default);
    Task<List<T>> GetAllCurrentDraftsAsync(CancellationToken ct = default);
    Task<List<T>> GetCurrentDraftChildrenAsync(Guid? parentNodeId, CancellationToken ct = default);
    Task<HashSet<Guid>> GetPublishedNodeIdsAsync(CancellationToken ct = default);

    /// <summary>The current drafts that match <paramref name="predicate"/>, newest first.</summary>
    Task<List<T>> FindCurrentDraftsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    /// <summary>The current drafts with relation <paramref name="name"/> to <paramref name="targetNodeId"/>, by ordinal, then newest first.</summary>
    Task<List<T>> FindCurrentDraftsRelatedAsync(string name, Guid targetNodeId, CancellationToken ct = default);

    /// <summary>Counts the current drafts' relation <paramref name="name"/> rows, grouped by target node.</summary>
    Task<Dictionary<Guid, int>> CountCurrentDraftsByRelationAsync(string name, CancellationToken ct = default);

    /// <summary>Counts the current drafts grouped by <paramref name="keySelector"/>.</summary>
    Task<Dictionary<TKey, int>> CountCurrentDraftsByAsync<TKey>(Expression<Func<T, TKey>> keySelector, CancellationToken ct = default)
        where TKey : notnull;

    // writes
    Task<ContentWriteResult> SaveDraftAsync(T entity, int? expectedVersionNumber, CancellationToken ct = default);
    // creates the item's first draft on a node with a caller-chosen id (a deterministic id, so the node's
    // primary key enforces uniqueness); fails with AlreadyExistsMessage when that node already exists
    Task<ContentWriteResult> CreateWithNodeIdAsync(T entity, Guid nodeId, CancellationToken ct = default);
    Task<ContentWriteResult> PublishAsync(Guid nodeId, CancellationToken ct = default);
    Task<ContentWriteResult> UnpublishAsync(Guid nodeId, CancellationToken ct = default);
    Task<ContentWriteResult> RevertPublishAsync(Guid nodeId, Guid publishedVersionId, Guid displacedVersionId, CancellationToken ct = default);
    Task<ContentWriteResult> RestoreAsync(Guid versionId, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid nodeId, bool softDelete, CancellationToken ct = default);
    Task<bool> DeleteVersionAsync(Guid versionId, CancellationToken ct = default);
}
