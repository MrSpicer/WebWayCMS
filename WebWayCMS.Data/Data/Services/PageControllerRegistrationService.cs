using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

public sealed class PageControllerRegistrationService : IPageControllerRegistrationService
{
    private readonly CmsDbContext _context;

    public PageControllerRegistrationService(CmsDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<List<PageControllerRegistrationDTO>> GetActiveAsync(CancellationToken ct = default)
    {
        return await _context.Set<PageControllerRegistrationDTO>()
            .AsNoTracking()
            .Where(p => p.IsActive
                && p.ContentMeta.IsPublished
                && !p.ContentMeta.IsDeleted
                && !_context.Set<PageControllerRegistrationDTO>().Any(p2 =>
                    p2.ContentMeta.MasterId == p.ContentMeta.MasterId
                    && p2.ContentMeta.Version > p.ContentMeta.Version))
            .OrderBy(p => p.Category)
            .ThenBy(p => p.Order)
            .ThenBy(p => p.DisplayName)
            .ToListAsync(ct);
    }

    public async Task<PageControllerRegistrationDTO?> GetByControllerNameAsync(string controllerName, CancellationToken ct = default)
    {
        return await _context.Set<PageControllerRegistrationDTO>()
            .AsNoTracking()
            .Where(p => p.ControllerName == controllerName
                && !p.ContentMeta.IsDeleted
                && !_context.Set<PageControllerRegistrationDTO>().Any(p2 =>
                    p2.ContentMeta.MasterId == p.ContentMeta.MasterId
                    && p2.ContentMeta.Version > p.ContentMeta.Version))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Dictionary<string, List<PageControllerRegistrationDTO>>> GetActiveByCategoryAsync(CancellationToken ct = default)
    {
        var active = await GetActiveAsync(ct);
        return active
            .GroupBy(p => p.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
    }
}
