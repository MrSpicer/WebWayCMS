using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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
    private readonly PageZoneTree _tree;
    private readonly IPageHistoryRecorder _recorder;

    /// <summary>The change note of a widget renumbered because another widget moved past it.</summary>
    internal const string SiblingNote = "Position changed (moved by another widget)";

    public ContentZoneService(
        CmsDbContext context,
        IContentStore<ContentZoneDTO> zoneStore,
        IContentStore<ContentZoneItemDTO> itemStore,
        IContentReadContext readContext,
        IChangeSetScope changeSetScope,
        ICMSRouteService routeService,
        PageZoneTree tree,
        IPageHistoryRecorder recorder)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _zoneStore = zoneStore ?? throw new ArgumentNullException(nameof(zoneStore));
        _itemStore = itemStore ?? throw new ArgumentNullException(nameof(itemStore));
        _readContext = readContext ?? throw new ArgumentNullException(nameof(readContext));
        _changeSetScope = changeSetScope ?? throw new ArgumentNullException(nameof(changeSetScope));
        _routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
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
        var itemNodeIds = _tree.CurrentDraftItems()
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

        var parentIds = await _tree.CurrentDraftItems()
            .Where(i => ids.Contains(i.ContentZoneNodeId) && ownerItemIds.Contains(i.Version.NodeId))
            .Select(i => i.ContentZoneNodeId)
            .Distinct()
            .ToListAsync(ct);

        return [.. parentIds];
    }

    public async Task<List<string>> GetOwnedSlotsWithItemsAsync(Guid itemNodeId, CancellationToken ct = default)
    {
        // Removed (inactive) widgets count: they move with their zone, so a restore finds them in the new view.
        var zoneIdsWithItems = _tree.CurrentDraftItems().Select(i => i.ContentZoneNodeId);

        return await _context.Set<ContentZoneAssignmentDTO>()
            .AsNoTracking()
            .Where(a => a.ParentItemNodeId == itemNodeId && zoneIdsWithItems.Contains(a.ContentZoneNodeId))
            .Select(a => a.SlotName)
            .ToListAsync(ct);
    }

    public async Task<List<Guid>> GetDescendantItemNodeIdsAsync(Guid containerItemNodeId, CancellationToken ct = default)
    {
        // Any version counts, so a widget that once sat inside (since moved out or removed) keeps its rows in
        // the container's history.
        var result = new List<Guid>();
        var visited = new HashSet<Guid> { containerItemNodeId };
        var frontier = new List<Guid> { containerItemNodeId };

        while (frontier.Count > 0)
        {
            var owners = frontier;
            var zoneIds = _context.Set<ContentZoneAssignmentDTO>()
                .AsNoTracking()
                .Where(a => a.ParentItemNodeId != null && owners.Contains(a.ParentItemNodeId.Value))
                .Select(a => a.ContentZoneNodeId);

            var itemIds = await _context.Set<ContentZoneItemDTO>()
                .AsNoTracking()
                .Where(i => zoneIds.Contains(i.ContentZoneNodeId) && !i.Version.Node.IsDeleted)
                .Select(i => i.Version.NodeId)
                .Distinct()
                .ToListAsync(ct);

            frontier = itemIds.Where(visited.Add).ToList();
            result.AddRange(frontier);
        }

        return result;
    }

    public async Task<List<ItemOwnedZone>> GetItemOwnedZonesForPageAsync(Guid pageNodeId, CancellationToken ct = default)
    {
        var owners = (await _tree.GetDraftTreeItemsAsync(pageNodeId, ct)).ToDictionary(i => i.Version.NodeId);
        if (owners.Count == 0)
            return [];

        var ownerIds = owners.Keys.ToList();
        var activeZoneIds = _tree.CurrentDraftItems().Where(i => i.IsActive).Select(i => i.ContentZoneNodeId);
        var assignments = await _context.Set<ContentZoneAssignmentDTO>()
            .AsNoTracking()
            .Where(a => a.ParentItemNodeId != null
                     && ownerIds.Contains(a.ParentItemNodeId.Value)
                     && activeZoneIds.Contains(a.ContentZoneNodeId))
            .ToListAsync(ct);

        return assignments
            .Select(a => new ItemOwnedZone(a.ContentZoneNodeId, a.SlotName, owners[a.ParentItemNodeId!.Value]))
            .ToList();
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
        => (await _tree.WalkUpAsync(zoneNodeId, ct)).PageNodeId;

    // ─── page history ─────────────────────────────────────────────────────────

    /// <summary>
    /// Records the change as a Draft entry in the history of every page whose tree holds the zone (none
    /// for a global zone). Called once per public write, after it committed.
    /// </summary>
    private async Task RecordAsync(Guid zoneNodeId, string note, CancellationToken ct)
    {
        foreach (var pageNodeId in await _tree.GetPagesForZoneAsync(zoneNodeId, ct))
            await _recorder.RecordDraftAsync(pageNodeId, note, ct);
    }

    private async Task<string> ZoneNameAsync(Guid zoneNodeId, CancellationToken ct)
        => (await _zoneStore.GetCurrentDraftAsync(zoneNodeId, ct))?.Name ?? "a zone";

    /// <summary>What an update changed, e.g. "Content edited; Settings changed".</summary>
    private static string DescribeUpdate(ContentZoneItemDTO before, ContentZoneItemDTO after)
    {
        var changes = new List<string>();
        if (before.IsActive != after.IsActive)
            changes.Add(after.IsActive ? "Restored" : "Removed");
        if (before.ComponentName != after.ComponentName || before.ComponentPropertiesJson != after.ComponentPropertiesJson)
            changes.Add("Content edited");
        // UpdateItemAsync always gives the new version a metadata document; an old row may hold null.
        if ((before.MetadataJson ?? "{}") != after.MetadataJson)
            changes.Add("Settings changed");
        return changes.Count == 0 ? "Saved (no changes)" : string.Join("; ", changes);
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
        using (_changeSetScope.UseChangeNote($"Added to {zone.Name}"))
        {
            var save = await _itemStore.SaveDraftAsync(item, null, ct);
            if (!save.Success)
                throw new InvalidOperationException(save.ErrorMessage ?? "Failed to add content zone item.");

            if (!await IsPageOwnedZoneAsync(zoneNodeId, ct))
                await _itemStore.PublishAsync(save.NodeId, ct);
        }

        await RecordAsync(zoneNodeId, $"Added {item.ComponentName} to {zone.Name}", ct);
        return item;
    }

    /// <summary>The version an update of <paramref name="existing"/> with <paramref name="item"/> saves.</summary>
    public static ContentZoneItemDTO MergeUpdate(ContentZoneItemDTO existing, ContentZoneItemDTO item) => existing with
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

    public Task<bool> UpdateItemAsync(ContentZoneItemDTO item, CancellationToken ct = default, int? expectedVersionNumber = null)
        => UpdateItemCoreAsync(item, expectedVersionNumber, null, ct);

    public Task<bool> UpdateItemAsync(ContentZoneItemDTO item, SlotRemapPlan remap, int? expectedVersionNumber, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(remap);
        return UpdateItemCoreAsync(item, expectedVersionNumber, remap, ct);
    }

    private async Task<bool> UpdateItemCoreAsync(ContentZoneItemDTO item, int? expectedVersionNumber, SlotRemapPlan? remap, CancellationToken ct)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));

        var nodeId = item.Version.Node?.Id ?? Guid.Empty;
        if (nodeId == Guid.Empty)
            return false;

        var existing = await _itemStore.GetCurrentDraftAsync(nodeId, ct);
        if (existing == null)
            return false;

        var updated = MergeUpdate(existing, item);

        var note = DescribeUpdate(existing, updated);
        var publish = !await IsPageOwnedZoneAsync(existing.ContentZoneNodeId, ct);

        using var _ = _changeSetScope.Begin(ChangeSetKind.Save, nodeId, null);
        if (remap == null)
        {
            using (_changeSetScope.UseChangeNote(note))
            {
                var save = await _itemStore.SaveDraftAsync(updated, expectedVersionNumber, ct);
                if (!save.Success)
                    return false;

                if (publish)
                    await _itemStore.PublishAsync(nodeId, ct);
            }

            await RecordAsync(existing.ContentZoneNodeId, $"{updated.ComponentName}: {note}", ct);
            return true;
        }

        // A view change that may strand nested content: the container's save, the new view's zones and the
        // moves of its content into them commit together, as one history entry. The zones are created after
        // the version-checked save, so a stale or failed save leaves none behind.
        var moved = false;
        var result = await RunAtomicallyAsync(async () =>
        {
            ContentWriteResult save;
            using (_changeSetScope.UseChangeNote(note))
                save = await _itemStore.SaveDraftAsync(updated, expectedVersionNumber, ct);
            if (!save.Success)
                return (false, save.ErrorMessage);

            if (publish)
                await _itemStore.PublishAsync(nodeId, ct);

            var moves = await PrepareMovesAsync(nodeId, remap, ct);
            moved = moves.Count > 0;
            return await ApplyMovesAsync(moves, publish, ct);
        }, ct);
        if (!result.Success)
            return false;

        await RecordAsync(existing.ContentZoneNodeId, moved ? remap.HistoryNote : $"{updated.ComponentName}: {note}", ct);
        return true;
    }

    /// <summary>One target zone of a remap: its final order (existing items, then the moved ones) and which items moved.</summary>
    private sealed record SlotMoveGroup(string ToSlot, Guid ZoneNodeId, List<ContentZoneItemDTO> Order, HashSet<Guid> Moved);

    /// <summary>
    /// Resolves a remap plan against the container's zones: each target slot's zone (created if the new view
    /// never rendered it yet) and, appended after what it already holds, every current-draft item (active or
    /// removed, in order) of the source slots mapped onto it. A source slot with no zone, or one that is the
    /// target zone itself, is skipped.
    /// </summary>
    private async Task<List<SlotMoveGroup>> PrepareMovesAsync(Guid ownerItemNodeId, SlotRemapPlan plan, CancellationToken ct)
    {
        var groups = new List<SlotMoveGroup>();
        foreach (var move in plan.Moves)
        {
            var source = await GetByItemSlotAsync(ownerItemNodeId, move.FromSlot, ct);
            if (source == null)
                continue;

            var group = groups.FirstOrDefault(g => g.ToSlot == move.ToSlot);
            if (group == null)
            {
                var (_, assignment) = await GetOrCreateByItemSlotAsync(ownerItemNodeId, move.ToSlot, ct);
                group = new SlotMoveGroup(move.ToSlot, assignment.ContentZoneNodeId, await ZoneOrderAsync(assignment.ContentZoneNodeId, ct), []);
                groups.Add(group);
            }

            if (source.ContentZoneNodeId == group.ZoneNodeId)
                continue;

            var items = await ZoneOrderAsync(source.ContentZoneNodeId, ct);
            group.Order.AddRange(items);
            group.Moved.UnionWith(items.Select(i => i.Version.NodeId));
        }

        return groups.Where(g => g.Moved.Count > 0).ToList();
    }

    private Task<List<ContentZoneItemDTO>> ZoneOrderAsync(Guid zoneNodeId, CancellationToken ct)
        => _tree.CurrentDraftItems()
            .Where(i => i.ContentZoneNodeId == zoneNodeId)
            .OrderBy(i => i.Ordinal)
            .ToListAsync(ct);

    /// <summary>Writes each target zone's new order; moved items are noted as such, renumbered ones as siblings.</summary>
    private async Task<(bool Success, string? ErrorMessage)> ApplyMovesAsync(List<SlotMoveGroup> groups, bool publish, CancellationToken ct)
    {
        foreach (var group in groups)
        {
            var notes = new OrderNotes(_ => SiblingNote,
                NoteFor: (item, _) => group.Moved.Contains(item.Version.NodeId) ? $"Moved to {group.ToSlot} (layout changed)" : SiblingNote);
            var result = await SaveZoneOrderAsync(group.ZoneNodeId, group.Order, publish, notes, ct);
            if (!result.Success)
                return result;
        }

        return (true, null);
    }

    public async Task<bool> RemoveItemAsync(Guid itemNodeId, CancellationToken ct = default)
    {
        // Removal is never a delete: the item gets an inactive version, so its history stays and a restore
        // or a page revert can bring it back. A page's item stays live until the page is published; a
        // global zone's item goes now (global writes publish).
        var current = await _itemStore.GetCurrentDraftAsync(itemNodeId, ct);
        if (current == null)
            return false;
        if (!current.IsActive)
            return true;

        using var _ = _changeSetScope.Begin(ChangeSetKind.Save, itemNodeId, null);
        using (_changeSetScope.UseChangeNote("Removed"))
        {
            // Expected = the version just read, so a concurrent edit between the read and this write fails
            // instead of being overwritten by the inactive copy.
            var save = await _itemStore.SaveDraftAsync(current with { IsActive = false }, current.Version.VersionNumber, ct);
            if (!save.Success)
                return false;

            if (!await IsPageOwnedZoneAsync(current.ContentZoneNodeId, ct))
                await _itemStore.PublishAsync(itemNodeId, ct);
        }

        await RecordAsync(current.ContentZoneNodeId, $"Removed {current.ComponentName}", ct);
        return true;
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

        // The editor sends only what it shows (the active items). Everything else in the zone — removed items
        // above all — keeps its place, and the requested items fill the positions they held in the new order,
        // so the whole zone is renumbered and a removed item never shares an ordinal with a live one if its
        // removal is later undone.
        var requested = resolved.Select(i => i.Version.NodeId).ToHashSet();
        var queue = new Queue<ContentZoneItemDTO>(resolved);
        var order = (await _tree.CurrentDraftItems()
                .Where(i => i.ContentZoneNodeId == zoneNodeId)
                .OrderBy(i => i.Ordinal)
                .ToListAsync(ct))
            .Select(i => requested.Contains(i.Version.NodeId) ? queue.Dequeue() : i)
            .ToList();
        // Only a repeated id is left over; its second save is stale, so such a request fails as a whole.
        order.AddRange(queue);

        var publish = !await IsPageOwnedZoneAsync(zoneNodeId, ct);

        using var _ = _changeSetScope.Begin(ChangeSetKind.Save, zoneNodeId, null);

        // Each save is version-checked, so a concurrent edit can still fail the loop partway: the
        // transaction keeps that from leaving half-rewritten (duplicate) ordinals behind. Only items whose
        // position changes get a new version.
        var result = await RunAtomicallyAsync(
            () => SaveZoneOrderAsync(zoneNodeId, order, publish, ReorderNotes, ct), ct);
        if (!result.Success)
            return false;

        await RecordAsync(zoneNodeId, $"Reordered {await ZoneNameAsync(zoneNodeId, ct)}", ct);
        return true;
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
        var (targetPageNodeId, targetOwners) = await _tree.WalkUpAsync(targetZoneNodeId, ct);
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
        var siblings = await _tree.CurrentDraftItems()
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

        var targetName = await ZoneNameAsync(targetZoneNodeId, ct);
        var crossZone = sourceZoneNodeId != targetZoneNodeId;
        var notes = new OrderNotes(_ => SiblingNote, itemNodeId,
            p => crossZone ? $"Moved to {targetName} (position {p})" : $"Moved to position {p}");

        using var _ = _changeSetScope.Begin(ChangeSetKind.Save, targetZoneNodeId, null);

        // Both zones' renumbering commits together or not at all, so a stale sibling in the source
        // zone can never leave the widget moved while the API reports the move as failed.
        var result = await RunAtomicallyAsync(
            () => SavePlacementAsync(targetZoneNodeId, sourceZoneNodeId, siblings, publishTarget: !targetPageNodeId.HasValue, notes, ct), ct);
        if (result.Success)
            await RecordAsync(targetZoneNodeId, crossZone
                ? $"Moved {item.ComponentName} to {targetName}"
                : $"Moved {item.ComponentName} within {targetName}", ct);
        return result;
    }

    public Task<(bool Success, string? ErrorMessage)> RestoreItemVersionAsync(
        Guid versionId, string? metadataJson, int? expectedVersionNumber, CancellationToken ct = default)
        => RestoreItemVersionCoreAsync(versionId, metadataJson, expectedVersionNumber, null, ct);

    public Task<(bool Success, string? ErrorMessage)> RestoreItemVersionAsync(
        Guid versionId, string? metadataJson, int? expectedVersionNumber, SlotRemapPlan remap, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(remap);
        return RestoreItemVersionCoreAsync(versionId, metadataJson, expectedVersionNumber, remap, ct);
    }

    private async Task<(bool Success, string? ErrorMessage)> RestoreItemVersionCoreAsync(
        Guid versionId, string? metadataJson, int? expectedVersionNumber, SlotRemapPlan? remap, CancellationToken ct)
    {
        var historical = await _itemStore.GetVersionAsync(versionId, ct);
        if (historical == null)
            return (false, IContentZoneService.VersionNotFoundMessage);

        var itemNodeId = historical.Version.NodeId;
        var current = await _itemStore.GetCurrentDraftAsync(itemNodeId, ct);
        if (current == null)
            return (false, IContentZoneService.ItemNotFoundMessage);

        if (expectedVersionNumber.HasValue && current.Version.VersionNumber != expectedVersionNumber.Value)
            return (false, ContentStore<ContentZoneItemDTO>.StaleVersionMessage);

        // The version goes back where it was. That zone must still exist and, for a page's widget, still
        // be in the same page's tree (and not inside the widget itself); a global widget never changes zone.
        var targetZoneNodeId = historical.ContentZoneNodeId;
        var sourceZoneNodeId = current.ContentZoneNodeId;
        if (await _zoneStore.GetCurrentDraftAsync(targetZoneNodeId, ct) == null)
            return (false, IContentZoneService.RestoreZoneMissingMessage);

        var (targetPageNodeId, targetOwners) = await _tree.WalkUpAsync(targetZoneNodeId, ct);
        if (targetZoneNodeId != sourceZoneNodeId
            && (!targetPageNodeId.HasValue
                || targetPageNodeId != await GetParentPageNodeForZoneAsync(sourceZoneNodeId, ct)
                || targetOwners.Contains(itemNodeId)))
            return (false, IContentZoneService.RestoreZoneMissingMessage);

        // The restored content keeps the item's current zone here, so the placement save below always
        // writes it — content and position land in a single new version.
        var restored = current with
        {
            ComponentName = historical.ComponentName,
            ComponentPropertiesJson = historical.ComponentPropertiesJson,
            MetadataJson = metadataJson ?? historical.MetadataJson ?? "{}",
            IsActive = historical.IsActive
        };
        var siblings = await _tree.CurrentDraftItems()
            .Where(i => i.ContentZoneNodeId == targetZoneNodeId && i.Version.NodeId != itemNodeId)
            .OrderBy(i => i.Ordinal)
            .ToListAsync(ct);
        var insertAt = siblings.FindIndex(i => i.Ordinal >= historical.Ordinal);
        siblings.Insert(insertAt < 0 ? siblings.Count : insertAt, restored);

        var versionNumber = historical.Version.VersionNumber;
        var notes = new OrderNotes(_ => SiblingNote, itemNodeId, _ => $"Restored from version {versionNumber}", AlwaysSavePrimary: true,
            FailureMessage: "Failed to restore the widget.");

        var publish = !targetPageNodeId.HasValue;

        using var _ = _changeSetScope.Begin(ChangeSetKind.Restore, itemNodeId, null);

        // The content, the placement and the renumbering of both zones commit together — with the restored
        // view's zones and the moves of a restored container's content into them (created only once the
        // placement saved). A global zone's order save publishes what it writes, the restored item included.
        var moved = false;
        var result = await RunAtomicallyAsync(async () =>
        {
            var placed = await SavePlacementAsync(targetZoneNodeId, sourceZoneNodeId, siblings, publish, notes, ct);
            if (!placed.Success || remap == null)
                return placed;

            var moves = await PrepareMovesAsync(itemNodeId, remap, ct);
            moved = moves.Count > 0;
            return await ApplyMovesAsync(moves, publish, ct);
        }, ct);
        if (result.Success)
        {
            var note = $"Restored {historical.ComponentName} from version {versionNumber}";
            await RecordAsync(targetZoneNodeId, moved ? $"{note}; {remap!.HistoryNote}" : note, ct);
        }
        return result;
    }

    /// <summary>
    /// The change notes a zone-order save stamps: the primary item's (the one moved or restored, if any)
    /// and everyone else's, each given the item's new 1-based position.
    /// </summary>
    private sealed record OrderNotes(
        Func<int, string> Others, Guid? PrimaryNodeId = null, Func<int, string>? Primary = null, bool AlwaysSavePrimary = false,
        string FailureMessage = "Failed to move the widget.", Func<ContentZoneItemDTO, int, string>? NoteFor = null)
    {
        /// <summary>The note for a non-primary item: <see cref="NoteFor"/> when given (per item), else <see cref="Others"/>.</summary>
        public string ForOther(ContentZoneItemDTO item, int position) => NoteFor?.Invoke(item, position) ?? Others(position);
    }

    private static readonly OrderNotes ReorderNotes = new(p => $"Reordered (position {p})");
    private static readonly OrderNotes SiblingNotes = new(_ => SiblingNote);

    /// <summary>
    /// Writes <paramref name="targetOrder"/> as the target zone's order, then (for a cross-zone placement)
    /// renumbers what stayed behind in the source zone. Runs inside the caller's
    /// <see cref="RunAtomicallyAsync"/> so both zones are written together or not at all.
    /// </summary>
    private async Task<(bool Success, string? ErrorMessage)> SavePlacementAsync(
        Guid targetZoneNodeId, Guid sourceZoneNodeId, List<ContentZoneItemDTO> targetOrder, bool publishTarget,
        OrderNotes notes, CancellationToken ct)
    {
        var target = await SaveZoneOrderAsync(targetZoneNodeId, targetOrder, publishTarget, notes, ct);
        if (!target.Success)
            return target;

        // A cross-zone placement leaves an ordinal gap in the source zone: renumber what remains. The
        // placed item's draft already sits in the target zone, so the query finds only what stayed
        // behind. Both zones belong to the same page (validated by the callers), so there is nothing to
        // publish here.
        if (sourceZoneNodeId == targetZoneNodeId)
            return target;

        var remaining = await _tree.CurrentDraftItems()
            .Where(i => i.ContentZoneNodeId == sourceZoneNodeId)
            .OrderBy(i => i.Ordinal)
            .ToListAsync(ct);
        return await SaveZoneOrderAsync(sourceZoneNodeId, remaining, publish: false, SiblingNotes, ct);
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
    /// (which is how a moved item changes zone). An item already in the zone at its ordinal is left alone,
    /// so a move mints no needless versions of its siblings — except the primary item when
    /// <see cref="OrderNotes.AlwaysSavePrimary"/> (a restore, whose content changed). Each version is noted
    /// per <paramref name="notes"/>. Every save carries the version the item was read with, so a concurrent
    /// edit to a sibling fails the whole reorder instead of being overwritten by a stale copy (reported with
    /// the store's "changed by someone else" message). Earlier saves are not undone here: the caller owns
    /// the change-set scope and runs this inside <see cref="RunAtomicallyAsync"/>.
    /// </summary>
    private async Task<(bool Success, string? ErrorMessage)> SaveZoneOrderAsync(
        Guid zoneNodeId, List<ContentZoneItemDTO> itemsInOrder, bool publish, OrderNotes notes, CancellationToken ct)
    {
        for (int i = 0; i < itemsInOrder.Count; i++)
        {
            var current = itemsInOrder[i];
            var position = i + 1;
            var isPrimary = current.Version.NodeId == notes.PrimaryNodeId;
            if (!(isPrimary && notes.AlwaysSavePrimary) && current.ContentZoneNodeId == zoneNodeId && current.Ordinal == position)
                continue;

            ContentWriteResult save;
            using (_changeSetScope.UseChangeNote(isPrimary ? notes.Primary!(position) : notes.ForOther(current, position)))
            {
                save = await _itemStore.SaveDraftAsync(
                    current with { ContentZoneNodeId = zoneNodeId, Ordinal = position },
                    current.Version.VersionNumber, ct);
            }

            if (!save.Success)
                return (false, save.ErrorMessage == ContentStore<ContentZoneItemDTO>.StaleVersionMessage
                    ? save.ErrorMessage
                    : notes.FailureMessage);

            if (publish)
                await _itemStore.PublishAsync(current.Version.NodeId, ct);
        }

        return (true, null);
    }

    // ─── page draft lifecycle ─────────────────────────────────────────────────

    public Task<List<Guid>> GetZoneTreeNodeIdsForPageAsync(Guid pageNodeId, CancellationToken ct = default)
        => _tree.GetZoneTreeNodeIdsAsync(pageNodeId, ct);

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

    public Task<HashSet<Guid>> GetPagesWithPendingZoneChangesAsync(IEnumerable<Guid> pageNodeIds, CancellationToken ct = default)
        => _tree.GetPagesWithPendingZoneChangesAsync(pageNodeIds, ct);

    public async Task<PageZonePublishResult> PublishPageZonesAsync(Guid pageNodeId, CancellationToken ct = default)
        => await PublishItemsAsync(
            await GetPendingItemNodeIdsAsync(await GetZoneTreeNodeIdsForPageAsync(pageNodeId, ct), ct), ct);

    public async Task<PageZonePublishResult> PublishPageZoneItemsAsync(Guid pageNodeId, IReadOnlyCollection<Guid> itemNodeIds, CancellationToken ct = default)
    {
        var pendingIds = await GetPendingItemNodeIdsAsync(await GetZoneTreeNodeIdsForPageAsync(pageNodeId, ct), ct);
        return await PublishItemsAsync(pendingIds.Where(itemNodeIds.Contains).ToList(), ct);
    }

    private async Task<PageZonePublishResult> PublishItemsAsync(List<Guid> itemNodeIds, CancellationToken ct)
    {
        var published = new List<ContentZoneItemDTO>();
        var errors = new List<string>();
        var failed = new List<Guid>();
        foreach (var itemNodeId in itemNodeIds)
        {
            var result = await _itemStore.PublishAsync(itemNodeId, ct);
            var item = result.Success ? await _itemStore.GetCurrentDraftAsync(itemNodeId, ct) : null;
            if (item == null)
            {
                errors.Add(result.ErrorMessage ?? $"Content zone item {itemNodeId} could not be published.");
                failed.Add(itemNodeId);
                continue;
            }

            published.Add(item);
        }

        return new PageZonePublishResult(published, errors) { FailedItemNodeIds = failed };
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

    private async Task<bool> IsPageOwnedZoneAsync(Guid zoneNodeId, CancellationToken ct)
        => (await GetParentPageNodeForZoneAsync(zoneNodeId, ct)).HasValue;

    /// <summary>Items whose current draft differs from what is live (never published, or edited since).</summary>
    private IQueryable<ContentZoneItemDTO> PendingItems()
        => _context.Set<ContentZoneItemDTO>()
            .AsNoTracking()
            .Where(i => i.Version.IsCurrentDraft
                     && i.Version.Culture == string.Empty
                     && i.Version.Segment == string.Empty
                     && i.Version.State != ContentVersionState.Published
                     && !i.Version.Node.IsDeleted);

    private async Task<(ContentZoneDTO Zone, ContentZoneAssignmentDTO Assignment)> ResolveOrCreateAssignmentAsync(
        Guid? parentPageNodeId, Guid? parentItemNodeId, string slotName,
        ContentZoneAssignmentDTO? assignment, CancellationToken ct)
    {
        // A caller's open transaction (a container's view-change save) is joined as-is: Npgsql has no nested
        // transactions, and the caller's rollback then undoes the zone too.
        var ambient = _context.Database.CurrentTransaction != null;
        using var transaction = ambient ? null : await _context.Database.BeginTransactionAsync(ct);
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
                    await RollbackUnlessAmbientAsync(transaction, ct);
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
                if (transaction != null)
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
            if (transaction != null)
                await transaction.CommitAsync(ct);

            return (zone, newAssignment);
        }
        catch
        {
            await RollbackUnlessAmbientAsync(transaction, ct);
            throw;
        }
    }

    /// <summary>Rolls back a transaction this method opened; a joined (ambient) one is the caller's to end.</summary>
    private static async Task RollbackUnlessAmbientAsync(IDbContextTransaction? transaction, CancellationToken ct)
    {
        if (transaction != null)
            await transaction.RollbackAsync(ct);
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
            // Sending it home is a new version copying the published one, so the versions left in this zone
            // are all older and can go.
            var publishedOutside = await PublishedVersionOutsideAsync(itemNodeId, zoneNodeId, ct);
            if (publishedOutside.HasValue)
            {
                if (await _tree.GetItemZoneAsync(itemNodeId, ct) == zoneNodeId)
                {
                    ContentWriteResult sentHome;
                    using (_changeSetScope.UseChangeNote("Returned to its published zone"))
                        sentHome = await _itemStore.RestoreAsync(publishedOutside.Value, ct);

                    // Its current draft would otherwise be left pointing at the zone deleted below.
                    if (!sentHome.Success)
                    {
                        _logger.Warning(
                            "Could not return item {ItemNodeId} to its published zone ({Error}); zone {ZoneNodeId} was not deleted.",
                            itemNodeId, sentHome.ErrorMessage, zoneNodeId);
                        return false;
                    }
                }

                var strayVersionIds = await _context.Set<ContentZoneItemDTO>()
                    .AsNoTracking()
                    .Where(i => i.Version.NodeId == itemNodeId && i.ContentZoneNodeId == zoneNodeId)
                    .Select(i => i.VersionId)
                    .ToListAsync(ct);
                foreach (var versionId in strayVersionIds)
                {
                    if (!await _itemStore.DeleteVersionAsync(versionId, ct))
                    {
                        _logger.Warning(
                            "Could not delete version {VersionId} of item {ItemNodeId}; zone {ZoneNodeId} was not deleted.",
                            versionId, itemNodeId, zoneNodeId);
                        return false;
                    }
                }
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

    /// <summary>The item's published version id when that version sits outside <paramref name="zoneNodeId"/>.</summary>
    private async Task<Guid?> PublishedVersionOutsideAsync(Guid itemNodeId, Guid zoneNodeId, CancellationToken ct)
        => await _context.Set<ContentZoneItemDTO>()
            .AsNoTracking()
            .Where(i => i.Version.NodeId == itemNodeId
                     && i.Version.State == ContentVersionState.Published
                     && i.ContentZoneNodeId != zoneNodeId)
            .Select(i => (Guid?)i.VersionId)
            .FirstOrDefaultAsync(ct);

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
