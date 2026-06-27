using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// Service for managing ContentZones and their items.
/// </summary>
public sealed class ContentZoneService : IContentZoneService
{
    private readonly ContentZoneContext _context;

    public ContentZoneService(ContentZoneContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<ContentZoneDTO?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return await _context.ContentZones
            .Include(z => z.Items.Where(i =>
                i.IsActive &&
                !_context.ContentZoneItems.Any(i2 => i2.ContentMeta.MasterId == i.ContentMeta.MasterId && i2.ContentMeta.Version > i.ContentMeta.Version))
                .OrderBy(i => i.Ordinal))
            .AsNoTracking()
            .Where(z => z.Name == name && !z.ContentMeta.IsDeleted && z.ContentMeta.IsPublished
                && !_context.ContentZones.Any(z2 => z2.ContentMeta.MasterId == z.ContentMeta.MasterId && z2.ContentMeta.Version > z.ContentMeta.Version))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ContentZoneDTO?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.ContentZones
            .Include(z => z.Items.Where(i =>
                i.IsActive &&
                !_context.ContentZoneItems.Any(i2 => i2.ContentMeta.MasterId == i.ContentMeta.MasterId && i2.ContentMeta.Version > i.ContentMeta.Version))
                .OrderBy(i => i.Ordinal))
            .AsNoTracking()
            .FirstOrDefaultAsync(z => z.ContentId == id, ct);
    }

