using Microsoft.EntityFrameworkCore;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

public sealed class ContentService<T> : IContentService<T> where T : class, IContent
{
    private readonly DbContext _dbContext;
    private readonly DbSet<T> _set;

    public ContentService(DbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _set = _dbContext.Set<T>();
    }

    public async Task<List<T>> GetAllAsync(CancellationToken ct = default)
    {
        return await _set
            .AsNoTracking()
            .Where(e => !_set.Any(e2 => e2.ContentMeta.MasterId == e.ContentMeta.MasterId && e2.ContentMeta.Version > e.ContentMeta.Version))
            .OrderByDescending(e => e.ContentMeta.ModificationDate)
            .ToListAsync(ct);
    }

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _set
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.ContentId == id, ct);
    }

    public async Task<T?> GetByMasterIdAsync(Guid masterId, CancellationToken ct = default)
    {
        return await _set
            .AsNoTracking()
            .Where(e => e.ContentMeta.MasterId == masterId)
            .OrderByDescending(e => e.ContentMeta.Version)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<T>> GetAllVersionsAsync(Guid masterId, CancellationToken ct = default)
        => await _set.AsNoTracking()
            .Where(e => e.ContentMeta.MasterId == masterId)
            .OrderByDescending(e => e.ContentMeta.Version)
            .ToListAsync(ct);

    public async Task<T> CreateAsync(T entity, CancellationToken ct = default)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity));

        var meta = entity.ContentMeta;

        if (meta.Id == Guid.Empty)
            meta.Id = Guid.NewGuid();

        entity.ContentId = meta.Id;
        meta.MasterId = meta.Id; // set masterId to own id for initial version

        // Auto-generate slug from title if slug is empty
        if (string.IsNullOrWhiteSpace(meta.Slug) && !string.IsNullOrWhiteSpace(meta.Title))
            meta.Slug = Uri.EscapeDataString(meta.Title);

        var now = DateTime.UtcNow;
        meta.CreationDate = now;
        meta.ModificationDate = now;
        if (meta.IsPublished && meta.PublicationDate == default)
            meta.PublicationDate = now;

        _set.Add(entity);
        await _dbContext.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<bool> UpdateAsync(T entity, CancellationToken ct = default)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity));

        var meta = entity.ContentMeta;
        var originalId = entity.ContentId;
        if (!await _set.AnyAsync(e => e.ContentId == originalId, ct))
            return false;

        meta.Version++;
        meta.Id = Guid.NewGuid(); // reset id for new version
        entity.ContentId = meta.Id;
        var now = DateTime.UtcNow;
        // Ensure modification timestamp reflects this update
        meta.ModificationDate = now;

        // Auto-generate slug from title if slug is empty
        if (string.IsNullOrWhiteSpace(meta.Slug) && !string.IsNullOrWhiteSpace(meta.Title))
            meta.Slug = Uri.EscapeDataString(meta.Title);

        if (meta.IsPublished && meta.PublicationDate == default)
            meta.PublicationDate = now;

        if (meta.IsPublished)
        {
            var previousPublished = await _set
                .Where(e => e.ContentMeta.MasterId == meta.MasterId && e.ContentMeta.IsPublished)
                .ToListAsync(ct);
            foreach (var prev in previousPublished)
                prev.ContentMeta.IsPublished = false;
            _dbContext.UpdateRange(previousPublished);
        }

        _dbContext.Add(entity);

        await _dbContext.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> UpsertAsync(T entity, CancellationToken ct = default)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity));

        if (entity.ContentId == Guid.Empty || entity.ContentMeta.MasterId == Guid.Empty)
        {
            await CreateAsync(entity, ct);
            return true;
        }

       return await UpdateAsync(entity, ct);
    }

    public async Task<T?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        return await _set
            .AsNoTracking()
            .Where(e => e.ContentMeta.Slug == slug
                     && !_set.Any(e2 => e2.ContentMeta.MasterId == e.ContentMeta.MasterId && e2.ContentMeta.Version > e.ContentMeta.Version))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<T>> GetChildrenAsync(Guid parentMasterId, CancellationToken ct = default)
    {
        return await _set
            .AsNoTracking()
            .Where(e => e.ContentMeta.ParentMasterId == parentMasterId
                     && !_set.Any(e2 => e2.ContentMeta.MasterId == e.ContentMeta.MasterId && e2.ContentMeta.Version > e.ContentMeta.Version))
            .OrderByDescending(e => e.ContentMeta.ModificationDate)
            .ToListAsync(ct);
    }

    public async Task<List<T>> GetRootsAsync(CancellationToken ct = default)
    {
        return await _set
            .AsNoTracking()
            .Where(e => e.ContentMeta.ParentMasterId == null
                     && !_set.Any(e2 => e2.ContentMeta.MasterId == e.ContentMeta.MasterId && e2.ContentMeta.Version > e.ContentMeta.Version))
            .OrderByDescending(e => e.ContentMeta.ModificationDate)
            .ToListAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, bool softDelete = false, bool deleteHistory = false, CancellationToken ct = default)
    {
        var entity = await _set.FirstOrDefaultAsync(e => e.ContentId == id, ct);
        if (entity == null) return false;

        if (softDelete && !deleteHistory)
        {
            entity.ContentMeta.IsDeleted = true;
            entity.ContentMeta.IsPublished = false;
            // Detach so UpdateAsync can persist a brand-new version (it reassigns the key, which
            // EF disallows on a tracked entity).
            _dbContext.Entry(entity.ContentMeta).State = EntityState.Detached;
            _dbContext.Entry(entity).State = EntityState.Detached;
            return await UpdateAsync(entity, ct);
        }

        if(deleteHistory)
        {
            var historyItems = await _set
                .Where(e => e.ContentMeta.MasterId == entity.ContentMeta.MasterId)
                .ToListAsync(ct);

            if(softDelete)
            {
                foreach (var item in historyItems)
                {
                    item.ContentMeta.IsDeleted = true;
                    item.ContentMeta.IsPublished = false;
                }

                _dbContext.UpdateRange(historyItems);
                await _dbContext.SaveChangesAsync(ct);
                return true;
            }

            _set.RemoveRange(historyItems);
            _dbContext.RemoveRange(historyItems.Select(h => h.ContentMeta));
            await _dbContext.SaveChangesAsync(ct);
            return true;
        }

        _set.Remove(entity);
        _dbContext.Remove(entity.ContentMeta);
        await _dbContext.SaveChangesAsync(ct);
        return true;
    }
}
