using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// Reads and writes the JSON content seed ledger. Rows are plain records (no version history) —
/// they are written by <c>JsonContentSeeder</c> and read back on every boot.
/// </summary>
public sealed class ContentSeedRecordService : IContentSeedRecordService
{
    private readonly CmsDbContext _context;

    public ContentSeedRecordService(CmsDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<ContentSeedRecordDTO?> GetAsync(Guid seedId, CancellationToken ct = default)
    {
        return await _context.Set<ContentSeedRecordDTO>()
            .FirstOrDefaultAsync(r => r.SeedId == seedId, ct);
    }

    public async Task UpsertAsync(ContentSeedRecordDTO record, CancellationToken ct = default)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));

        var existing = await _context.Set<ContentSeedRecordDTO>()
            .FirstOrDefaultAsync(r => r.SeedId == record.SeedId, ct);

        if (existing == null)
        {
            _context.Set<ContentSeedRecordDTO>().Add(record);
        }
        else
        {
            _context.Entry(existing).CurrentValues.SetValues(record);
        }

        await _context.SaveChangesAsync(ct);
    }
}
