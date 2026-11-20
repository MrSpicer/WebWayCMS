using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services.Registrations;

/// <summary>The form component registration table (not versioned: rows are updated in place).</summary>
public sealed class FormComponentRegistrationSyncTarget : IRegistrationSyncTarget<FormComponentRegistrationDTO>
{
    private readonly CmsDbContext _context;
    private readonly IFormComponentRegistrationService _service;

    public FormComponentRegistrationSyncTarget(CmsDbContext context, IFormComponentRegistrationService service)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public string KeyOf(FormComponentRegistrationDTO row) => row.ComponentName;

    public async Task<IReadOnlyList<FormComponentRegistrationDTO>> LoadRowsAsync(CancellationToken ct = default)
        => await _context.Set<FormComponentRegistrationDTO>().ToListAsync(ct);

    public async Task SaveCodeOwnedChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();
    }

    public async Task<(bool Success, string? ErrorMessage)> InsertAsync(FormComponentRegistrationDTO row, CancellationToken ct = default)
    {
        var (success, error, _) = await _service.UpsertAsync(row, ct);
        return (success, error);
    }
}
