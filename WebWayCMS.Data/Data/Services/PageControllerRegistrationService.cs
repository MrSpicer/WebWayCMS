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
            .Published()
            .Where(p => p.IsActive)
            .InDisplayOrder()
            .ToListAsync(ct);
    }

    public async Task<PageControllerRegistrationDTO?> GetByControllerNameAsync(string controllerName, CancellationToken ct = default)
    {
        return await _context.Set<PageControllerRegistrationDTO>()
            .AsNoTracking()
            .Where(p => p.ControllerName == controllerName && !p.Version.Node.IsDeleted)
            .OrderByDescending(p => p.Version.VersionNumber)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Dictionary<string, List<PageControllerRegistrationDTO>>> GetActiveByCategoryAsync(CancellationToken ct = default)
    {
        var active = await GetActiveAsync(ct);
        return active.GroupByCategory();
    }
}
