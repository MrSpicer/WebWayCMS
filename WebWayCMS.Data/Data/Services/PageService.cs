using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// Service for managing dynamic pages with versioning support.
/// </summary>
public sealed class PageService : IPageService
{
    private readonly PageContext _context;

    public PageService(PageContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<List<PageDTO>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Pages
            .AsNoTracking()
            .Where(p => !p.ContentMeta.IsDeleted
                && !_context.Pages.Any(p2 => p2.ContentMeta.MasterId == p.ContentMeta.MasterId && p2.ContentMeta.Version > p.ContentMeta.Version))
            .OrderBy(p => p.Route)
            .ToListAsync(ct);
    }

    public async Task<PageDTO?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Pages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ContentId == id, ct);
    }

    public async Task<PageDTO?> GetByRouteAsync(string route, CancellationToken ct = default)
    {
        route = NormalizeRoute(route);

        return await _context.Pages
            .AsNoTracking()
            .Where(p => p.Route == route && !p.ContentMeta.IsDeleted && p.ContentMeta.IsPublished
                && !_context.Pages.Any(p2 => p2.ContentMeta.MasterId == p.ContentMeta.MasterId && p2.ContentMeta.Version > p.ContentMeta.Version))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<PageDTO>> GetAllVersionsAsync(Guid masterId, CancellationToken ct = default)
        => await _context.Pages
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
        page.Route = NormalizeRoute(page.Route);

        var now = DateTime.UtcNow;
        meta.CreationDate = now;
        meta.ModificationDate = now;
        if (meta.PublicationDate == default)
            meta.PublicationDate = now;

        _context.Pages.Add(page);
        await _context.SaveChangesAsync(ct);
        return page;
    }

    public async Task<bool> UpdateAsync(PageDTO page, CancellationToken ct = default)
    {
        if (page == null) throw new ArgumentNullException(nameof(page));

        var meta = page.ContentMeta;
        if (!await _context.Pages.AnyAsync(p => p.ContentId == page.ContentId, ct))
            return false;

        meta.Version++;
        meta.Id = Guid.NewGuid();
        page.ContentId = meta.Id;
        page.Route = NormalizeRoute(page.Route);
        meta.ModificationDate = DateTime.UtcNow;
        if (meta.IsPublished && meta.PublicationDate == default)
            meta.PublicationDate = DateTime.UtcNow;

        if (meta.IsPublished)
        {
            var previousPublished = await _context.Pages
                .Where(p => p.ContentMeta.MasterId == meta.MasterId && p.ContentMeta.IsPublished)
                .ToListAsync(ct);
            foreach (var prev in previousPublished)
                prev.ContentMeta.IsPublished = false;
            _context.Pages.UpdateRange(previousPublished);
        }

        _context.Pages.Add(page);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _context.Pages.FirstOrDefaultAsync(p => p.ContentId == id, ct);
        if (entity == null) return false;

        var allVersions = await _context.Pages
            .Where(p => p.ContentMeta.MasterId == entity.ContentMeta.MasterId)
            .ToListAsync(ct);

        _context.Pages.RemoveRange(allVersions);
        _context.RemoveRange(allVersions.Select(v => v.ContentMeta));
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _context.Pages.FirstOrDefaultAsync(p => p.ContentId == id, ct);
        if (entity == null) return false;
        _context.Pages.Remove(entity);
        _context.Remove(entity.ContentMeta);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> IsRouteAvailableAsync(string route, Guid? excludeMasterId = null, CancellationToken ct = default)
    {
        route = NormalizeRoute(route);

        var query = _context.Pages
            .Where(p => p.Route == route
                && !p.ContentMeta.IsDeleted
                && !_context.Pages.Any(p2 => p2.ContentMeta.MasterId == p.ContentMeta.MasterId && p2.ContentMeta.Version > p.ContentMeta.Version));

        if (excludeMasterId.HasValue)
            query = query.Where(p => p.ContentMeta.MasterId != excludeMasterId.Value);

        return !await query.AnyAsync(ct);
    }

    private static string NormalizeRoute(string route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return "/";

        route = route.Trim().ToLowerInvariant();

        // Ensure leading slash
        if (!route.StartsWith('/'))
            route = "/" + route;

        // Remove trailing slash (but keep root "/")
        if (route.Length > 1 && route.EndsWith('/'))
            route = route.TrimEnd('/');

        return route;
    }
}