using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

public sealed class WidgetRegistrationService : IWidgetRegistrationService
{
    private readonly CmsDbContext _context;

    public WidgetRegistrationService(CmsDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<List<WidgetRegistrationDTO>> GetActiveAsync(CancellationToken ct = default)
    {
        return await _context.Set<WidgetRegistrationDTO>()
            .AsNoTracking()
            .Where(w => w.IsActive
                && w.ContentMeta.IsPublished
                && !w.ContentMeta.IsDeleted
                && !_context.Set<WidgetRegistrationDTO>().Any(w2 =>
                    w2.ContentMeta.MasterId == w.ContentMeta.MasterId
                    && w2.ContentMeta.Version > w.ContentMeta.Version))
            .OrderBy(w => w.Category)
            .ThenBy(w => w.Order)
            .ThenBy(w => w.DisplayName)
            .ToListAsync(ct);
    }

    public async Task<WidgetRegistrationDTO?> GetByComponentNameAsync(string componentName, CancellationToken ct = default)
    {
        return await _context.Set<WidgetRegistrationDTO>()
            .AsNoTracking()
            .Where(w => w.ComponentName == componentName
                && !w.ContentMeta.IsDeleted
                && !_context.Set<WidgetRegistrationDTO>().Any(w2 =>
                    w2.ContentMeta.MasterId == w.ContentMeta.MasterId
                    && w2.ContentMeta.Version > w.ContentMeta.Version))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Dictionary<string, List<WidgetRegistrationDTO>>> GetActiveByCategoryAsync(CancellationToken ct = default)
    {
        var active = await GetActiveAsync(ct);
        return active
            .GroupBy(w => w.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
    }
}
