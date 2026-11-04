using WebWayCMS.Data.Services;

namespace WebWayCMS.Models.PageHistory;

/// <summary>
/// The page editor's page-set history: list a page's publish snapshots, inspect one (against the entry
/// before it, or against the current drafts), revert the page to one as drafts, or delete one. Used by
/// the admin page-history screens and the MCP page-history tools.
/// </summary>
public interface IPageHistoryModel
{
    /// <summary>
    /// One page of the page's entries, newest first (<paramref name="skip"/> clamped to at least 0,
    /// <paramref name="take"/> to 1..<see cref="IPageHistoryService.MaxPageSize"/>); null when the page
    /// does not exist.
    /// </summary>
    Task<PageHistoryIndexViewModel?> GetIndexAsync(
        Guid pageNodeId, int skip = 0, int take = IPageHistoryService.DefaultPageSize, CancellationToken ct = default);

    /// <summary>An entry and what it changed; null when the page or entry does not exist (or do not match).</summary>
    Task<PageHistoryEntryViewModel?> GetEntryAsync(Guid pageNodeId, Guid entryId, CancellationToken ct = default);

    /// <summary>The current drafts compared with an entry, plus the token a revert must present.</summary>
    Task<PageHistoryCompareViewModel?> GetCompareAsync(Guid pageNodeId, Guid entryId, CancellationToken ct = default);

    /// <summary>Reverts the page set to the entry as drafts (see <see cref="IPageHistoryService.RevertAsync"/>).</summary>
    Task<(bool Success, string? ErrorMessage)> RevertAsync(Guid pageNodeId, Guid entryId, string stateToken, CancellationToken ct = default);

    /// <summary>Deletes the entry record (its versions are kept); false when it does not exist or belong to the page.</summary>
    Task<bool> DeleteAsync(Guid pageNodeId, Guid entryId, CancellationToken ct = default);
}
