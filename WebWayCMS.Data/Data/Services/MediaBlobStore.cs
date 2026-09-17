using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// The default <see cref="IMediaBlobStore"/>: bytes in a Postgres <c>bytea</c> column, in their
/// own table so catalog queries never materialize them.
/// </summary>
/// <remarks>
/// Chosen as the default because it is the only medium that works unchanged across the
/// admin/rendering-only split — those hosts are guaranteed a shared database, never a shared
/// filesystem — and because it keeps media inside the same backup and restore story as content.
/// </remarks>
public sealed class MediaBlobStore : IMediaBlobStore
{
    private readonly CmsDbContext _context;

    public MediaBlobStore(CmsDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<byte[]?> GetAsync(string hash, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hash)) return null;

        var row = await _context.Set<MediaBlobBytesDTO>()
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Hash == hash, ct);

        return row?.Bytes;
    }

    public async Task<bool> ExistsAsync(string hash, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hash)) return false;

        return await _context.Set<MediaBlobBytesDTO>().AnyAsync(b => b.Hash == hash, ct);
    }

    public async Task<(bool Success, string? ErrorMessage)> PutAsync(string hash, byte[] bytes, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hash)) return (false, "A media blob hash is required.");
        if (bytes == null || bytes.Length == 0) return (false, "A media blob cannot be empty.");

        // Content-addressed: the same hash always means the same bytes, so a repeat write is a no-op.
        if (await _context.Set<MediaBlobBytesDTO>().AnyAsync(b => b.Hash == hash, ct))
            return (true, null);

        _context.Set<MediaBlobBytesDTO>().Add(new MediaBlobBytesDTO { Hash = hash, Bytes = bytes });
        await _context.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<bool> DeleteAsync(string hash, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hash)) return false;

        var row = await _context.Set<MediaBlobBytesDTO>().FirstOrDefaultAsync(b => b.Hash == hash, ct);
        if (row == null) return false;

        _context.Set<MediaBlobBytesDTO>().Remove(row);
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
