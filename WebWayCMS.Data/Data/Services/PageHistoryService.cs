using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <inheritdoc cref="IPageHistoryService"/>
public sealed class PageHistoryService : IPageHistoryService
{
    private readonly CmsDbContext _context;
    private readonly IContentStore<PageDTO> _pageStore;
    private readonly IContentStore<ContentZoneItemDTO> _itemStore;
    private readonly PageZoneTree _tree;
    private readonly IPageHistoryRecorder _recorder;
    private readonly IChangeSetScope _changeSetScope;
    private readonly ICmsTransaction _transaction;

    public PageHistoryService(
        CmsDbContext context,
        IContentStore<PageDTO> pageStore,
        IContentStore<ContentZoneItemDTO> itemStore,
        PageZoneTree tree,
        IPageHistoryRecorder recorder,
        IChangeSetScope changeSetScope,
        ICmsTransaction transaction)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _pageStore = pageStore ?? throw new ArgumentNullException(nameof(pageStore));
        _itemStore = itemStore ?? throw new ArgumentNullException(nameof(itemStore));
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _changeSetScope = changeSetScope ?? throw new ArgumentNullException(nameof(changeSetScope));
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
    }

    // ─── recording ────────────────────────────────────────────────────────────

    public Task<PageSetSnapshot?> CaptureAsync(Guid pageNodeId, CancellationToken ct = default)
        => _recorder.RecordPublishedAsync(pageNodeId, "Published", ct);

    public Task<PageSetSnapshot?> RecordDraftAsync(Guid pageNodeId, string note, CancellationToken ct = default)
        => _recorder.RecordDraftAsync(pageNodeId, note, ct);

    // ─── reads ────────────────────────────────────────────────────────────────

    public Task<List<PageSetSnapshot>> ListAsync(Guid pageNodeId, CancellationToken ct = default)
        => Newest(_context.Set<PageSetSnapshot>()
                .AsNoTracking()
                .Include(s => s.Items)
                .Where(s => s.PageNodeId == pageNodeId))
            .ToListAsync(ct);

    public async Task<(List<PageSetSnapshot> Entries, int TotalCount)> ListPageAsync(
        Guid pageNodeId, int skip, int take, CancellationToken ct = default)
    {
        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 1, IPageHistoryService.MaxPageSize);

        var snapshots = _context.Set<PageSetSnapshot>().AsNoTracking().Where(s => s.PageNodeId == pageNodeId);
        var total = await snapshots.CountAsync(ct);
        var entries = await Newest(snapshots.Include(s => s.Items))
            .Skip(skip)
            .Take(take + 1)
            .ToListAsync(ct);
        return (entries, total);
    }

    public Task<PageSetSnapshot?> GetAsync(Guid snapshotId, CancellationToken ct = default)
        => _context.Set<PageSetSnapshot>()
            .AsNoTracking()
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == snapshotId, ct);

    public async Task<Guid?> GetLatestIdAsync(Guid pageNodeId, CancellationToken ct = default)
        => await Newest(_context.Set<PageSetSnapshot>().Where(s => s.PageNodeId == pageNodeId))
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(ct);

    public async Task<PageSetSnapshot?> GetPreviousAsync(PageSetSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Entries sharing the snapshot's timestamp are ordered by id exactly as ListAsync orders them (in the
        // database, whose uuid order need not match Guid.CompareTo), so the neighbour may be one of them.
        var tied = await Newest(_context.Set<PageSetSnapshot>()
                .Where(s => s.PageNodeId == snapshot.PageNodeId && s.CreatedUtc == snapshot.CreatedUtc))
            .Select(s => s.Id)
            .ToListAsync(ct);
        var next = tied.IndexOf(snapshot.Id) + 1;
        if (next > 0 && next < tied.Count)
            return await GetAsync(tied[next], ct);

        return await Newest(_context.Set<PageSetSnapshot>()
                .AsNoTracking()
                .Include(s => s.Items)
                .Where(s => s.PageNodeId == snapshot.PageNodeId && s.CreatedUtc < snapshot.CreatedUtc))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Newest first: the order <see cref="ListAsync"/> and the neighbour lookups share.</summary>
    private static IQueryable<PageSetSnapshot> Newest(IQueryable<PageSetSnapshot> snapshots)
        => snapshots.OrderByDescending(s => s.CreatedUtc).ThenByDescending(s => s.Id);

    public async Task<PageSetState?> GetSnapshotStateAsync(Guid snapshotId, CancellationToken ct = default)
    {
        var snapshot = await GetAsync(snapshotId, ct);
        return snapshot == null ? null : await GetStateAsync(snapshot, ct);
    }

    /// <summary>The page and item versions a snapshot captured, or null when they no longer exist.</summary>
    private async Task<PageSetState?> GetStateAsync(PageSetSnapshot snapshot, CancellationToken ct)
    {
        // The Restrict FKs guarantee every referenced version exists while the snapshot does; a null here
        // would be a broken invariant — report the state as missing rather than crash the caller.
        var page = await _pageStore.GetVersionAsync(snapshot.PageVersionId, ct);
        if (page == null)
            return null;

        var versionIds = snapshot.Items.Select(i => i.ItemVersionId).ToList();
        var items = await _context.Set<ContentZoneItemDTO>()
            .AsNoTracking()
            .Where(i => versionIds.Contains(i.VersionId))
            .ToListAsync(ct);
        return new PageSetState(page, items);
    }

    public async Task<PageSetState?> GetCurrentStateAsync(Guid pageNodeId, CancellationToken ct = default)
    {
        var page = await _pageStore.GetCurrentDraftAsync(pageNodeId, ct);
        if (page == null)
            return null;

        var zoneIds = await _tree.GetZoneTreeNodeIdsAsync(pageNodeId, ct);
        var items = await _tree.CurrentDraftItems()
            .Where(ZoneRelations.InZones(zoneIds))
            .ToListAsync(ct);
        return new PageSetState(page, items);
    }

    public async Task<string?> GetDraftStateTokenAsync(Guid pageNodeId, CancellationToken ct = default)
    {
        var state = await GetCurrentStateAsync(pageNodeId, ct);
        return state == null ? null : PageSetEquality.StateToken(state.Page, state.Items);
    }

    public async Task<List<PageSetSnapshot>> GetSnapshotsReferencingVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        var viaItems = _context.Set<PageSetSnapshotItem>()
            .Where(i => i.ItemVersionId == versionId)
            .Select(i => i.SnapshotId);

        return await _context.Set<PageSetSnapshot>()
            .AsNoTracking()
            .Where(s => s.PageVersionId == versionId || viaItems.Contains(s.Id))
            .OrderByDescending(s => s.CreatedUtc)
            .ThenByDescending(s => s.Id)
            .ToListAsync(ct);
    }

    public async Task<ContentReadSnapshot?> GetReadSnapshotAsync(Guid snapshotId, CancellationToken ct = default)
    {
        var snapshot = await GetAsync(snapshotId, ct);
        if (snapshot == null)
            return null;

        // Every item that has ever sat in the page's tree is governed, so one added after the snapshot
        // (not captured) reads nothing instead of its published version.
        var zoneIds = await _tree.GetZoneTreeNodeIdsAsync(snapshot.PageNodeId, ct);
        var treeItemNodeIds = await _context.Set<ContentZoneItemDTO>()
            .AsNoTracking()
            .Where(ZoneRelations.InZones(zoneIds))
            .Select(i => i.Version.NodeId)
            .Distinct()
            .ToListAsync(ct);

        var governed = treeItemNodeIds
            .Concat(snapshot.Items.Select(i => i.ItemNodeId))
            .Append(snapshot.PageNodeId)
            .Distinct()
            .ToArray();
        var versions = snapshot.Items.Select(i => i.ItemVersionId)
            .Append(snapshot.PageVersionId)
            .ToArray();
        return new ContentReadSnapshot(governed, versions);
    }

    public async Task<HashSet<Guid>> GetPagesWithUnpublishedChangesAsync(IEnumerable<Guid> pageNodeIds, CancellationToken ct = default)
    {
        var ids = pageNodeIds.ToList();
        if (ids.Count == 0)
            return [];

        var pending = await _tree.GetPagesWithPendingZoneChangesAsync(ids, ct);

        var pages = await _tree.Pages()
            .Where(p => ids.Contains(p.Version.NodeId)
                     && (p.Version.IsCurrentDraft || p.Version.State == ContentVersionState.Published))
            .ToListAsync(ct);
        var published = pages
            .Where(p => p.Version.State == ContentVersionState.Published)
            .ToDictionary(p => p.Version.NodeId);

        // A page draft that is not live, or that differs from the live page by content, is a change. (A
        // draft equal to the live page — left by a discard or a revert — is not.)
        foreach (var draft in pages.Where(p => p.Version.IsCurrentDraft))
        {
            if (!published.TryGetValue(draft.Version.NodeId, out var live) || !PageSetEquality.PagesEqual(live, draft))
                pending.Add(draft.Version.NodeId);
        }

        return pending;
    }

    // ─── writes ───────────────────────────────────────────────────────────────

    public async Task<bool> DeleteAsync(Guid snapshotId, CancellationToken ct = default)
    {
        var snapshot = await _context.Set<PageSetSnapshot>()
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == snapshotId, ct);
        if (snapshot == null)
            return false;

        _context.Set<PageSetSnapshot>().Remove(snapshot);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<(bool Success, string? ErrorMessage)> RevertAsync(
        Guid snapshotId, string expectedStateToken, RestorableMetadata restorableMetadata, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(restorableMetadata);

        var snapshot = await GetAsync(snapshotId, ct);
        if (snapshot == null)
            return (false, IPageHistoryService.NotFoundMessage);

        var current = await GetCurrentStateAsync(snapshot.PageNodeId, ct);
        if (current == null)
            return (false, IPageHistoryService.NotFoundMessage);

        if (PageSetEquality.StateToken(current.Page, current.Items) != expectedStateToken)
            return (false, IPageHistoryService.StaleStateMessage);

        var target = await GetStateAsync(snapshot, ct);
        if (target == null)
            return (false, IPageHistoryService.NotFoundMessage);

        return await ApplyAndRecordAsync(current, target.Page, target.Items, restorableMetadata,
            ChangeSetKind.Restore, $"Reverted to page history from {snapshot.CreatedUtc:yyyy-MM-dd HH:mm} UTC", ct);
    }

    public async Task<(bool Success, string? ErrorMessage)> DiscardUnpublishedChangesAsync(Guid pageNodeId, CancellationToken ct = default)
    {
        var live = await _tree.Pages()
            .FirstOrDefaultAsync(p => p.Version.NodeId == pageNodeId && p.Version.State == ContentVersionState.Published, ct);
        if (live == null)
            return (false, IPageHistoryService.NotPublishedMessage);

        // A published page always has a current draft (the published row itself, or a newer draft); a null
        // would be a broken invariant — refuse rather than crash.
        var current = await GetCurrentStateAsync(pageNodeId, ct);
        if (current == null)
            return (false, IPageHistoryService.NotFoundMessage);

        var liveItems = await _tree.GetLiveTreeItemsAsync(pageNodeId, ct);

        // What went live already passed metadata validation, so it is restored verbatim.
        return await ApplyAndRecordAsync(current, live, liveItems, (_, historical, _) => historical,
            ChangeSetKind.Discard, "Discarded unpublished changes", ct);
    }

    /// <summary>
    /// Writes <paramref name="targetPage"/> and <paramref name="targetItems"/> over the current drafts as new
    /// versions, in one transaction under one change set noted <paramref name="note"/>: the page and each
    /// target item get the target content and placement (equal ones are skipped), and every other item in
    /// the tree becomes an inactive draft. On success, records the result as a Draft history entry.
    /// </summary>
    private async Task<(bool Success, string? ErrorMessage)> ApplyAndRecordAsync(
        PageSetState current, PageDTO targetPage, List<ContentZoneItemDTO> targetItems,
        RestorableMetadata restorableMetadata, ChangeSetKind kind, string note, CancellationToken ct)
    {
        var pageNodeId = current.Page.Version.NodeId;
        var currentByNode = current.Items.ToDictionary(i => i.Version.NodeId);
        var treeZoneIds = (await _tree.GetZoneTreeNodeIdsAsync(pageNodeId, ct)).ToHashSet();

        // Validate before writing anything: every target widget must still exist in this page's tree.
        foreach (var target in targetItems)
        {
            if (!currentByNode.ContainsKey(target.Version.NodeId) || !treeZoneIds.Contains(target.ContentZoneNodeId))
                return (false, "A zone or widget in this history entry no longer exists on the page; nothing was reverted.");
        }

        string? error = null;

        using var changeSet = _changeSetScope.Begin(kind, pageNodeId, note);
        using var changeNote = _changeSetScope.UseChangeNote(note);
        await _transaction.RunAsync(async token =>
        {
            error = await ApplyPageAsync(current.Page, targetPage, token)
                 ?? await ApplyItemsAsync(current.Items, targetItems, currentByNode, restorableMetadata, token);
            return error == null;
        }, ct);

        if (error != null)
            return (false, error);

        await _recorder.RecordDraftAsync(pageNodeId, note, ct);
        return (true, null);
    }

    private async Task<string?> ApplyPageAsync(PageDTO current, PageDTO target, CancellationToken ct)
    {
        if (PageSetEquality.PagesEqual(current, target))
            return null;

        var reverted = current with
        {
            ControllerName = target.ControllerName,
            ViewName = target.ViewName,
            ConfigurationJson = target.ConfigurationJson,
            Version = current.Version with
            {
                Title = target.Version.Title,
                Slug = target.Version.Slug,
                PublishStartUtc = target.Version.PublishStartUtc,
                PublishEndUtc = target.Version.PublishEndUtc,
                CustomFields = target.Version.CustomFields.Select(c => c with { }).ToList()
            }
        };
        reverted.Version.ReplaceAllRelationsFrom(target.Version.Relations);

        var save = await _pageStore.SaveDraftAsync(reverted, current.Version.VersionNumber, ct);
        return save.Success ? null : save.ErrorMessage;
    }

    private async Task<string?> ApplyItemsAsync(
        List<ContentZoneItemDTO> currentItems,
        List<ContentZoneItemDTO> targetItems,
        Dictionary<Guid, ContentZoneItemDTO> currentByNode,
        RestorableMetadata restorableMetadata,
        CancellationToken ct)
    {
        var writes = new List<(ContentZoneItemDTO Current, ContentZoneItemDTO Target)>();

        foreach (var target in targetItems)
        {
            var existing = currentByNode[target.Version.NodeId];
            var reverted = existing with
            {
                ComponentName = target.ComponentName,
                ComponentPropertiesJson = target.ComponentPropertiesJson,
                MetadataJson = restorableMetadata(target.ComponentName, target.MetadataJson, existing.MetadataJson) ?? "{}",
                IsActive = true,
                Version = existing.Version with { }
            };
            // The target's relations include its placement (Zone + ordinal), so this also moves it back.
            reverted.Version.ReplaceAllRelationsFrom(target.Version.Relations);
            writes.Add((existing, reverted));
        }

        // Everything else in the tree is taken off the page as an inactive draft (never deleted). One that
        // would share an ordinal with a restored widget moves to the end of its zone, so a later restore of
        // it never collides.
        var targetNodeIds = targetItems.Select(i => i.Version.NodeId).ToHashSet();
        var takenOrdinals = targetItems.Select(i => (i.ContentZoneNodeId, i.Ordinal)).ToHashSet();
        var lastOrdinal = currentItems.Concat(targetItems)
            .GroupBy(i => i.ContentZoneNodeId)
            .ToDictionary(g => g.Key, g => g.Max(i => i.Ordinal));
        foreach (var extra in currentItems
                     .Where(i => !targetNodeIds.Contains(i.Version.NodeId))
                     .OrderBy(i => i.ContentZoneNodeId)
                     .ThenBy(i => i.Ordinal))
        {
            var ordinal = takenOrdinals.Contains((extra.ContentZoneNodeId, extra.Ordinal))
                ? lastOrdinal[extra.ContentZoneNodeId] += 1
                : extra.Ordinal;
            writes.Add((extra, ZoneRelations.Placed(extra with { IsActive = false }, extra.ContentZoneNodeId, ordinal)));
        }

        foreach (var (existing, reverted) in writes)
        {
            if (PageSetEquality.ItemsEqual(existing, reverted))
                continue;

            var save = await _itemStore.SaveDraftAsync(reverted, existing.Version.VersionNumber, ct);
            if (!save.Success)
                return save.ErrorMessage;
        }

        return null;
    }
}