    public async Task<List<ContentZoneDTO>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.ContentZones
            .Include(z => z.Items.Where(i =>
                i.IsActive &&
                !_context.ContentZoneItems.Any(i2 => i2.ContentMeta.MasterId == i.ContentMeta.MasterId && i2.ContentMeta.Version > i.ContentMeta.Version))
                .OrderBy(i => i.Ordinal))
            .AsNoTracking()
            .Where(z => !z.ContentMeta.IsDeleted
                && !_context.ContentZones.Any(z2 => z2.ContentMeta.MasterId == z.ContentMeta.MasterId && z2.ContentMeta.Version > z.ContentMeta.Version))
            .OrderBy(z => z.Name)
            .ToListAsync(ct);
    }

    public async Task<ContentZoneDTO> CreateAsync(ContentZoneDTO zone, CancellationToken ct = default)
    {
        if (zone == null) throw new ArgumentNullException(nameof(zone));

        var meta = zone.ContentMeta;

        if (meta.Id == Guid.Empty)
            meta.Id = Guid.NewGuid();

        zone.ContentId = meta.Id;

        var now = DateTime.UtcNow;
        meta.CreationDate = now;
        meta.ModificationDate = now;
        if (meta.PublicationDate == default)
            meta.PublicationDate = now;

        meta.MasterId = meta.Id;
        _context.ContentZones.Add(zone);
        await _context.SaveChangesAsync(ct);
        return zone;
    }

    public async Task<bool> UpdateAsync(ContentZoneDTO zone, CancellationToken ct = default)
    {
        if (zone == null) throw new ArgumentNullException(nameof(zone));

        var existing = await _context.ContentZones.FirstOrDefaultAsync(z => z.ContentId == zone.ContentId, ct);
        if (existing == null) return false;

        var newMeta = existing.ContentMeta with
        {
            Id = Guid.NewGuid(),
            Version = existing.ContentMeta.Version + 1,
            Title = zone.ContentMeta.Title,
            IsPublished = zone.ContentMeta.IsPublished,
            IsArchived = zone.ContentMeta.IsArchived,
            IsHidden = zone.ContentMeta.IsHidden,
            ModificationDate = DateTime.UtcNow,
            CustomFields = existing.ContentMeta.CustomFields.Select(c => c with { }).ToList()
        };
        var newVersion = existing with
        {
            ContentId = newMeta.Id,
            ContentMeta = newMeta,
            Name = zone.Name,
            Description = zone.Description
        };
        _context.ContentZones.Add(newVersion);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await _context.ContentZones.FirstOrDefaultAsync(z => z.ContentId == id, ct);
        if (existing == null) return false;

        _context.ContentZones.Remove(existing);
        _context.Remove(existing.ContentMeta);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ContentZoneItemDTO> AddItemAsync(Guid zoneId, ContentZoneItemDTO item, CancellationToken ct = default)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));

        var zone = await _context.ContentZones.FirstOrDefaultAsync(z => z.ContentId == zoneId, ct);
        if (zone == null)
            throw new InvalidOperationException($"Content zone with ID {zoneId} not found.");

        var meta = item.ContentMeta;
        if (meta.Id == Guid.Empty)
            meta.Id = Guid.NewGuid();

        item.ContentId = meta.Id;
        item.ContentZoneId = zoneId;
        meta.MasterId = meta.Id;
        meta.Version = 0;
        meta.IsPublished = true;
        meta.CreationDate = DateTime.UtcNow;
        meta.ModificationDate = DateTime.UtcNow;

        // Auto-assign ordinal if not set
        if (item.Ordinal == 0)
        {
            var maxOrdinal = await _context.ContentZoneItems
                .Where(i => i.ContentZoneId == zoneId)
                .MaxAsync(i => (int?)i.Ordinal, ct) ?? 0;
            item.Ordinal = maxOrdinal + 1;
        }

        _context.ContentZoneItems.Add(item);
        await _context.SaveChangesAsync(ct);
        return item;
    }

    public async Task<bool> UpdateItemAsync(ContentZoneItemDTO item, CancellationToken ct = default)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));

        var existing = await _context.ContentZoneItems.FirstOrDefaultAsync(i => i.ContentId == item.ContentId, ct);
        if (existing == null) return false;

        var newMeta = existing.ContentMeta with
        {
            Id = Guid.NewGuid(),
            Version = existing.ContentMeta.Version + 1,
            ModificationDate = DateTime.UtcNow,
            CustomFields = existing.ContentMeta.CustomFields.Select(c => c with { }).ToList()
        };
        var newVersion = existing with
        {
            ContentId = newMeta.Id,
            ContentMeta = newMeta,
            ComponentName = item.ComponentName,
            ComponentPropertiesJson = item.ComponentPropertiesJson,
            IsActive = item.IsActive
            // Ordinal, ContentZoneId, MasterId preserved from existing
        };
        _context.ContentZoneItems.Add(newVersion);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RemoveItemAsync(Guid itemId, CancellationToken ct = default)
    {
        var existing = await _context.ContentZoneItems.FirstOrDefaultAsync(i => i.ContentId == itemId, ct);
        if (existing == null) return false;

        _context.ContentZoneItems.Remove(existing);
        _context.Remove(existing.ContentMeta);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ContentZoneItemDTO?> GetItemByIdAsync(Guid itemId, CancellationToken ct = default)
    {
        return await _context.ContentZoneItems
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.ContentId == itemId, ct);
    }

    public async Task<bool> ReorderItemsAsync(Guid zoneId, List<Guid> itemIdsInOrder, CancellationToken ct = default)
    {
        var items = await _context.ContentZoneItems
            .Where(i => i.ContentZoneId == zoneId
                && !_context.ContentZoneItems.Any(i2 => i2.ContentMeta.MasterId == i.ContentMeta.MasterId && i2.ContentMeta.Version > i.ContentMeta.Version))
            .ToListAsync(ct);

        for (int i = 0; i < itemIdsInOrder.Count; i++)
        {
            var item = items.FirstOrDefault(x => x.ContentId == itemIdsInOrder[i]);
            if (item != null)
            {
                item.Ordinal = i + 1;
                item.ContentMeta.ModificationDate = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ContentZoneAssignmentDTO?> GetByPageSlotAsync(Guid pageMasterId, string slotName, CancellationToken ct = default)
    {
        return await _context.ContentZoneAssignments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ParentPageMasterId == pageMasterId && a.SlotName == slotName, ct);
    }

    public async Task<(ContentZoneDTO Zone, ContentZoneAssignmentDTO Assignment)> GetOrCreateByPageSlotAsync(Guid pageMasterId, string slotName, CancellationToken ct = default)
    {
        var assignment = await _context.ContentZoneAssignments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ParentPageMasterId == pageMasterId && a.SlotName == slotName, ct);

        if (assignment != null)
        {
            var latestZone = await _context.ContentZones
                .Include(z => z.Items.Where(i =>
                    i.IsActive &&
                    !_context.ContentZoneItems.Any(i2 => i2.ContentMeta.MasterId == i.ContentMeta.MasterId && i2.ContentMeta.Version > i.ContentMeta.Version))
                    .OrderBy(i => i.Ordinal))
                .AsNoTracking()
                .Where(z => z.ContentMeta.MasterId == assignment.ContentZoneId && !z.ContentMeta.IsDeleted)
                .OrderByDescending(z => z.ContentMeta.Version)
                .FirstOrDefaultAsync(ct);

            if (latestZone != null)
                return (latestZone, assignment);
        }

        // Create zone + assignment atomically
        using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            // Re-check inside transaction to avoid duplicate creation
            assignment = await _context.ContentZoneAssignments
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.ParentPageMasterId == pageMasterId && a.SlotName == slotName, ct);

            if (assignment != null)
            {
                var latestZone = await _context.ContentZones
                    .Include(z => z.Items.Where(i =>
                        i.IsActive &&
                        !_context.ContentZoneItems.Any(i2 => i2.ContentMeta.MasterId == i.ContentMeta.MasterId && i2.ContentMeta.Version > i.ContentMeta.Version))
                        .OrderBy(i => i.Ordinal))
                    .Where(z => z.ContentMeta.MasterId == assignment.ContentZoneId && !z.ContentMeta.IsDeleted)
                    .OrderByDescending(z => z.ContentMeta.Version)
                    .FirstOrDefaultAsync(ct);

                await transaction.RollbackAsync(ct);
                return (latestZone!, assignment);
            }

            var zone = NewPublishedZone(slotName);
            _context.ContentZones.Add(zone);

            var newAssignment = new ContentZoneAssignmentDTO
            {
                Id = Guid.NewGuid(),
                SlotName = slotName,
                ContentZoneId = zone.ContentId,
                ParentPageMasterId = pageMasterId,
                ParentZoneId = null
            };
            _context.ContentZoneAssignments.Add(newAssignment);
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            newAssignment.ContentZone = zone;
            return (zone, newAssignment);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<ContentZoneAssignmentDTO?> GetByZoneSlotAsync(Guid parentZoneId, string slotName, CancellationToken ct = default)
    {
        return await _context.ContentZoneAssignments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ParentZoneId == parentZoneId && a.SlotName == slotName, ct);
    }

    public async Task<(ContentZoneDTO Zone, ContentZoneAssignmentDTO Assignment)> GetOrCreateByZoneSlotAsync(Guid parentZoneId, string slotName, CancellationToken ct = default)
    {
        var assignment = await _context.ContentZoneAssignments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ParentZoneId == parentZoneId && a.SlotName == slotName, ct);

        if (assignment != null)
        {
            var latestZone = await _context.ContentZones
                .Include(z => z.Items.Where(i =>
                    i.IsActive &&
                    !_context.ContentZoneItems.Any(i2 => i2.ContentMeta.MasterId == i.ContentMeta.MasterId && i2.ContentMeta.Version > i.ContentMeta.Version))
                    .OrderBy(i => i.Ordinal))
                .AsNoTracking()
                .Where(z => z.ContentMeta.MasterId == assignment.ContentZoneId && !z.ContentMeta.IsDeleted)
                .OrderByDescending(z => z.ContentMeta.Version)
                .FirstOrDefaultAsync(ct);

            if (latestZone != null)
                return (latestZone, assignment);
        }

        using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            assignment = await _context.ContentZoneAssignments
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.ParentZoneId == parentZoneId && a.SlotName == slotName, ct);

            if (assignment != null)
            {
                var latestZone = await _context.ContentZones
                    .Include(z => z.Items.Where(i =>
                        i.IsActive &&
                        !_context.ContentZoneItems.Any(i2 => i2.ContentMeta.MasterId == i.ContentMeta.MasterId && i2.ContentMeta.Version > i.ContentMeta.Version))
                        .OrderBy(i => i.Ordinal))
                    .Where(z => z.ContentMeta.MasterId == assignment.ContentZoneId && !z.ContentMeta.IsDeleted)
                    .OrderByDescending(z => z.ContentMeta.Version)
                    .FirstOrDefaultAsync(ct);

                await transaction.RollbackAsync(ct);
                return (latestZone!, assignment);
            }

            var zone = NewPublishedZone(slotName);
            _context.ContentZones.Add(zone);

            var newAssignment = new ContentZoneAssignmentDTO
            {
                Id = Guid.NewGuid(),
                SlotName = slotName,
                ContentZoneId = zone.ContentId,
                ParentPageMasterId = null,
                ParentZoneId = parentZoneId
            };
            _context.ContentZoneAssignments.Add(newAssignment);
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            newAssignment.ContentZone = zone;
            return (zone, newAssignment);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<IEnumerable<ContentZoneAssignmentDTO>> GetAllAssignmentsForPageAsync(Guid pageMasterId, CancellationToken ct = default)
    {
        return await _context.ContentZoneAssignments
            .AsNoTracking()
            .Where(a => a.ParentPageMasterId == pageMasterId)
            .ToListAsync(ct);
    }

    public async Task<List<ContentZoneDTO>> GetAllByPageAsync(Guid pageMasterId, CancellationToken ct = default)
    {
        var assignments = await _context.ContentZoneAssignments
            .AsNoTracking()
            .Where(a => a.ParentPageMasterId == pageMasterId)
            .ToListAsync(ct);

        var masterIds = assignments.Select(a => a.ContentZoneId).ToList();

        return await _context.ContentZones
            .Include(z => z.Items.Where(i =>
                i.IsActive &&
                !_context.ContentZoneItems.Any(i2 => i2.ContentMeta.MasterId == i.ContentMeta.MasterId && i2.ContentMeta.Version > i.ContentMeta.Version))
                .OrderBy(i => i.Ordinal))
            .AsNoTracking()
            .Where(z => masterIds.Contains(z.ContentMeta.MasterId) && !z.ContentMeta.IsDeleted
                && !_context.ContentZones.Any(z2 => z2.ContentMeta.MasterId == z.ContentMeta.MasterId && z2.ContentMeta.Version > z.ContentMeta.Version))
            .OrderBy(z => z.Name)
            .ToListAsync(ct);
    }

    public async Task<List<ContentZoneDTO>> GetAllByParentZoneAsync(Guid parentZoneId, CancellationToken ct = default)
    {
        var assignments = await _context.ContentZoneAssignments
            .AsNoTracking()
            .Where(a => a.ParentZoneId == parentZoneId)
            .ToListAsync(ct);

        var masterIds = assignments.Select(a => a.ContentZoneId).ToList();

        return await _context.ContentZones
            .Include(z => z.Items.Where(i =>
                i.IsActive &&
                !_context.ContentZoneItems.Any(i2 => i2.ContentMeta.MasterId == i.ContentMeta.MasterId && i2.ContentMeta.Version > i.ContentMeta.Version))
                .OrderBy(i => i.Ordinal))
            .AsNoTracking()
            .Where(z => masterIds.Contains(z.ContentMeta.MasterId) && !z.ContentMeta.IsDeleted
                && !_context.ContentZones.Any(z2 => z2.ContentMeta.MasterId == z.ContentMeta.MasterId && z2.ContentMeta.Version > z.ContentMeta.Version))
            .OrderBy(z => z.Name)
            .ToListAsync(ct);
    }

    public async Task<ContentZoneDTO> GetOrCreateByNameAsync(string name, CancellationToken ct = default)
    {
        var zone = await _context.ContentZones
            .Include(z => z.Items.Where(i =>
                i.IsActive &&
                !_context.ContentZoneItems.Any(i2 => i2.ContentMeta.MasterId == i.ContentMeta.MasterId && i2.ContentMeta.Version > i.ContentMeta.Version))
                .OrderBy(i => i.Ordinal))
            .Where(z => z.Name == name && !z.ContentMeta.IsDeleted && z.ContentMeta.IsPublished
                && !_context.ContentZones.Any(z2 => z2.ContentMeta.MasterId == z.ContentMeta.MasterId && z2.ContentMeta.Version > z.ContentMeta.Version))
            .FirstOrDefaultAsync(ct);

        if (zone != null)
            return zone;

        using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            zone = await _context.ContentZones
                .Include(z => z.Items.Where(i =>
                    i.IsActive &&
                    !_context.ContentZoneItems.Any(i2 => i2.ContentMeta.MasterId == i.ContentMeta.MasterId && i2.ContentMeta.Version > i.ContentMeta.Version))
                    .OrderBy(i => i.Ordinal))
                .Where(z => z.Name == name && !z.ContentMeta.IsDeleted && z.ContentMeta.IsPublished
                    && !_context.ContentZones.Any(z2 => z2.ContentMeta.MasterId == z.ContentMeta.MasterId && z2.ContentMeta.Version > z.ContentMeta.Version))
                .FirstOrDefaultAsync(ct);

            if (zone != null)
            {
                await transaction.RollbackAsync(ct);
                return zone;
            }

            zone = NewPublishedZone(name);
            _context.ContentZones.Add(zone);
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return zone;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<HashSet<Guid>> GetZoneIdsWithChildrenAsync(IEnumerable<Guid> zoneIds, CancellationToken ct = default)
    {
        var ids = zoneIds.ToList();
        if (ids.Count == 0)
            return [];

        var parentIds = await _context.ContentZoneAssignments
            .AsNoTracking()
            .Where(a => a.ParentZoneId != null && ids.Contains(a.ParentZoneId.Value))
            .Select(a => a.ParentZoneId!.Value)
            .Distinct()
            .ToListAsync(ct);

        return [.. parentIds];
    }

    public async Task<List<ContentZoneDTO>> GetAllVersionsAsync(Guid masterId, CancellationToken ct = default)
    {
        return await _context.ContentZones
            .AsNoTracking()
            .Where(z => z.ContentMeta.MasterId == masterId)
            .OrderByDescending(z => z.ContentMeta.Version)
            .ToListAsync(ct);
    }

    public async Task<List<ContentZoneItemDTO>> GetAllItemVersionsAsync(Guid itemMasterId, CancellationToken ct = default)
    {
        return await _context.ContentZoneItems
            .AsNoTracking()
            .Where(i => i.ContentMeta.MasterId == itemMasterId)
            .OrderByDescending(i => i.ContentMeta.Version)
            .ToListAsync(ct);
    }

    public async Task<Dictionary<Guid, int>> GetAssignmentCountsByMasterIdAsync(IEnumerable<Guid> masterIds, CancellationToken ct = default)
    {
        var ids = masterIds.ToList();
        if (ids.Count == 0) return [];

        return await _context.ContentZoneAssignments
            .AsNoTracking()
            .Where(a => ids.Contains(a.ContentZoneId))
            .GroupBy(a => a.ContentZoneId)
            .Select(g => new { MasterId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.MasterId, x => x.Count, ct);
    }

    private static ContentZoneDTO NewPublishedZone(string name)
    {
        var zoneId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        return new ContentZoneDTO
        {
            ContentId = zoneId,
            ContentMeta = new ContentDTO
            {
                Id = zoneId,
                MasterId = zoneId,
                Title = name,
                IsPublished = true,
                CreationDate = now,
                ModificationDate = now,
                PublicationDate = now
            },
            Name = name
        };
    }
}