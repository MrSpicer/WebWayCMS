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
            .Published()
            .Where(w => w.IsActive)
            .InDisplayOrder()
            .ToListAsync(ct);
    }

    public async Task<WidgetRegistrationDTO?> GetByComponentNameAsync(string componentName, CancellationToken ct = default)
    {
        return await _context.Set<WidgetRegistrationDTO>()
            .AsNoTracking()
            .Where(w => w.ComponentName == componentName && !w.Version.Node.IsDeleted)
            .OrderByDescending(w => w.Version.VersionNumber)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Dictionary<string, List<WidgetRegistrationDTO>>> GetActiveByCategoryAsync(CancellationToken ct = default)
    {
        var active = await GetActiveAsync(ct);
        return active.GroupByCategory();
    }
}
