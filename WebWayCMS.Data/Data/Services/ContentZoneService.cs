using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// Manages content zones: item resolution, assignment (slot) management, and item writes.
/// Zone and item versioning is delegated to the generic <see cref="IContentStore{T}"/>. Zone items
/// auto-publish on write (there is no separate publish surface for items), preserving the inline
/// editor's immediately-visible behaviour while still producing proper version history.
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

    public async Task<ContentZoneAssignmentDTO?> GetByZoneSlotAsync(Guid parentZoneNodeId, string slotName, CancellationToken ct = default)
    {
        return await _context.Set<ContentZoneAssignmentDTO>()
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ParentZoneNodeId == parentZoneNodeId && a.SlotName == slotName, ct);
    }

    public async Task<(ContentZoneDTO Zone, ContentZoneAssignmentDTO Assignment)> GetOrCreateByZoneSlotAsync(Guid parentZoneNodeId, string slotName, CancellationToken ct = default)
    {
        var assignment = await GetByZoneSlotAsync(parentZoneNodeId, slotName, ct);
        if (assignment != null)
        {
            var zone = await ResolveZoneAsync(assignment.ContentZoneNodeId, ct);
            if (zone != null)
                return (zone, assignment);
        }

        return await ResolveOrCreateAssignmentAsync(null, parentZoneNodeId, slotName, assignment, ct);
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
        var assignments = await _context.Set<ContentZoneAssignmentDTO>()
            .AsNoTracking()
            .Where(a => a.ParentZoneNodeId == parentZoneNodeId)
            .ToListAsync(ct);

        var nodeIds = assignments.Select(a => a.ContentZoneNodeId).ToList();

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

        var parentIds = await _context.Set<ContentZoneAssignmentDTO>()
            .AsNoTracking()
            .Where(a => a.ParentZoneNodeId != null && ids.Contains(a.ParentZoneNodeId.Value))
            .Select(a => a.ParentZoneNodeId!.Value)
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
    {
        var visited = new HashSet<Guid>();
        Guid? currentZoneId = zoneNodeId;

        while (currentZoneId.HasValue)
        {
            if (!visited.Add(currentZoneId.Value))
                return null;

            var assignment = await _context.Set<ContentZoneAssignmentDTO>()
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.ContentZoneNodeId == currentZoneId.Value, ct);

            if (assignment == null)
                return null;

            if (assignment.ParentPageNodeId.HasValue)
                return assignment.ParentPageNodeId.Value;

            currentZoneId = assignment.ParentZoneNodeId;
        }

        return null;
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

        await _itemStore.PublishAsync(save.NodeId, ct);
        return item;
    }

    public async Task<bool> UpdateItemAsync(ContentZoneItemDTO item, CancellationToken ct = default)
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
            IsActive = item.IsActive
            // Ordinal and ContentZoneNodeId are preserved from the existing record.
        };

        using var _ = _changeSetScope.Begin(ChangeSetKind.Save, nodeId, null);

        var save = await _itemStore.SaveDraftAsync(updated, null, ct);
        if (!save.Success)
            return false;

        await _itemStore.PublishAsync(nodeId, ct);
        return true;
    }

    public async Task<bool> RemoveItemAsync(Guid itemNodeId, CancellationToken ct = default)
    {
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

        using var _ = _changeSetScope.Begin(ChangeSetKind.Save, zoneNodeId, null);

        for (int i = 0; i < resolved.Count; i++)
        {
            var updated = resolved[i] with { Ordinal = i + 1 };
            var save = await _itemStore.SaveDraftAsync(updated, null, ct);
            if (!save.Success)
                return false;

            await _itemStore.PublishAsync(itemNodeIdsInOrder[i], ct);
        }

        return true;
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

    private async Task<(ContentZoneDTO Zone, ContentZoneAssignmentDTO Assignment)> ResolveOrCreateAssignmentAsync(
        Guid? parentPageNodeId, Guid? parentZoneNodeId, string slotName,
        ContentZoneAssignmentDTO? assignment, CancellationToken ct)
    {
        using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            assignment = parentPageNodeId.HasValue
                ? await _context.Set<ContentZoneAssignmentDTO>()
                    .FirstOrDefaultAsync(a => a.ParentPageNodeId == parentPageNodeId.Value && a.SlotName == slotName, ct)
                : await _context.Set<ContentZoneAssignmentDTO>()
                    .FirstOrDefaultAsync(a => a.ParentZoneNodeId == parentZoneNodeId!.Value && a.SlotName == slotName, ct);

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
                ParentZoneNodeId = parentZoneNodeId
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
        var assignments = await _context.Set<ContentZoneAssignmentDTO>()
            .Where(a => a.ContentZoneNodeId == zoneNodeId || a.ParentZoneNodeId == zoneNodeId)
            .ToListAsync(ct);
        _context.Set<ContentZoneAssignmentDTO>().RemoveRange(assignments);

        var items = await _context.Set<ContentZoneItemDTO>()
            .Where(i => i.ContentZoneNodeId == zoneNodeId)
            .Select(i => i.Version.NodeId)
            .Distinct()
            .ToListAsync(ct);
        foreach (var itemNodeId in items)
        {
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
                await DeleteZoneTreeAsync(zoneNodeId, ct);
        }
    }

    public Task DeleteZoneTreeAsync(Guid zoneNodeId, CancellationToken ct = default)
        => DeleteZoneTreeAsync(zoneNodeId, new HashSet<Guid>(), ct);

    private async Task DeleteZoneTreeAsync(Guid zoneNodeId, HashSet<Guid> visited, CancellationToken ct)
    {
        if (!visited.Add(zoneNodeId))
            return;

        // Collect child zones before deleting the parent: DeleteZoneAsync removes their assignment
        // rows (ParentZoneNodeId == zoneNodeId) but leaves the child zone nodes themselves, so they
        // must be walked and deleted too.
        var childZoneIds = await _context.Set<ContentZoneAssignmentDTO>()
            .AsNoTracking()
            .Where(a => a.ParentZoneNodeId == zoneNodeId)
            .Select(a => a.ContentZoneNodeId)
            .ToListAsync(ct);

        await DeleteZoneAsync(zoneNodeId, ct);

        foreach (var childId in childZoneIds)
        {
            // DeleteZoneAsync has flushed; a child shared with another page or parent still has its
            // own assignment rows and must survive. Recurse only when nothing references it any more.
            var remaining = await _context.Set<ContentZoneAssignmentDTO>()
                .CountAsync(a => a.ContentZoneNodeId == childId, ct);
            if (remaining == 0)
                await DeleteZoneTreeAsync(childId, visited, ct);
        }
    }

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
