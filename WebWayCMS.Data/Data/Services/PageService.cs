using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

public sealed class PageService : IPageService
{
    private readonly CmsDbContext _context;

    public PageService(CmsDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<List<PageDTO>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Set<PageDTO>()
            .AsNoTracking()
            .Where(p => !p.ContentMeta.IsDeleted
                && !_context.Set<PageDTO>().Any(p2 =>
                    p2.ContentMeta.MasterId == p.ContentMeta.MasterId
                    && p2.ContentMeta.Version > p.ContentMeta.Version))
            .OrderBy(p => p.ContentMeta.Title)
            .ToListAsync(ct);
    }

    public async Task<PageDTO?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Set<PageDTO>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ContentId == id, ct);
    }

    public async Task<List<PageDTO>> GetAllVersionsAsync(Guid masterId, CancellationToken ct = default)
        => await _context.Set<PageDTO>()
            .AsNoTracking()
            .Where(p => p.ContentMeta.MasterId == masterId)
            .OrderByDescending(p => p.ContentMeta.Version)
            .ToListAsync(ct);

    public async Task<PageDTO> CreateAsync(PageDTO page, CancellationToken ct = default)
    {
        if (page == null) throw new ArgumentNullException(nameof(page));

        var meta = page.ContentMeta;

        if (meta.Id == Guid.Empty)
            meta.Id = Guid.NewGuid();

        page.ContentId = meta.Id;
        meta.MasterId = meta.Id;
        meta.Version = 0;

        var now = DateTime.UtcNow;
        meta.CreationDate = now;
        meta.ModificationDate = now;
        if (meta.PublicationDate == default)
            meta.PublicationDate = now;

        _context.Set<PageDTO>().Add(page);
        await _context.SaveChangesAsync(ct);
        return page;
    }

    public async Task<bool> UpdateAsync(PageDTO page, CancellationToken ct = default)
    {
        if (page == null) throw new ArgumentNullException(nameof(page));

        var meta = page.ContentMeta;
        if (!await _context.Set<PageDTO>().AnyAsync(p => p.ContentId == page.ContentId, ct))
            return false;

        meta.Version++;
        meta.Id = Guid.NewGuid();
        page.ContentId = meta.Id;
        meta.ModificationDate = DateTime.UtcNow;
        if (meta.IsPublished && meta.PublicationDate == default)
            meta.PublicationDate = DateTime.UtcNow;

        if (meta.IsPublished)
        {
            var previousPublished = await _context.Set<PageDTO>()
                .Where(p => p.ContentMeta.MasterId == meta.MasterId && p.ContentMeta.IsPublished)
                .ToListAsync(ct);
            foreach (var prev in previousPublished)
                prev.ContentMeta.IsPublished = false;
            _context.Set<PageDTO>().UpdateRange(previousPublished);
        }

        _context.Set<PageDTO>().Add(page);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _context.Set<PageDTO>().FirstOrDefaultAsync(p => p.ContentId == id, ct);
        if (entity == null) return false;

        var allVersions = await _context.Set<PageDTO>()
            .Where(p => p.ContentMeta.MasterId == entity.ContentMeta.MasterId)
            .ToListAsync(ct);

        _context.Set<PageDTO>().RemoveRange(allVersions);
        _context.RemoveRange(allVersions.Select(v => v.ContentMeta));
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _context.Set<PageDTO>().FirstOrDefaultAsync(p => p.ContentId == id, ct);
        if (entity == null) return false;
        _context.Set<PageDTO>().Remove(entity);
        _context.Remove(entity.ContentMeta);
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
