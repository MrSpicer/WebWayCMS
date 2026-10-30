using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// Manages content zones: item resolution, owned zones (a page's or container item's slots), and item
/// writes. The structure is relations: an item's version sits in a zone through its
/// <see cref="ZoneRelations.Zone"/> relation (with its ordinal), and an owned zone's version names its
/// owner and slot through <see cref="ZoneRelations.Owner"/>; an owned zone's node id is deterministic
/// (<see cref="ZoneNodeIds"/>), so concurrent first renders can't create it twice.
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
            .Where(ZoneRelations.InZone(zoneNodeId))
            .Where(i => i.IsActive)
            .OrderBy(ZoneRelations.ZoneOrdinal)
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

    // ─── owned zones ──────────────────────────────────────────────────────────

    public async Task<OwnedZone?> GetByPageSlotAsync(Guid pageNodeId, string slotName, CancellationToken ct = default)
        => await _tree.ZoneOwners()
            .FirstOrDefaultAsync(a => a.ParentPageNodeId == pageNodeId && a.SlotName == slotName, ct);

    public async Task<(ContentZoneDTO Zone, OwnedZone Owner)> GetOrCreateByPageSlotAsync(Guid pageNodeId, string slotName, CancellationToken ct = default)
        => await GetOrCreateOwnedZoneAsync(await GetByPageSlotAsync(pageNodeId, slotName, ct), pageNodeId, slotName, ownerIsPage: true, ct);

    public async Task<OwnedZone?> GetByItemSlotAsync(Guid parentItemNodeId, string slotName, CancellationToken ct = default)
        => await _tree.ZoneOwners()
            .FirstOrDefaultAsync(a => a.ParentItemNodeId == parentItemNodeId && a.SlotName == slotName, ct);

    public async Task<(ContentZoneDTO Zone, OwnedZone Owner)> GetOrCreateByItemSlotAsync(Guid parentItemNodeId, string slotName, CancellationToken ct = default)
        => await GetOrCreateOwnedZoneAsync(await GetByItemSlotAsync(parentItemNodeId, slotName, ct), parentItemNodeId, slotName, ownerIsPage: false, ct);

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

    public async Task<List<OwnedZone>> GetOwnedZonesForPageAsync(Guid pageNodeId, CancellationToken ct = default)
        => await _tree.ZoneOwners()
            .Where(a => a.ParentPageNodeId == pageNodeId)
            .ToListAsync(ct);

    public async Task<List<ContentZoneDTO>> GetAllByPageAsync(Guid pageNodeId, CancellationToken ct = default)
    {
        var nodeIds = await _tree.ZoneOwners()
            .Where(a => a.ParentPageNodeId == pageNodeId)
            .Select(a => a.ZoneNodeId)
            .ToListAsync(ct);

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
        var itemNodeIds = await _tree.CurrentDraftItems()
            .Where(ZoneRelations.InZone(parentZoneNodeId))
            .Select(i => i.Version.NodeId)
            .ToListAsync(ct);

        var nodeIds = await _tree.ZoneOwners()
            .Where(a => a.ParentItemNodeId != null && itemNodeIds.Contains(a.ParentItemNodeId.Value))
            .Select(a => a.ZoneNodeId)
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

        var ownerItemIds = await _tree.ZoneOwners()
            .Where(a => a.ParentItemNodeId != null)
            .Select(a => a.ParentItemNodeId!.Value)
            .ToListAsync(ct);

        var parentIds = await _tree.CurrentDraftItems()
            .Where(ZoneRelations.InZones(ids))
            .Where(i => ownerItemIds.Contains(i.Version.NodeId))
            .Select(ZoneRelations.ZoneOf)
            .Distinct()
            .ToListAsync(ct);

        return [.. parentIds];
    }

    public async Task<List<string>> GetOwnedSlotsWithItemsAsync(Guid itemNodeId, CancellationToken ct = default)
    {
        var owned = await _tree.ZoneOwners()
            .Where(a => a.ParentItemNodeId == itemNodeId)
            .ToListAsync(ct);
        if (owned.Count == 0)
            return [];

        // Removed (inactive) widgets count: they move with their zone, so a restore finds them in the new view.
        var withItems = await _tree.CurrentDraftItems()
            .Where(ZoneRelations.InZones(owned.Select(a => a.ZoneNodeId)))
            .Select(ZoneRelations.ZoneOf)
            .Distinct()
            .ToListAsync(ct);

        return owned.Where(a => withItems.Contains(a.ZoneNodeId)).Select(a => a.SlotName).ToList();
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
            var zoneIds = await _tree.ZoneOwners()
                .Where(a => a.ParentItemNodeId != null && owners.Contains(a.ParentItemNodeId.Value))
                .Select(a => a.ZoneNodeId)
                .ToListAsync(ct);

            var itemIds = await _context.Set<ContentZoneItemDTO>()
                .AsNoTracking()
                .Where(ZoneRelations.InZones(zoneIds))
                .Where(i => !i.Version.Node.IsDeleted)
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
        var owned = await _tree.ZoneOwners()
            .Where(a => a.ParentItemNodeId != null && ownerIds.Contains(a.ParentItemNodeId.Value))
            .ToListAsync(ct);
        if (owned.Count == 0)
            return [];

        var activeZoneIds = await _tree.CurrentDraftItems()
            .Where(i => i.IsActive)
            .Where(ZoneRelations.InZones(owned.Select(a => a.ZoneNodeId)))
            .Select(ZoneRelations.ZoneOf)
            .Distinct()
            .ToListAsync(ct);

        return owned
            .Where(a => activeZoneIds.Contains(a.ZoneNodeId))
            .Select(a => new ItemOwnedZone(a.ZoneNodeId, a.SlotName, owners[a.ParentItemNodeId!.Value]))
            .ToList();
    }

    public async Task<Dictionary<Guid, int>> GetAssignmentCountsByNodeIdAsync(IEnumerable<Guid> nodeIds, CancellationToken ct = default)
    {
        var ids = nodeIds.ToList();
        if (ids.Count == 0) return [];

        // A zone has at most one owner; global zones have none.
        return (await _tree.ZoneOwners()
                .Where(a => ids.Contains(a.ZoneNodeId))
                .Select(a => a.ZoneNodeId)
                .ToListAsync(ct))
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());
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

        // New items always carry a metadata document, so the column never holds null on a write.
        if (string.IsNullOrWhiteSpace(item.MetadataJson))
            item.MetadataJson = "{}";

        // A caller may place the item at an ordinal in this zone; otherwise it goes last.
        var ordinal = item.ContentZoneNodeId == zoneNodeId ? item.Ordinal : 0;
        var mintOrdinal = ordinal == 0;

        using var _ = _changeSetScope.Begin(ChangeSetKind.Save, zoneNodeId, null);

        // Minting and saving share one transaction: the row lock in MintLastOrdinalAsync only helps
        // if the insert that consumes the minted ordinal commits while the lock is still held.
        var result = await RunAtomicallyAsync(async () =>
        {
            if (mintOrdinal)
                ZoneRelations.Place(item.Version, zoneNodeId, await MintLastOrdinalAsync(zoneNodeId, ct));

            using (_changeSetScope.UseChangeNote($"Added to {zone.Name}"))
            {
                var save = await _itemStore.SaveDraftAsync(item, null, ct);
                if (!save.Success)
                    return (false, save.ErrorMessage ?? "Failed to add content zone item.");

                if (!await IsPageOwnedZoneAsync(zoneNodeId, ct))
                    await _itemStore.PublishAsync(save.NodeId, ct);
            }
            return (true, null);
        }, ct);
        if (!result.Success)
            throw new InvalidOperationException(result.ErrorMessage ?? "Failed to add content zone item.");

        await RecordAsync(zoneNodeId, $"Added {item.ComponentName} to {zone.Name}", ct);
        return item;
    }

    /// <summary>
    /// The zone's last ordinal + 1, minted under the zone's row lock (<see cref="LockZonesAsync"/>) so two
    /// concurrent adds cannot read the same MAX and share an ordinal, and an add cannot interleave with a
    /// reorder, move, restore or remap renumbering the same zone.
    /// </summary>
    private async Task<int> MintLastOrdinalAsync(Guid zoneNodeId, CancellationToken ct)
    {
        await LockZonesAsync([zoneNodeId], ct);

        var maxOrdinal = await _context.Set<ContentZoneItemDTO>()
            .Where(ZoneRelations.InZone(zoneNodeId))
            .Select(ZoneRelations.ZoneOrdinal)
            .MaxAsync(o => (int?)o, ct) ?? 0;
        return maxOrdinal + 1;
    }

    /// <summary>
    /// Takes each zone's row lock for the rest of the enclosing transaction. Every write of a zone's
    /// ordinals (an add's mint, a reorder, a move, a restore, a remap) runs inside
    /// <see cref="RunAtomicallyAsync"/> and locks first, so they serialize per zone; there is no unique index
    /// on the ordinal, deliberately, because a reorder rewrites 1..n sequentially. The lock is the zone's own
    /// version rows, which exist even in an empty zone. Zones are locked in a fixed (id) order so two writers
    /// placing across the same pair of zones cannot deadlock. The InMemory provider (the unit-test database)
    /// has no row locks; a single-context test database cannot race itself anyway.
    /// </summary>
    private async Task LockZonesAsync(IEnumerable<Guid> zoneNodeIds, CancellationToken ct)
    {
        if (!_context.Database.IsRelational())
            return;

        // Interpolated, not ExecuteSqlRawAsync(sql, id, ct): that binds to the params overload and sends the
        // CancellationToken as a SQL parameter.
        foreach (var zoneNodeId in zoneNodeIds.Distinct().Order())
            await _context.Database.ExecuteSqlAsync(
                $"SELECT \"Id\" FROM \"ContentVersions\" WHERE \"NodeId\" = {zoneNodeId} FOR UPDATE", ct);
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
        IsActive = item.IsActive,
        // The caller's relation writes (e.g. config.* ids) ride on its own copy of the version; the
        // reloaded version would otherwise carry the old relations forward and drop them. Placement is
        // not an update's to change (that is a reorder or move), so a Zone write is dropped and the
        // existing zone and ordinal carry forward.
        Version = existing.Version with
        {
            RelationWrites = item.Version.RelationWrites
                .Where(w => w.Key != ZoneRelations.Zone)
                .ToDictionary(w => w.Key, w => w.Value)
        }
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
                var (_, owner) = await GetOrCreateByItemSlotAsync(ownerItemNodeId, move.ToSlot, ct);
                group = new SlotMoveGroup(move.ToSlot, owner.ZoneNodeId, await ZoneOrderAsync(owner.ZoneNodeId, ct), []);
                groups.Add(group);
            }

            if (source.ZoneNodeId == group.ZoneNodeId)
                continue;

            var items = await ZoneOrderAsync(source.ZoneNodeId, ct);
            group.Order.AddRange(items);
            group.Moved.UnionWith(items.Select(i => i.Version.NodeId));
        }

        return groups.Where(g => g.Moved.Count > 0).ToList();
    }

    private Task<List<ContentZoneItemDTO>> ZoneOrderAsync(Guid zoneNodeId, CancellationToken ct)
        => _tree.CurrentDraftItems()
            .Where(ZoneRelations.InZone(zoneNodeId))
            .OrderBy(ZoneRelations.ZoneOrdinal)
            .ToListAsync(ct);

    /// <summary>Writes each target zone's new order; moved items are noted as such, renumbered ones as siblings.</summary>
    private async Task<(bool Success, string? ErrorMessage)> ApplyMovesAsync(List<SlotMoveGroup> groups, bool publish, CancellationToken ct)
    {
        await LockZonesAsync(groups.Select(g => g.ZoneNodeId), ct);
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
        var order = (await ZoneOrderAsync(zoneNodeId, ct))
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
        var siblings = (await ZoneOrderAsync(targetZoneNodeId, ct))
            .Where(i => i.Version.NodeId != itemNodeId)
            .ToList();
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
        var siblings = (await ZoneOrderAsync(targetZoneNodeId, ct))
            .Where(i => i.Version.NodeId != itemNodeId)
            .ToList();
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
        // Both zones up front, in lock order, before either is written.
        await LockZonesAsync([targetZoneNodeId, sourceZoneNodeId], ct);

        var target = await SaveZoneOrderAsync(targetZoneNodeId, targetOrder, publishTarget, notes, ct);
        if (!target.Success)
            return target;

        // A cross-zone placement leaves an ordinal gap in the source zone: renumber what remains. The
        // placed item's draft already sits in the target zone, so the query finds only what stayed
        // behind. Both zones belong to the same page (validated by the callers), so there is nothing to
        // publish here.
        if (sourceZoneNodeId == targetZoneNodeId)
            return target;

        var remaining = await ZoneOrderAsync(sourceZoneNodeId, ct);
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
    /// the store's "changed by someone else" message). The zone is locked first (<see cref="LockZonesAsync"/>)
    /// and re-read: a planned item changed since the caller read it fails the write as stale, and an item an
    /// add committed meanwhile goes last, so no two items ever share an ordinal. Earlier saves are not undone
    /// here: the caller owns the change-set scope and runs this inside <see cref="RunAtomicallyAsync"/>.
    /// </summary>
    private async Task<(bool Success, string? ErrorMessage)> SaveZoneOrderAsync(
        Guid zoneNodeId, List<ContentZoneItemDTO> itemsInOrder, bool publish, OrderNotes notes, CancellationToken ct)
    {
        await LockZonesAsync([zoneNodeId], ct);

        // The order was planned from a read taken before the lock. Under it, the zone as it is now decides:
        // a planned item still here at another version than it was read with (a concurrent move, reorder
        // or edit) fails the write as stale — the skip below would otherwise trust its stale ordinal and
        // never version-check it — while an item an add committed meanwhile goes last. A planned item that
        // left the zone meanwhile cannot collide: its stale copy is either skipped (a gap) or saved and
        // refused by the store's version check.
        var now = await ZoneOrderAsync(zoneNodeId, ct);
        var nowById = now.ToDictionary(i => i.Version.NodeId);
        if (itemsInOrder.Any(i => nowById.TryGetValue(i.Version.NodeId, out var current)
                && current.Version.VersionNumber != i.Version.VersionNumber))
            return (false, ContentStore<ContentZoneItemDTO>.StaleVersionMessage);

        var planned = itemsInOrder.Select(i => i.Version.NodeId).ToHashSet();
        var joined = now.Where(i => !planned.Contains(i.Version.NodeId)).ToList();
        if (joined.Count > 0)
            itemsInOrder = [.. itemsInOrder, .. joined];

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
                    ZoneRelations.Placed(current, zoneNodeId, position),
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
            .Where(ZoneRelations.InZones(ids))
            .Select(i => i.Version.NodeId)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task<PageTreeLiveItems> GetLiveTreeItemsForPageAsync(Guid pageNodeId, CancellationToken ct = default)
    {
        // What the page shows live: the zone tree's own walk (active items under active containers).
        var live = await _tree.GetLiveTreeItemsAsync(pageNodeId, ct);

        // Every other published item in the tree is not shown, so its route rows are stale: a removed
        // (inactive) widget, and an active one nested, at any depth, under a removed container (the walk
        // stops there). Zone scope is the whole tree, since a removed container keeps its nested zones.
        var zoneIds = await GetZoneTreeNodeIdsForPageAsync(pageNodeId, ct);
        var liveIds = live.Select(i => i.Version.NodeId).ToList();
        var removed = zoneIds.Count == 0
            ? []
            : await _context.Set<ContentZoneItemDTO>()
                .AsNoTracking()
                .Where(i => i.Version.State == ContentVersionState.Published
                         && i.Version.Culture == string.Empty
                         && i.Version.Segment == string.Empty
                         && !i.Version.Node.IsDeleted
                         && !liveIds.Contains(i.Version.NodeId))
                .Where(ZoneRelations.InZones(zoneIds))
                .ToListAsync(ct);

        return new PageTreeLiveItems(live, removed);
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

    /// <summary>
    /// The owner's zone in <paramref name="slotName"/>: the existing one when <paramref name="owned"/> resolves,
    /// else created (published at once) on its deterministic node id. A concurrent create of the same zone
    /// loses on the node's primary key and reads the winner's zone instead. A caller's open transaction (a
    /// container's view-change save) is joined as-is, so its rollback undoes the zone too.
    /// </summary>
    private async Task<(ContentZoneDTO Zone, OwnedZone Owner)> GetOrCreateOwnedZoneAsync(
        OwnedZone? owned, Guid ownerNodeId, string slotName, bool ownerIsPage, CancellationToken ct)
    {
        var zoneNodeId = owned?.ZoneNodeId ?? ZoneNodeIds.For(ownerNodeId, slotName);
        owned ??= new OwnedZone
        {
            ZoneNodeId = zoneNodeId,
            SlotName = slotName,
            ParentPageNodeId = ownerIsPage ? ownerNodeId : null,
            ParentItemNodeId = ownerIsPage ? null : ownerNodeId
        };

        var existing = await ResolveZoneAsync(zoneNodeId, ct);
        if (existing != null)
            return (existing, owned);

        var zone = NewZone(slotName);
        ZoneRelations.Own(zone.Version, ownerNodeId, slotName);
        var save = await _zoneStore.CreateWithNodeIdAsync(zone, zoneNodeId, ct);
        if (!save.Success)
        {
            if (save.ErrorMessage == ContentStore<ContentZoneDTO>.AlreadyExistsMessage
                && await ResolveZoneAsync(zoneNodeId, ct) is { } raced)
                return (raced, owned);
            throw new InvalidOperationException(save.ErrorMessage ?? "Failed to create content zone.");
        }

        await _zoneStore.PublishAsync(zoneNodeId, ct);
        return (zone, owned);
    }

    public Task<bool> DeleteZoneAsync(Guid zoneNodeId, CancellationToken ct = default)
        => DeleteZoneCoreAsync(zoneNodeId, [], ct);

    /// <summary>
    /// Deletes one zone with its content. The zone's ownership is a relation on its own versions, so it
    /// goes with them — and stays until then, so <paramref name="visited"/> stops a nested-zone cycle from
    /// leading back here.
    /// </summary>
    private async Task<bool> DeleteZoneCoreAsync(Guid zoneNodeId, HashSet<Guid> visited, CancellationToken ct)
    {
        if (!visited.Add(zoneNodeId))
            return true;

        var items = await _context.Set<ContentZoneItemDTO>()
            .AsNoTracking()
            .Where(ZoneRelations.InZone(zoneNodeId))
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
                    .Where(i => i.Version.NodeId == itemNodeId)
                    .Where(ZoneRelations.InZone(zoneNodeId))
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

            await DeleteItemCoreAsync(itemNodeId, visited, ct);
        }

        return await _zoneStore.DeleteAsync(zoneNodeId, softDelete: false, ct);
    }

    public Task<bool> DeleteItemAsync(Guid itemNodeId, CancellationToken ct = default)
        => DeleteItemCoreAsync(itemNodeId, [], ct);

    private async Task<bool> DeleteItemCoreAsync(Guid itemNodeId, HashSet<Guid> visited, CancellationToken ct)
    {
        await DeleteItemOwnedZonesAsync(itemNodeId, visited, ct);

        // A routable widget item owns CMSRouteDTO rows keyed to its node id; sweep them before the
        // item is hard-deleted so the route table never dangles at a deleted node.
        await _routeService.DeleteByOwningContentAsync(itemNodeId, ct);
        return await _itemStore.DeleteAsync(itemNodeId, softDelete: false, ct);
    }

    public async Task DeletePageZonesAsync(Guid pageNodeId, CancellationToken ct = default)
    {
        var visited = new HashSet<Guid>();
        foreach (var owned in await GetOwnedZonesForPageAsync(pageNodeId, ct))
            await DeleteZoneCoreAsync(owned.ZoneNodeId, visited, ct);
    }

    /// <summary>
    /// Deletes the zones nested in an item (a container widget's slots) and, through them, their own
    /// items and nested zones.
    /// </summary>
    private async Task DeleteItemOwnedZonesAsync(Guid itemNodeId, HashSet<Guid> visited, CancellationToken ct)
    {
        var owned = await _tree.ZoneOwners()
            .Where(a => a.ParentItemNodeId == itemNodeId)
            .Select(a => a.ZoneNodeId)
            .ToListAsync(ct);
        foreach (var childId in owned)
            await DeleteZoneCoreAsync(childId, visited, ct);
    }

    /// <summary>The item's published version id when that version sits outside <paramref name="zoneNodeId"/>.</summary>
    private async Task<Guid?> PublishedVersionOutsideAsync(Guid itemNodeId, Guid zoneNodeId, CancellationToken ct)
        => await _context.Set<ContentZoneItemDTO>()
            .AsNoTracking()
            .Where(i => i.Version.NodeId == itemNodeId
                     && i.Version.State == ContentVersionState.Published
                     && !i.Version.Relations.Any(r => r.Name == ZoneRelations.Zone && r.TargetNodeId == zoneNodeId))
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
