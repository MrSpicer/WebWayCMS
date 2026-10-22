using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// Manages content zones: item resolution, assignment (slot) management, and item writes.
/// Zone and item versioning is delegated to the generic <see cref="IContentStore{T}"/>. Items in a
/// zone that belongs to a page (directly or through nested zones) are written as drafts and go live
/// only when the page is published (<see cref="PublishPageZonesAsync"/>); items in global/name-based
/// zones have no page to publish them, so they auto-publish on write.
/// </summary>
public sealed class ContentZoneService : IContentZoneService
{
    private readonly Serilog.ILogger _logger = Serilog.Log.ForContext<ContentZoneService>();

    private readonly CmsDbContext _context;
    private readonly IContentStore<ContentZoneDTO> _zoneStore;
    private readonly IContentStore<ContentZoneItemDTO> _itemStore;
    private readonly IContentReadContext _readContext;
    private readonly IChangeSetScope _changeSetScope;
    private readonly ICMSRouteService _routeService;

    public ContentZoneService(
        CmsDbContext context,
        IContentStore<ContentZoneDTO> zoneStore,
        IContentStore<ContentZoneItemDTO> itemStore,
        IContentReadContext readContext,
        IChangeSetScope changeSetScope,
        ICMSRouteService routeService)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _zoneStore = zoneStore ?? throw new ArgumentNullException(nameof(zoneStore));
        _itemStore = itemStore ?? throw new ArgumentNullException(nameof(itemStore));
        _readContext = readContext ?? throw new ArgumentNullException(nameof(readContext));
        _changeSetScope = changeSetScope ?? throw new ArgumentNullException(nameof(changeSetScope));
        _routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
    }

    // ─── item resolution ──────────────────────────────────────────────────────

    public async Task<List<ContentZoneItemDTO>> GetItemsAsync(Guid zoneNodeId, CancellationToken ct = default)
    {
        return await _context.Set<ContentZoneItemDTO>()
            .AsNoTracking()
            .AtReadContext(_readContext)
            .Where(i => i.ContentZoneNodeId == zoneNodeId && i.IsActive)
            .OrderBy(i => i.Ordinal)
            .ToListAsync(ct);
    }

    // ─── zone reads ───────────────────────────────────────────────────────────

    public Task<ContentZoneDTO?> GetZoneByNodeAsync(Guid zoneNodeId, CancellationToken ct = default)
        => _zoneStore.GetAsync(zoneNodeId, ct);

    public async Task<ContentZoneDTO?> GetZoneByNameAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return await _context.Set<ContentZoneDTO>()
            .AsNoTracking()
            .AtReadContext(_readContext)
            .FirstOrDefaultAsync(z => z.Name == name, ct);
    }

    // ─── assignment management ────────────────────────────────────────────────

    public async Task<ContentZoneAssignmentDTO?> GetByPageSlotAsync(Guid pageNodeId, string slotName, CancellationToken ct = default)
    {
        return await _context.Set<ContentZoneAssignmentDTO>()
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ParentPageNodeId == pageNodeId && a.SlotName == slotName, ct);
    }

    public async Task<(ContentZoneDTO Zone, ContentZoneAssignmentDTO Assignment)> GetOrCreateByPageSlotAsync(Guid pageNodeId, string slotName, CancellationToken ct = default)
    {
        var assignment = await GetByPageSlotAsync(pageNodeId, slotName, ct);
        if (assignment != null)
        {
            var zone = await ResolveZoneAsync(assignment.ContentZoneNodeId, ct);
            if (zone != null)
                return (zone, assignment);
        }

        return await ResolveOrCreateAssignmentAsync(pageNodeId, null, slotName, assignment, ct);
    }

    public async Task<ContentZoneAssignmentDTO?> GetByItemSlotAsync(Guid parentItemNodeId, string slotName, CancellationToken ct = default)
    {
        return await _context.Set<ContentZoneAssignmentDTO>()
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ParentItemNodeId == parentItemNodeId && a.SlotName == slotName, ct);
    }

    public async Task<(ContentZoneDTO Zone, ContentZoneAssignmentDTO Assignment)> GetOrCreateByItemSlotAsync(Guid parentItemNodeId, string slotName, CancellationToken ct = default)
    {
        var assignment = await GetByItemSlotAsync(parentItemNodeId, slotName, ct);
        if (assignment != null)
        {
            var zone = await ResolveZoneAsync(assignment.ContentZoneNodeId, ct);
            if (zone != null)
                return (zone, assignment);
        }

        return await ResolveOrCreateAssignmentAsync(null, parentItemNodeId, slotName, assignment, ct);
    }

    public async Task<ContentZoneDTO> GetOrCreateByNameAsync(string name, CancellationToken ct = default)
    {
        var zone = await GetZoneByNameIncludingDraftsAsync(name, ct);
        if (zone != null)
            return zone;

        using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            zone = await GetZoneByNameIncludingDraftsAsync(name, ct);
            if (zone != null)
            {
                await transaction.RollbackAsync(ct);
                return zone;
            }

            zone = await CreatePublishedZoneAsync(name, ct);
            await transaction.CommitAsync(ct);
            return zone;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<IEnumerable<ContentZoneAssignmentDTO>> GetAllAssignmentsForPageAsync(Guid pageNodeId, CancellationToken ct = default)
    {
        return await _context.Set<ContentZoneAssignmentDTO>()
            .AsNoTracking()
            .Where(a => a.ParentPageNodeId == pageNodeId)
            .ToListAsync(ct);
    }

    public async Task<List<ContentZoneDTO>> GetAllByPageAsync(Guid pageNodeId, CancellationToken ct = default)
    {
        var assignments = await _context.Set<ContentZoneAssignmentDTO>()
            .AsNoTracking()
            .Where(a => a.ParentPageNodeId == pageNodeId)
            .ToListAsync(ct);

        var nodeIds = assignments.Select(a => a.ContentZoneNodeId).ToList();

        return await _context.Set<ContentZoneDTO>()
            .AsNoTracking()
            .AtReadContext(_readContext)
            .Where(z => nodeIds.Contains(z.Version.NodeId))
            .OrderBy(z => z.Name)
            .ToListAsync(ct);
    }

    public async Task<List<ContentZoneDTO>> GetAllByParentZoneAsync(Guid parentZoneNodeId, CancellationToken ct = default)
    {
        // A nested zone's parent zone is the zone its owning item currently sits in.
        var itemNodeIds = CurrentDraftItems()
            .Where(i => i.ContentZoneNodeId == parentZoneNodeId)
            .Select(i => i.Version.NodeId);

        var nodeIds = await _context.Set<ContentZoneAssignmentDTO>()
            .AsNoTracking()
            .Where(a => a.ParentItemNodeId != null && itemNodeIds.Contains(a.ParentItemNodeId.Value))
            .Select(a => a.ContentZoneNodeId)
            .ToListAsync(ct);

        return await _context.Set<ContentZoneDTO>()
            .AsNoTracking()
            .AtReadContext(_readContext)
            .Where(z => nodeIds.Contains(z.Version.NodeId))
            .OrderBy(z => z.Name)
            .ToListAsync(ct);
    }

    public async Task<HashSet<Guid>> GetZoneNodeIdsWithChildrenAsync(IEnumerable<Guid> zoneNodeIds, CancellationToken ct = default)
    {
        var ids = zoneNodeIds.ToList();
        if (ids.Count == 0)
            return [];

        var ownerItemIds = _context.Set<ContentZoneAssignmentDTO>()
            .AsNoTracking()
            .Where(a => a.ParentItemNodeId != null)
            .Select(a => a.ParentItemNodeId!.Value);

        var parentIds = await CurrentDraftItems()
            .Where(i => ids.Contains(i.ContentZoneNodeId) && ownerItemIds.Contains(i.Version.NodeId))
            .Select(i => i.ContentZoneNodeId)
            .Distinct()
            .ToListAsync(ct);

        return [.. parentIds];
    }

    public async Task<Dictionary<Guid, int>> GetAssignmentCountsByNodeIdAsync(IEnumerable<Guid> nodeIds, CancellationToken ct = default)
    {
        var ids = nodeIds.ToList();
        if (ids.Count == 0) return [];

        return await _context.Set<ContentZoneAssignmentDTO>()
            .AsNoTracking()
            .Where(a => ids.Contains(a.ContentZoneNodeId))
            .GroupBy(a => a.ContentZoneNodeId)
            .Select(g => new { NodeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.NodeId, x => x.Count, ct);
    }

    // ─── parent resolution ────────────────────────────────────────────────────

    public async Task<Guid?> GetParentPageNodeForZoneAsync(Guid zoneNodeId, CancellationToken ct = default)
        => (await WalkUpAsync(zoneNodeId, ct)).PageNodeId;

    /// <summary>
    /// Walks a zone up to its page: zone → assignment → page, or → owning item → that item's zone, and
    /// so on. Returns the page (null for a global, orphaned, or cyclic zone) and every container item
    /// passed on the way.
    /// </summary>
    private async Task<(Guid? PageNodeId, HashSet<Guid> OwnerItemNodeIds)> WalkUpAsync(Guid zoneNodeId, CancellationToken ct)
    {
        var owners = new HashSet<Guid>();
        var visited = new HashSet<Guid>();
        Guid? currentZoneId = zoneNodeId;

        while (currentZoneId.HasValue)
        {
            if (!visited.Add(currentZoneId.Value))
                return (null, owners);

            var assignment = await _context.Set<ContentZoneAssignmentDTO>()
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.ContentZoneNodeId == currentZoneId.Value, ct);

            if (assignment == null)
                return (null, owners);

            if (assignment.ParentPageNodeId.HasValue)
                return (assignment.ParentPageNodeId.Value, owners);

            if (!assignment.ParentItemNodeId.HasValue)
                return (null, owners);

            owners.Add(assignment.ParentItemNodeId.Value);
            currentZoneId = await GetItemZoneAsync(assignment.ParentItemNodeId.Value, ct);
        }

        return (null, owners);
    }

    // ─── item writes ──────────────────────────────────────────────────────────

    public Task<ContentZoneItemDTO?> GetItemByNodeIdAsync(Guid itemNodeId, CancellationToken ct = default)
        => _itemStore.GetCurrentDraftAsync(itemNodeId, ct);

    public async Task<ContentZoneItemDTO> AddItemAsync(Guid zoneNodeId, ContentZoneItemDTO item, CancellationToken ct = default)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));

        var zone = await _zoneStore.GetCurrentDraftAsync(zoneNodeId, ct)
            ?? throw new InvalidOperationException($"Content zone with ID {zoneNodeId} not found.");

        item.ContentZoneNodeId = zoneNodeId;

        // New items always carry a metadata document, so the column never holds null on a write.
        if (string.IsNullOrWhiteSpace(item.MetadataJson))
            item.MetadataJson = "{}";

        if (item.Ordinal == 0)
        {
            var maxOrdinal = await _context.Set<ContentZoneItemDTO>()
                .Where(i => i.ContentZoneNodeId == zoneNodeId)
                .MaxAsync(i => (int?)i.Ordinal, ct) ?? 0;
            item.Ordinal = maxOrdinal + 1;
        }

        using var _ = _changeSetScope.Begin(ChangeSetKind.Save, zoneNodeId, null);

        var save = await _itemStore.SaveDraftAsync(item, null, ct);
        if (!save.Success)
            throw new InvalidOperationException(save.ErrorMessage ?? "Failed to add content zone item.");

        if (!await IsPageOwnedZoneAsync(zoneNodeId, ct))
            await _itemStore.PublishAsync(save.NodeId, ct);
        return item;
    }

    public async Task<bool> UpdateItemAsync(ContentZoneItemDTO item, CancellationToken ct = default, int? expectedVersionNumber = null)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));

        var nodeId = item.Version.Node?.Id ?? Guid.Empty;
        if (nodeId == Guid.Empty)
            return false;

        var existing = await _itemStore.GetCurrentDraftAsync(nodeId, ct);
        if (existing == null)
            return false;

        var updated = existing with
        {
            ComponentName = item.ComponentName,
            ComponentPropertiesJson = item.ComponentPropertiesJson,
            // Null metadata means "not supplied": a content-config save must never wipe the
            // per-item metadata edited through the zone editor's metadata dialog. A blank supplied
            // document still normalizes to "{}" so the column never holds null on a write.
            MetadataJson = string.IsNullOrWhiteSpace(item.MetadataJson) ? existing.MetadataJson ?? "{}" : item.MetadataJson,
            IsActive = item.IsActive
            // Ordinal and ContentZoneNodeId are preserved from the existing record.
        };

        using var _ = _changeSetScope.Begin(ChangeSetKind.Save, nodeId, null);

        var save = await _itemStore.SaveDraftAsync(updated, expectedVersionNumber, ct);
        if (!save.Success)
            return false;

        if (!await IsPageOwnedZoneAsync(existing.ContentZoneNodeId, ct))
            await _itemStore.PublishAsync(nodeId, ct);
        return true;
    }

    public async Task<bool> RemoveItemAsync(Guid itemNodeId, CancellationToken ct = default)
    {
        // A live item in a page's zone must stay live until the page is published, so its removal is
        // recorded as an inactive draft. Anything else (never published, or a global zone) goes now.
        var current = await _itemStore.GetCurrentDraftAsync(itemNodeId, ct);
        if (current != null
            && await IsPageOwnedZoneAsync(current.ContentZoneNodeId, ct)
            && await HasPublishedVersionAsync(itemNodeId, ct))
        {
            using var _ = _changeSetScope.Begin(ChangeSetKind.Save, itemNodeId, null);
            // Expected = the version just read, so a concurrent edit between the read and this write
            // fails instead of being overwritten by the inactive copy.
            var save = await _itemStore.SaveDraftAsync(current with { IsActive = false }, current.Version.VersionNumber, ct);
            return save.Success;
        }

        // A hard-deleted container takes its nested zones (and their items) with it.
        await DeleteItemOwnedZonesAsync(itemNodeId, ct);
        return await _itemStore.DeleteAsync(itemNodeId, softDelete: false, ct);
    }

    public async Task<bool> ReorderItemsAsync(Guid zoneNodeId, List<Guid> itemNodeIdsInOrder, CancellationToken ct = default)
    {
        // Resolve and validate every id up front so a bad id never causes a partial reorder. A reorder
        // that writes some ordinals and then reports failure is worse than a rejected one.
        var resolved = new List<ContentZoneItemDTO>(itemNodeIdsInOrder.Count);
        foreach (var itemNodeId in itemNodeIdsInOrder)
        {
            var current = await _itemStore.GetCurrentDraftAsync(itemNodeId, ct);
            if (current == null || current.ContentZoneNodeId != zoneNodeId)
                return false;

            resolved.Add(current);
        }

        var publish = !await IsPageOwnedZoneAsync(zoneNodeId, ct);

        using var _ = _changeSetScope.Begin(ChangeSetKind.Save, zoneNodeId, null);

        // Each save is version-checked, so a concurrent edit can still fail the loop partway: the
        // transaction keeps that from leaving half-rewritten (duplicate) ordinals behind.
        var result = await RunAtomicallyAsync(
            () => SaveZoneOrderAsync(zoneNodeId, resolved, publish, skipUnchanged: false, ct), ct);
        return result.Success;
    }

    public async Task<(bool Success, string? ErrorMessage)> MoveItemAsync(
        Guid itemNodeId, Guid targetZoneNodeId, int position, CancellationToken ct = default, int? expectedVersionNumber = null)
    {
        var item = await _itemStore.GetCurrentDraftAsync(itemNodeId, ct);
        if (item == null || !item.IsActive)
            return (false, IContentZoneService.ItemNotFoundMessage);

        // The caller's view of the widget: reject the move when it was edited since the page loaded,
        // so a drop can never silently overwrite a concurrent edit.
        if (expectedVersionNumber.HasValue && item.Version.VersionNumber != expectedVersionNumber.Value)
            return (false, ContentStore<ContentZoneItemDTO>.StaleVersionMessage);

        if (await _zoneStore.GetCurrentDraftAsync(targetZoneNodeId, ct) == null)
            return (false, "Target zone not found.");

        var sourceZoneNodeId = item.ContentZoneNodeId;
        var (targetPageNodeId, targetOwners) = await WalkUpAsync(targetZoneNodeId, ct);
        if (sourceZoneNodeId != targetZoneNodeId)
        {
            // Moving between zones is only meaningful inside one page's draft: global zones auto-publish
            // and another page publishes on its own schedule, so neither can hold a page's pending move.
            var sourcePageNodeId = await GetParentPageNodeForZoneAsync(sourceZoneNodeId, ct);
            if (!targetPageNodeId.HasValue || sourcePageNodeId != targetPageNodeId)
                return (false, "Widgets can only be moved between zones of the same page.");

            // A container can't go into one of its own nested zones (at any depth).
            if (targetOwners.Contains(itemNodeId))
                return (false, "A widget cannot be moved into itself.");
        }

        // The target's items with the moved one inserted. Removed (inactive) items are renumbered too, in
        // place, so they never share an ordinal with a live item if their removal is later discarded.
        // The position counts active items only, since that is the order the editor shows.
        var siblings = await CurrentDraftItems()
            .Where(i => i.ContentZoneNodeId == targetZoneNodeId && i.Version.NodeId != itemNodeId)
            .OrderBy(i => i.Ordinal)
            .ToListAsync(ct);
        var active = siblings.Where(i => i.IsActive).ToList();
        var activeIndex = Math.Clamp(position, 0, active.Count);
        var insertAt = activeIndex < active.Count
            ? siblings.FindIndex(i => i.VersionId == active[activeIndex].VersionId)
            : siblings.Count;
        // Inserted with its stored zone, so a cross-zone move is never skipped as "unchanged".
        siblings.Insert(insertAt, item);

        using var _ = _changeSetScope.Begin(ChangeSetKind.Save, targetZoneNodeId, null);

        // Both zones' renumbering commits together or not at all, so a stale sibling in the source
        // zone can never leave the widget moved while the API reports the move as failed.
        return await RunAtomicallyAsync(async () =>
        {
            var target = await SaveZoneOrderAsync(targetZoneNodeId, siblings, publish: !targetPageNodeId.HasValue, skipUnchanged: true, ct);
            if (!target.Success)
                return target;

            // A cross-zone move leaves an ordinal gap in the source zone: renumber what remains, in
            // place. The moved item's draft already sits in the target zone, so the query finds only
            // what stayed behind. Both zones belong to the same page (validated above), so there is
            // nothing to publish here.
            if (sourceZoneNodeId == targetZoneNodeId)
                return target;

            var remaining = await CurrentDraftItems()
                .Where(i => i.ContentZoneNodeId == sourceZoneNodeId)
                .OrderBy(i => i.Ordinal)
                .ToListAsync(ct);
            return await SaveZoneOrderAsync(sourceZoneNodeId, remaining, publish: false, skipUnchanged: true, ct);
        }, ct);
    }

    /// <summary>
    /// Runs a multi-save write in one DB transaction, committing only when it succeeds. On failure the
    /// rollback restores the rows, and the change tracker is cleared too: the earlier saves left it
    /// holding values the database no longer has, which the scoped context would otherwise serve back
    /// (identity resolution) or replay on its next SaveChanges.
    /// </summary>
    private async Task<(bool Success, string? ErrorMessage)> RunAtomicallyAsync(
        Func<Task<(bool Success, string? ErrorMessage)>> write, CancellationToken ct)
    {
        using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            var result = await write();
            if (result.Success)
            {
                await transaction.CommitAsync(ct);
                return result;
            }

            await transaction.RollbackAsync(ct);
            _context.ChangeTracker.Clear();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            _context.ChangeTracker.Clear();
            throw;
        }
    }

    /// <summary>
    /// Writes ordinals 1..n for <paramref name="itemsInOrder"/>, all placed in <paramref name="zoneNodeId"/>
    /// (which is how a moved item changes zone). With <paramref name="skipUnchanged"/>, an item already
    /// in the zone at its ordinal is left alone so a move mints no needless drafts of its siblings.
    /// Every save carries the version the item was read with, so a concurrent edit to a sibling fails
    /// the whole reorder instead of being overwritten by a stale copy (reported with the store's
    /// "changed by someone else" message). Earlier saves are not undone here: the caller owns the
    /// change-set scope and runs this inside <see cref="RunAtomicallyAsync"/>.
    /// </summary>
    private async Task<(bool Success, string? ErrorMessage)> SaveZoneOrderAsync(
        Guid zoneNodeId, List<ContentZoneItemDTO> itemsInOrder, bool publish, bool skipUnchanged, CancellationToken ct)
    {
        for (int i = 0; i < itemsInOrder.Count; i++)
        {
            var current = itemsInOrder[i];
            if (skipUnchanged && current.ContentZoneNodeId == zoneNodeId && current.Ordinal == i + 1)
                continue;

            var save = await _itemStore.SaveDraftAsync(
                current with { ContentZoneNodeId = zoneNodeId, Ordinal = i + 1 },
                current.Version.VersionNumber, ct);
            if (!save.Success)
                return (false, save.ErrorMessage == ContentStore<ContentZoneItemDTO>.StaleVersionMessage
                    ? save.ErrorMessage
                    : "Failed to move the widget.");

            if (publish)
                await _itemStore.PublishAsync(current.Version.NodeId, ct);
        }

        return (true, null);
    }

    // ─── page draft lifecycle ─────────────────────────────────────────────────

    public async Task<List<Guid>> GetZoneTreeNodeIdsForPageAsync(Guid pageNodeId, CancellationToken ct = default)
        => (await WalkZoneTreesAsync([pageNodeId], ct)).Select(x => x.ZoneNodeId).Distinct().ToList();

    public async Task<List<Guid>> GetPendingItemNodeIdsAsync(IEnumerable<Guid> zoneNodeIds, CancellationToken ct = default)
    {
        var ids = zoneNodeIds.ToList();
        if (ids.Count == 0)
            return [];

        return await PendingItems()
            .Where(i => ids.Contains(i.ContentZoneNodeId))
            .Select(i => i.Version.NodeId)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task<HashSet<Guid>> GetPagesWithPendingZoneChangesAsync(IEnumerable<Guid> pageNodeIds, CancellationToken ct = default)
    {
        var pageIds = pageNodeIds.ToList();
        if (pageIds.Count == 0)
            return [];

        var pendingZoneIds = await PendingItems()
            .Select(i => i.ContentZoneNodeId)
            .Distinct()
            .ToListAsync(ct);
        if (pendingZoneIds.Count == 0)
            return [];

        var pending = pendingZoneIds.ToHashSet();
        return (await WalkZoneTreesAsync(pageIds, ct))
            .Where(x => pending.Contains(x.ZoneNodeId))
            .Select(x => x.PageNodeId)
            .ToHashSet();
    }

    public async Task<PageZonePublishResult> PublishPageZonesAsync(Guid pageNodeId, CancellationToken ct = default)
    {
        var pendingIds = await GetPendingItemNodeIdsAsync(await GetZoneTreeNodeIdsForPageAsync(pageNodeId, ct), ct);

        var published = new List<ContentZoneItemDTO>();
        var errors = new List<string>();
        foreach (var itemNodeId in pendingIds)
        {
            var result = await _itemStore.PublishAsync(itemNodeId, ct);
            var item = result.Success ? await _itemStore.GetCurrentDraftAsync(itemNodeId, ct) : null;
            if (item == null)
            {
                errors.Add(result.ErrorMessage ?? $"Content zone item {itemNodeId} could not be published.");
                continue;
            }

            published.Add(item);
        }

        return new PageZonePublishResult(published, errors);
    }

    public async Task<(bool Success, string? ErrorMessage)> DiscardPageZoneDraftsAsync(Guid pageNodeId, CancellationToken ct = default)
    {
        var pendingIds = await GetPendingItemNodeIdsAsync(await GetZoneTreeNodeIdsForPageAsync(pageNodeId, ct), ct);

        // Once-published items first: they return to their published zone, which moves any live widget
        // dragged into a never-published container back out before that container is deleted below.
        var publishedIds = await _context.Set<ContentVersion>()
            .AsNoTracking()
            .Where(v => pendingIds.Contains(v.NodeId) && v.State == ContentVersionState.Published)
            .Select(v => v.NodeId)
            .Distinct()
            .ToListAsync(ct);
        var ordered = pendingIds.OrderBy(id => publishedIds.Contains(id) ? 0 : 1).ToList();

        using var _ = _changeSetScope.Begin(ChangeSetKind.Discard, pageNodeId, null);

        var errors = new List<string>();
        foreach (var itemNodeId in ordered)
        {
            // Already gone: it sat in a nested zone of a container deleted earlier in this loop.
            if (!await _context.Set<ContentNode>().AnyAsync(n => n.Id == itemNodeId, ct))
                continue;

            // The discard deletes the item outright only when its draft is its sole version (it falls
            // back to any earlier version otherwise). Only then does a container take its nested zones
            // with it, and they must go first because the assignment FK won't let the item go before them.
            if (await IsSoleVersionAsync(itemNodeId, ct))
                await DeleteItemOwnedZonesAsync(itemNodeId, ct);

            var result = await _itemStore.DiscardDraftAsync(itemNodeId, ct);
            if (!result.Success)
            {
                errors.Add(result.ErrorMessage ?? $"Content zone item {itemNodeId} could not be discarded.");
                continue;
            }

            // Sweep any route rows a deleted item may own so the route table never points at a
            // deleted node.
            if (result.VersionId == Guid.Empty)
                await _routeService.DeleteByOwningContentAsync(itemNodeId, ct);
        }

        return errors.Count == 0 ? (true, null) : (false, string.Join(" ", errors));
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

    private async Task<bool> IsPageOwnedZoneAsync(Guid zoneNodeId, CancellationToken ct)
        => (await GetParentPageNodeForZoneAsync(zoneNodeId, ct)).HasValue;

    private Task<bool> HasPublishedVersionAsync(Guid nodeId, CancellationToken ct)
        => _context.Set<ContentVersion>()
            .AnyAsync(v => v.NodeId == nodeId && v.State == ContentVersionState.Published, ct);

    /// <summary>
    /// True when the node has no default-variant version besides its current draft, i.e. when
    /// <see cref="IContentStore{T}.DiscardDraftAsync"/> would hard-delete it.
    /// </summary>
    private async Task<bool> IsSoleVersionAsync(Guid nodeId, CancellationToken ct)
        => !await _context.Set<ContentVersion>()
            .AnyAsync(v => v.NodeId == nodeId
                        && v.Culture == string.Empty
                        && v.Segment == string.Empty
                        && !v.IsCurrentDraft, ct);

    /// <summary>Items whose current draft differs from what is live (never published, or edited since).</summary>
    private IQueryable<ContentZoneItemDTO> PendingItems()
        => _context.Set<ContentZoneItemDTO>()
            .AsNoTracking()
            .Where(i => i.Version.IsCurrentDraft
                     && i.Version.Culture == string.Empty
                     && i.Version.Segment == string.Empty
                     && i.Version.State != ContentVersionState.Published
                     && !i.Version.Node.IsDeleted);

    /// <summary>Items whose current draft is live in the default variant (not deleted).</summary>
    private IQueryable<ContentZoneItemDTO> CurrentDraftItems()
        => _context.Set<ContentZoneItemDTO>()
            .AsNoTracking()
            .Where(i => i.Version.IsCurrentDraft
                     && i.Version.Culture == string.Empty
                     && i.Version.Segment == string.Empty
                     && !i.Version.Node.IsDeleted);

    /// <summary>
    /// The zone an item's current draft sits in, or null when the item no longer exists. Resolving
    /// from the draft (rather than the read-context-appropriate version) is safe for every caller:
    /// <see cref="MoveItemAsync"/> forbids cross-page moves, so an item's draft and published
    /// placements always resolve to the same owning page.
    /// </summary>
    private async Task<Guid?> GetItemZoneAsync(Guid itemNodeId, CancellationToken ct)
        => await CurrentDraftItems()
            .Where(i => i.Version.NodeId == itemNodeId)
            .Select(i => (Guid?)i.ContentZoneNodeId)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Every zone under each of the given pages, as (zone, page) pairs in breadth-first order: the
    /// pages' directly assigned zones, then the zones nested in their items, level by level. Only the
    /// current frontier is queried, so the cost follows the size of these pages' trees, not the whole
    /// zone table. Items are matched on any version, so a moved item's draft and published zones both
    /// count. The visited set guards against cycles.
    /// </summary>
    private async Task<List<(Guid ZoneNodeId, Guid PageNodeId)>> WalkZoneTreesAsync(
        IReadOnlyCollection<Guid> pageNodeIds, CancellationToken ct)
    {
        var result = new List<(Guid ZoneNodeId, Guid PageNodeId)>();
        var visited = new HashSet<(Guid, Guid)>();

        var frontier = (await _context.Set<ContentZoneAssignmentDTO>()
                .AsNoTracking()
                .Where(a => a.ParentPageNodeId != null && pageNodeIds.Contains(a.ParentPageNodeId.Value))
                .Select(a => new { a.ContentZoneNodeId, PageNodeId = a.ParentPageNodeId!.Value })
                .ToListAsync(ct))
            .Select(x => (x.ContentZoneNodeId, x.PageNodeId))
            .ToList();

        while (frontier.Count > 0)
        {
            var added = frontier.Where(visited.Add).ToList();
            result.AddRange(added);

            var zoneIds = added.Select(x => x.ContentZoneNodeId).Distinct().ToList();
            var itemZones = await _context.Set<ContentZoneItemDTO>()
                .AsNoTracking()
                .Where(i => zoneIds.Contains(i.ContentZoneNodeId))
                .Select(i => new { i.Version.NodeId, i.ContentZoneNodeId })
                .Distinct()
                .ToListAsync(ct);
            var itemIds = itemZones.Select(x => x.NodeId).Distinct().ToList();
            if (itemIds.Count == 0)
                break;

            var nested = await _context.Set<ContentZoneAssignmentDTO>()
                .AsNoTracking()
                .Where(a => a.ParentItemNodeId != null && itemIds.Contains(a.ParentItemNodeId.Value))
                .Select(a => new { a.ContentZoneNodeId, ItemNodeId = a.ParentItemNodeId!.Value })
                .ToListAsync(ct);

            // A nested zone belongs to every page whose tree holds an item owning it.
            frontier = (from n in nested
                        join iz in itemZones on n.ItemNodeId equals iz.NodeId
                        join z in added on iz.ContentZoneNodeId equals z.ContentZoneNodeId
                        select (n.ContentZoneNodeId, z.PageNodeId))
                .Distinct()
                .ToList();
        }

        return result;
    }

    private async Task<(ContentZoneDTO Zone, ContentZoneAssignmentDTO Assignment)> ResolveOrCreateAssignmentAsync(
        Guid? parentPageNodeId, Guid? parentItemNodeId, string slotName,
        ContentZoneAssignmentDTO? assignment, CancellationToken ct)
    {
        using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            assignment = parentPageNodeId.HasValue
                ? await _context.Set<ContentZoneAssignmentDTO>()
                    .FirstOrDefaultAsync(a => a.ParentPageNodeId == parentPageNodeId.Value && a.SlotName == slotName, ct)
                : await _context.Set<ContentZoneAssignmentDTO>()
                    .FirstOrDefaultAsync(a => a.ParentItemNodeId == parentItemNodeId!.Value && a.SlotName == slotName, ct);

            if (assignment != null)
            {
                var existingZone = await ResolveZoneAsync(assignment.ContentZoneNodeId, ct);
                if (existingZone != null)
                {
                    await transaction.RollbackAsync(ct);
                    return (existingZone, assignment);
                }

                // Dangling assignment: the zone it points to no longer exists. Create a real zone and
                // repoint the assignment instead of handing back an unpersisted Guid.Empty zone.
                _logger.Warning(
                    "Content zone assignment {AssignmentId} pointed at missing zone {StaleZoneNodeId} for slot '{SlotName}'; repairing.",
                    assignment.Id, assignment.ContentZoneNodeId, slotName);

                var repairedZone = await CreatePublishedZoneAsync(slotName, ct);
                assignment.ContentZoneNodeId = repairedZone.Version.Node!.Id;
                assignment.ContentZoneNode = repairedZone.Version.Node;
                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return (repairedZone, assignment);
            }

            var zone = await CreatePublishedZoneAsync(slotName, ct);
            var newAssignment = new ContentZoneAssignmentDTO
            {
                Id = Guid.NewGuid(),
                SlotName = slotName,
                ContentZoneNodeId = zone.Version.Node!.Id,
                ContentZoneNode = zone.Version.Node,
                ParentPageNodeId = parentPageNodeId,
                ParentItemNodeId = parentItemNodeId
            };
            _context.Set<ContentZoneAssignmentDTO>().Add(newAssignment);
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return (zone, newAssignment);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<bool> DeleteZoneAsync(Guid zoneNodeId, CancellationToken ct = default)
    {
        // The zone's own assignment rows go first, so a nested-zone cycle can never lead back here.
        var assignments = await _context.Set<ContentZoneAssignmentDTO>()
            .Where(a => a.ContentZoneNodeId == zoneNodeId)
            .ToListAsync(ct);
        _context.Set<ContentZoneAssignmentDTO>().RemoveRange(assignments);
        await _context.SaveChangesAsync(ct);

        var items = await _context.Set<ContentZoneItemDTO>()
            .AsNoTracking()
            .Where(i => i.ContentZoneNodeId == zoneNodeId)
            .Select(i => i.Version.NodeId)
            .Distinct()
            .ToListAsync(ct);
        foreach (var itemNodeId in items)
        {
            // A widget whose published version lives in another zone was dragged in (or has since been
            // published elsewhere): send it home and drop its versions here instead of deleting a live item.
            if (await HasPublishedVersionOutsideAsync(itemNodeId, zoneNodeId, ct))
            {
                await _itemStore.DiscardDraftAsync(itemNodeId, ct);
                var strayVersionIds = await _context.Set<ContentZoneItemDTO>()
                    .AsNoTracking()
                    .Where(i => i.Version.NodeId == itemNodeId && i.ContentZoneNodeId == zoneNodeId)
                    .Select(i => i.VersionId)
                    .ToListAsync(ct);
                foreach (var versionId in strayVersionIds)
                    await _itemStore.DeleteVersionAsync(versionId, ct);
                continue;
            }

            await DeleteItemOwnedZonesAsync(itemNodeId, ct);

            // A routable widget item owns CMSRouteDTO rows keyed to its node id; sweep them before the
            // item is hard-deleted so the route table never dangles at a deleted node.
            await _routeService.DeleteByOwningContentAsync(itemNodeId, ct);
            await _itemStore.DeleteAsync(itemNodeId, softDelete: false, ct);
        }

        return await _zoneStore.DeleteAsync(zoneNodeId, softDelete: false, ct);
    }

    public async Task DeletePageZonesAsync(Guid pageNodeId, CancellationToken ct = default)
    {
        var assignments = (await GetAllAssignmentsForPageAsync(pageNodeId, ct)).ToList();
        if (assignments.Count == 0)
            return;

        var zoneNodeIds = assignments.Select(a => a.ContentZoneNodeId).Distinct().ToList();

        // Remove this page's own assignment rows up front (tracked), so a shared zone's node can
        // survive while this page's row never dangles at a deleted page.
        var tracked = await _context.Set<ContentZoneAssignmentDTO>()
            .Where(a => a.ParentPageNodeId == pageNodeId)
            .ToListAsync(ct);
        _context.Set<ContentZoneAssignmentDTO>().RemoveRange(tracked);
        await _context.SaveChangesAsync(ct);

        foreach (var zoneNodeId in zoneNodeIds)
        {
            // A zone node is deleted exactly when nothing references it any more. Re-query after the
            // page's own rows are gone so a zone assigned to two of this page's slots still cleans up.
            var remaining = await _context.Set<ContentZoneAssignmentDTO>()
                .CountAsync(a => a.ContentZoneNodeId == zoneNodeId, ct);
            if (remaining == 0)
                await DeleteZoneAsync(zoneNodeId, ct);
        }
    }

    /// <summary>
    /// Deletes the zones nested in an item (a container widget's slots) and, through them, their own
    /// items and nested zones. A zone still referenced by another assignment keeps its node.
    /// </summary>
    private async Task DeleteItemOwnedZonesAsync(Guid itemNodeId, CancellationToken ct)
    {
        var owned = await _context.Set<ContentZoneAssignmentDTO>()
            .Where(a => a.ParentItemNodeId == itemNodeId)
            .ToListAsync(ct);
        if (owned.Count == 0)
            return;

        _context.Set<ContentZoneAssignmentDTO>().RemoveRange(owned);
        await _context.SaveChangesAsync(ct);

        foreach (var childId in owned.Select(a => a.ContentZoneNodeId).Distinct())
        {
            var remaining = await _context.Set<ContentZoneAssignmentDTO>()
                .CountAsync(a => a.ContentZoneNodeId == childId, ct);
            if (remaining == 0)
                await DeleteZoneAsync(childId, ct);
        }
    }

    private Task<bool> HasPublishedVersionOutsideAsync(Guid itemNodeId, Guid zoneNodeId, CancellationToken ct)
        => _context.Set<ContentZoneItemDTO>()
            .AnyAsync(i => i.Version.NodeId == itemNodeId
                        && i.Version.State == ContentVersionState.Published
                        && i.ContentZoneNodeId != zoneNodeId, ct);

    private async Task<ContentZoneDTO?> ResolveZoneAsync(Guid zoneNodeId, CancellationToken ct)
        => await _zoneStore.GetAsync(zoneNodeId, ct)
        ?? await _zoneStore.GetCurrentDraftAsync(zoneNodeId, ct);

    private async Task<ContentZoneDTO?> GetZoneByNameIncludingDraftsAsync(string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var published = await GetZoneByNameAsync(name, ct);
        if (published != null)
            return published;

        var drafts = await _zoneStore.GetAllCurrentDraftsAsync(ct) ?? [];
        return drafts.FirstOrDefault(z => z.Name == name);
    }

    private async Task<ContentZoneDTO> CreatePublishedZoneAsync(string name, CancellationToken ct)
    {
        var zone = NewZone(name);
        var save = await _zoneStore.SaveDraftAsync(zone, null, ct);
        if (!save.Success)
            throw new InvalidOperationException(save.ErrorMessage ?? "Failed to create content zone.");
        await _zoneStore.PublishAsync(save.NodeId, ct);
        return zone;
    }

    private static ContentZoneDTO NewZone(string name)
        => new()
        {
            Version = new ContentVersion
            {
                Title = name,
                Node = new ContentNode()
            },
            Name = name
        };
}
