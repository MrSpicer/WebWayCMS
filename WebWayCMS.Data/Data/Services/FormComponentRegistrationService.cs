using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// Manages form component registrations. Registrations are plain rows (no version history) — they are
/// seeded and re-synced from code by <c>CmsFormComponentSeeder</c>, and edited in place via the admin.
/// </summary>
public sealed class FormComponentRegistrationService : IFormComponentRegistrationService
{
    private readonly CmsDbContext _context;

    public FormComponentRegistrationService(CmsDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<List<FormComponentRegistrationDTO>> GetActiveAsync(CancellationToken ct = default)
    {
        return await OrderedQuery()
            .Where(f => f.IsActive)
            .ToListAsync(ct);
    }

    public async Task<List<FormComponentRegistrationDTO>> GetAllAsync(CancellationToken ct = default)
    {
        return await OrderedQuery().ToListAsync(ct);
    }

    public async Task<FormComponentRegistrationDTO?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Set<FormComponentRegistrationDTO>()
            .FirstOrDefaultAsync(f => f.Id == id, ct);
    }

    public async Task<FormComponentRegistrationDTO?> GetByComponentNameAsync(string componentName, CancellationToken ct = default)
    {
        return await _context.Set<FormComponentRegistrationDTO>()
            .FirstOrDefaultAsync(f => f.ComponentName == componentName, ct);
    }

    public async Task<Dictionary<string, List<FormComponentRegistrationDTO>>> GetActiveByCategoryAsync(CancellationToken ct = default)
    {
        var active = await GetActiveAsync(ct);
        return active
            .GroupBy(f => f.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<(bool Success, string? ErrorMessage, FormComponentRegistrationDTO? Registration)> UpsertAsync(
        FormComponentRegistrationDTO registration, CancellationToken ct = default)
    {
        if (registration == null) throw new ArgumentNullException(nameof(registration));

        var nameTaken = await _context.Set<FormComponentRegistrationDTO>()
            .Where(r => r.ComponentName == registration.ComponentName && r.Id != registration.Id)
            .AnyAsync(ct);

        if (nameTaken)
            return (false, "A form component with this name already exists.", null);

        if (registration.Id == Guid.Empty)
        {
            registration.Id = Guid.NewGuid();
            _context.Set<FormComponentRegistrationDTO>().Add(registration);
        }
        else
        {
            var existing = await _context.Set<FormComponentRegistrationDTO>()
                .FirstOrDefaultAsync(r => r.Id == registration.Id, ct);
            if (existing == null)
                return (false, "Form component registration not found.", null);

            _context.Entry(existing).CurrentValues.SetValues(registration);
        }

        await _context.SaveChangesAsync(ct);
        return (true, null, registration);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _context.Set<FormComponentRegistrationDTO>()
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (entity == null) return false;

        _context.Set<FormComponentRegistrationDTO>().Remove(entity);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    private IQueryable<FormComponentRegistrationDTO> OrderedQuery()
        => _context.Set<FormComponentRegistrationDTO>()
            .AsNoTracking()
            .OrderBy(f => f.Category)
            .ThenBy(f => f.Order)
            .ThenBy(f => f.DisplayName);
}
