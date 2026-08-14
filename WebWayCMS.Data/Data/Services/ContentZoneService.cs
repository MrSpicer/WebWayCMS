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
    private readonly CmsDbContext _context;
    private readonly IContentStore<ContentZoneDTO> _zoneStore;
    private readonly IContentStore<ContentZoneItemDTO> _itemStore;
    private readonly IContentReadContext _readContext;
    private readonly IChangeSetScope _changeSetScope;

    public ContentZoneService(
        CmsDbContext context,
        IContentStore<ContentZoneDTO> zoneStore,
        IContentStore<ContentZoneItemDTO> itemStore,
        IContentReadContext readContext,
        IChangeSetScope changeSetScope)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _zoneStore = zoneStore ?? throw new ArgumentNullException(nameof(zoneStore));
        _itemStore = itemStore ?? throw new ArgumentNullException(nameof(itemStore));
        _readContext = readContext ?? throw new ArgumentNullException(nameof(readContext));
        _changeSetScope = changeSetScope ?? throw new ArgumentNullException(nameof(changeSetScope));
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
            var zone = await _zoneStore.GetAsync(assignment.ContentZoneNodeId, ct);
            if (zone != null)
                return (zone, assignment);
        }

        using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            assignment = await _context.Set<ContentZoneAssignmentDTO>()
                .FirstOrDefaultAsync(a => a.ParentPageNodeId == pageNodeId && a.SlotName == slotName, ct);

            if (assignment != null)
            {
                var existingZone = await _zoneStore.GetAsync(assignment.ContentZoneNodeId, ct);
                await transaction.RollbackAsync(ct);
                return (existingZone ?? NewZone(slotName), assignment);
            }

            var zone = await CreatePublishedZoneAsync(slotName, ct);
            var newAssignment = new ContentZoneAssignmentDTO
            {
                Id = Guid.NewGuid(),
                SlotName = slotName,
                ContentZoneNodeId = zone.Version.Node!.Id,
                ContentZoneNode = zone.Version.Node,
                ParentPageNodeId = pageNodeId,
                ParentZoneNodeId = null
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
            var zone = await _zoneStore.GetAsync(assignment.ContentZoneNodeId, ct);
            if (zone != null)
                return (zone, assignment);
        }

        using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            assignment = await _context.Set<ContentZoneAssignmentDTO>()
                .FirstOrDefaultAsync(a => a.ParentZoneNodeId == parentZoneNodeId && a.SlotName == slotName, ct);

            if (assignment != null)
            {
                var existingZone = await _zoneStore.GetAsync(assignment.ContentZoneNodeId, ct);
                await transaction.RollbackAsync(ct);
                return (existingZone ?? NewZone(slotName), assignment);
            }

            var zone = await CreatePublishedZoneAsync(slotName, ct);
            var newAssignment = new ContentZoneAssignmentDTO
            {
                Id = Guid.NewGuid(),
                SlotName = slotName,
                ContentZoneNodeId = zone.Version.Node!.Id,
                ContentZoneNode = zone.Version.Node,
                ParentPageNodeId = null,
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

    public async Task<ContentZoneDTO> GetOrCreateByNameAsync(string name, CancellationToken ct = default)
    {
        var zone = await GetZoneByNameAsync(name, ct);
        if (zone != null)
            return zone;

        using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            zone = await GetZoneByNameAsync(name, ct);
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
        using var _ = _changeSetScope.Begin(ChangeSetKind.Save, zoneNodeId, null);

        for (int i = 0; i < itemNodeIdsInOrder.Count; i++)
        {
            var itemNodeId = itemNodeIdsInOrder[i];
            var current = await _itemStore.GetCurrentDraftAsync(itemNodeId, ct);
            if (current == null)
                continue;

            var updated = current with { Ordinal = i + 1 };
            var save = await _itemStore.SaveDraftAsync(updated, null, ct);
            if (save.Success)
                await _itemStore.PublishAsync(itemNodeId, ct);
        }

        return true;
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

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
            await _itemStore.DeleteAsync(itemNodeId, softDelete: false, ct);

        return await _zoneStore.DeleteAsync(zoneNodeId, softDelete: false, ct);
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
