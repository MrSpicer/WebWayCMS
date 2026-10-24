using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>A page and its zone-tree items, as of a snapshot or as current drafts.</summary>
public sealed record PageSetState(PageDTO Page, List<ContentZoneItemDTO> Items);

/// <summary>
/// The metadata a historical zone item version can be saved back with today (the List→Single
/// grandfathering rule lives with the widget registry, outside the Data tier).
/// </summary>
public delegate string? RestorableMetadata(string componentName, string? historicalJson, string? currentJson);

/// <summary>
/// Page-set history: an entry (a page plus every active item in its zone tree) after every change to
/// the page set and after every publish, and the operations on those entries (read, compare, preview,
/// revert, delete), plus discarding a page's unpublished changes.
/// </summary>
public interface IPageHistoryService
{
    /// <summary>Returned by <see cref="RevertAsync"/> when the page set changed since the caller read it.</summary>
    const string StaleStateMessage = "This page or one of its widgets was changed by someone else since you opened it.";

    /// <summary>Returned when a snapshot does not exist (or does not belong to the page).</summary>
    const string NotFoundMessage = "History entry not found.";

    /// <summary>Returned by <see cref="DiscardUnpublishedChangesAsync"/> for a page that is not live.</summary>
    const string NotPublishedMessage = "This page isn't published, so there is no published state to return to. Use page history instead.";

    /// <summary>
    /// Records what is live for the page now (a Published entry, see
    /// <see cref="IPageHistoryRecorder.RecordPublishedAsync"/>). Called after every publish.
    /// </summary>
    Task<PageSetSnapshot?> CaptureAsync(Guid pageNodeId, CancellationToken ct = default);

    /// <summary>Records the page's current drafts as a Draft entry (see <see cref="IPageHistoryRecorder.RecordDraftAsync"/>).</summary>
    Task<PageSetSnapshot?> RecordDraftAsync(Guid pageNodeId, string note, CancellationToken ct = default);

    /// <summary>The page's snapshots, newest first, with their items.</summary>
    Task<List<PageSetSnapshot>> ListAsync(Guid pageNodeId, CancellationToken ct = default);

    /// <summary>The id of the page's newest snapshot (the first of <see cref="ListAsync"/>), or null when it has none.</summary>
    Task<Guid?> GetLatestIdAsync(Guid pageNodeId, CancellationToken ct = default);

    /// <summary>One snapshot with its items, or null.</summary>
    Task<PageSetSnapshot?> GetAsync(Guid snapshotId, CancellationToken ct = default);

    /// <summary>
    /// The snapshot after <paramref name="snapshot"/> in <see cref="ListAsync"/>'s order (the one recorded
    /// just before it, ties on the timestamp broken the same way), or null.
    /// </summary>
    Task<PageSetSnapshot?> GetPreviousAsync(PageSetSnapshot snapshot, CancellationToken ct = default);

    /// <summary>The page and item versions a snapshot captured, or null when it does not exist.</summary>
    Task<PageSetState?> GetSnapshotStateAsync(Guid snapshotId, CancellationToken ct = default);

    /// <summary>
    /// The page's current draft and the current drafts of every item in its zone tree (removed, i.e.
    /// inactive, items included), or null when the page does not exist.
    /// </summary>
    Task<PageSetState?> GetCurrentStateAsync(Guid pageNodeId, CancellationToken ct = default);

    /// <summary><see cref="PageSetEquality.StateToken"/> over the current drafts, or null when the page does not exist.</summary>
    Task<string?> GetDraftStateTokenAsync(Guid pageNodeId, CancellationToken ct = default);

    /// <summary>The snapshots that captured <paramref name="versionId"/> (as the page or an item), newest first.</summary>
    Task<List<PageSetSnapshot>> GetSnapshotsReferencingVersionAsync(Guid versionId, CancellationToken ct = default);

    /// <summary>
    /// The read filter that renders <paramref name="snapshotId"/>: the page and every node now or then in
    /// its zone tree are pinned to the captured versions (so a later item is hidden); all else reads
    /// published. Null when the snapshot does not exist.
    /// </summary>
    Task<ContentReadSnapshot?> GetReadSnapshotAsync(Guid snapshotId, CancellationToken ct = default);

    /// <summary>Deletes the snapshot record only; the versions it referenced are kept.</summary>
    Task<bool> DeleteAsync(Guid snapshotId, CancellationToken ct = default);

    /// <summary>
    /// Restores the page set to <paramref name="snapshotId"/> as new drafts, in one transaction: the page
    /// and each captured item get the captured content and placement (items already equal are skipped),
    /// and every other item in the tree becomes an inactive draft. Nothing is published or deleted; the
    /// revert is itself recorded as an entry. Refused with <see cref="StaleStateMessage"/> when the drafts
    /// no longer match <paramref name="expectedStateToken"/>.
    /// </summary>
    Task<(bool Success, string? ErrorMessage)> RevertAsync(
        Guid snapshotId, string expectedStateToken, RestorableMetadata restorableMetadata, CancellationToken ct = default);

    /// <summary>
    /// Returns the page set to what is live, as new drafts (the same mechanics as <see cref="RevertAsync"/>
    /// with the live state as the target): edited items get their live content and placement back, items
    /// never live (or live as removed) become inactive drafts, and the page's fields return to the
    /// published ones. Nothing is deleted, and the discard is recorded as an entry, so it can be undone.
    /// Refused with <see cref="NotPublishedMessage"/> when the page is not published.
    /// </summary>
    Task<(bool Success, string? ErrorMessage)> DiscardUnpublishedChangesAsync(Guid pageNodeId, CancellationToken ct = default);

    /// <summary>
    /// The pages (among <paramref name="pageNodeIds"/>) whose drafts differ from what is live — the page's
    /// own fields or anything in its zone tree, compared by content. A page that is not published counts
    /// as having unpublished changes.
    /// </summary>
    Task<HashSet<Guid>> GetPagesWithUnpublishedChangesAsync(IEnumerable<Guid> pageNodeIds, CancellationToken ct = default);
}
