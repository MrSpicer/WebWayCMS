using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// Writes page-history entries (<see cref="PageSetSnapshot"/>): one after every change to a page set
/// (<see cref="RecordDraftAsync"/>) and one after every publish (<see cref="RecordPublishedAsync"/>).
/// </summary>
public interface IPageHistoryRecorder
{
    /// <summary>
    /// Records the page's current draft and the active current drafts in its zone tree. Nothing is written
    /// (null) when the page does not exist, the state equals the latest entry of either kind, or the write
    /// fails (logged — history never fails the change it records).
    /// </summary>
    Task<PageSetSnapshot?> RecordDraftAsync(Guid pageNodeId, string note, CancellationToken ct = default);

    /// <summary>
    /// Records what is live: the published page and the active published items in its tree. Nothing is
    /// written when the page is not published, the state equals the latest Published entry, or the write fails.
    /// </summary>
    Task<PageSetSnapshot?> RecordPublishedAsync(Guid pageNodeId, string note, CancellationToken ct = default);
}
