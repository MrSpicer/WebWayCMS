using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

public sealed class FormComponentRegistrationService : IFormComponentRegistrationService
{
    private readonly CmsDbContext _context;

    public FormComponentRegistrationService(CmsDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<List<FormComponentRegistrationDTO>> GetActiveAsync(CancellationToken ct = default)
    {
        return await _context.Set<FormComponentRegistrationDTO>()
            .AsNoTracking()
            .Where(f => f.IsActive
                && f.Version.State == ContentVersionState.Published
                && !f.Version.Node.IsDeleted)
            .OrderBy(f => f.Category)
            .ThenBy(f => f.Order)
            .ThenBy(f => f.DisplayName)
            .ToListAsync(ct);
    }

    public async Task<FormComponentRegistrationDTO?> GetByComponentNameAsync(string componentName, CancellationToken ct = default)
    {
        return await _context.Set<FormComponentRegistrationDTO>()
            .AsNoTracking()
            .Where(f => f.ComponentName == componentName && !f.Version.Node.IsDeleted)
            .OrderByDescending(f => f.Version.VersionNumber)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Dictionary<string, List<FormComponentRegistrationDTO>>> GetActiveByCategoryAsync(CancellationToken ct = default)
    {
        var active = await GetActiveAsync(ct);
        return active
            .GroupBy(f => f.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
    }
}
