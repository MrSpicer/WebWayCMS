using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
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

/// <inheritdoc cref="IPageHistoryRecorder"/>
public sealed class PageHistoryRecorder : IPageHistoryRecorder
{
    private readonly Serilog.ILogger _logger = Serilog.Log.ForContext<PageHistoryRecorder>();

    private readonly CmsDbContext _context;
    private readonly PageZoneTree _tree;
    private readonly IChangeSetScope _changeSetScope;
    private readonly IContentUserContext _userContext;

    public PageHistoryRecorder(CmsDbContext context, PageZoneTree tree, IChangeSetScope changeSetScope, IContentUserContext userContext)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        _changeSetScope = changeSetScope ?? throw new ArgumentNullException(nameof(changeSetScope));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
    }

    public async Task<PageSetSnapshot?> RecordDraftAsync(Guid pageNodeId, string note, CancellationToken ct = default)
    {
        var page = await _tree.Pages()
            .FirstOrDefaultAsync(p => p.Version.NodeId == pageNodeId && p.Version.IsCurrentDraft, ct);
        if (page == null)
            return null;

        return await RecordAsync(page, await _tree.GetDraftTreeItemsAsync(pageNodeId, ct), PageSetSnapshotKind.Draft, note, ct);
    }

    public async Task<PageSetSnapshot?> RecordPublishedAsync(Guid pageNodeId, string note, CancellationToken ct = default)
    {
        var page = await _tree.Pages()
            .FirstOrDefaultAsync(p => p.Version.NodeId == pageNodeId && p.Version.State == ContentVersionState.Published, ct);
        if (page == null)
            return null;

        return await RecordAsync(page, await _tree.GetLiveTreeItemsAsync(pageNodeId, ct), PageSetSnapshotKind.Published, note, ct);
    }

    private async Task<PageSetSnapshot?> RecordAsync(
        PageDTO page, List<ContentZoneItemDTO> items, PageSetSnapshotKind kind, string note, CancellationToken ct)
    {
        var pageNodeId = page.Version.NodeId;
        var hash = PageSetEquality.ContentHash(page, items);

        // A draft entry repeats nothing already recorded (a save that changed nothing, an unpublish); a
        // published entry repeats only the last publish, since a publish of the same drafts still records
        // that they went live.
        var latest = await _context.Set<PageSetSnapshot>()
            .AsNoTracking()
            .Where(s => s.PageNodeId == pageNodeId && (kind == PageSetSnapshotKind.Draft || s.Kind == kind))
            .OrderByDescending(s => s.CreatedUtc)
            .ThenByDescending(s => s.Id)
            .Select(s => s.StateHash)
            .FirstOrDefaultAsync(ct);
        if (latest == hash)
            return null;

        var snapshot = new PageSetSnapshot
        {
            Id = Guid.NewGuid(),
            PageNodeId = pageNodeId,
            PageVersionId = page.VersionId,
            Kind = kind,
            Note = TruncateNote(note),
            StateHash = hash,
            CreatedUtc = DateTime.UtcNow,
            CreatedBy = _userContext.CurrentUserId,
            ChangeSetId = _changeSetScope.Current,
            ItemCount = items.Count,
            Items = items.Select(i => new PageSetSnapshotItem
            {
                ItemNodeId = i.Version.NodeId,
                ItemVersionId = i.VersionId,
                ZoneNodeId = i.ContentZoneNodeId,
                Ordinal = i.Ordinal
            }).ToList()
        };

        // History is a record of the change, never a condition of it: a failed write is logged and the
        // entry untracked, so the scoped context does not replay it on the next SaveChanges.
        _context.Set<PageSetSnapshot>().Add(snapshot);
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.Error(ex, "Recording page history for page {PageNodeId} failed.", pageNodeId);
            _context.Entry(snapshot).State = EntityState.Detached;
            foreach (var item in snapshot.Items)
                _context.Entry(item).State = EntityState.Detached;
            return null;
        }

        return snapshot;
    }

    /// <summary>
    /// Truncates a note to 500 characters, never splitting a UTF-16 surrogate pair at the cut: a forward
    /// cut can only orphan the high half of a pair, so a trailing high surrogate is dropped with it.
    /// </summary>
    private static string TruncateNote(string note)
    {
        const int maxLength = 500;
        if (note.Length <= maxLength)
        {
            return note;
        }

        var cut = note[..maxLength];
        if (char.IsHighSurrogate(cut[^1]))
        {
            cut = cut[..^1];
        }

        return cut;
    }
}
