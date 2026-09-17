using System.Security.Cryptography;

using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// Catalogs media blobs and orchestrates the configured <see cref="IMediaBlobStore"/>.
/// Content-addressing is what makes this deduplicating: identical bytes always produce the same
/// hash, so re-uploading a file already in the library costs one row lookup and no new storage.
/// </summary>
public sealed class MediaLibrary : IMediaLibrary
{
    internal const string ReferencedMessage = "This image is still used by one or more content items.";

    private readonly CmsDbContext _context;
    private readonly IMediaBlobStore _blobStore;

    public MediaLibrary(CmsDbContext context, IMediaBlobStore blobStore)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _blobStore = blobStore ?? throw new ArgumentNullException(nameof(blobStore));
    }

    public string ComputeHash(byte[] bytes)
    {
        if (bytes == null) throw new ArgumentNullException(nameof(bytes));

        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public async Task<(bool Success, string? ErrorMessage, MediaBlobDTO? Blob)> AddAsync(
        byte[] bytes, string contentType, string originalFileName, int width, int height, CancellationToken ct = default)
    {
        if (bytes == null || bytes.Length == 0) return (false, "A media file cannot be empty.", null);
        if (string.IsNullOrWhiteSpace(contentType)) return (false, "A media content type is required.", null);

        var hash = ComputeHash(bytes);

        var existing = await _context.Set<MediaBlobDTO>()
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Hash == hash, ct);

        if (existing != null)
            return (true, null, existing);

        var put = await _blobStore.PutAsync(hash, bytes, ct);
        if (!put.Success)
            return (false, put.ErrorMessage, null);

        var blob = new MediaBlobDTO
        {
            Hash = hash,
            ContentType = contentType,
            ByteLength = bytes.LongLength,
            Width = width,
            Height = height,
            OriginalFileName = originalFileName ?? string.Empty,
            CreatedUtc = DateTime.UtcNow
        };

        _context.Set<MediaBlobDTO>().Add(blob);
        await _context.SaveChangesAsync(ct);

        return (true, null, blob);
    }

    public Task<MediaBlobDTO?> GetMetadataAsync(string hash, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(hash)
            ? Task.FromResult<MediaBlobDTO?>(null)
            : _context.Set<MediaBlobDTO>().AsNoTracking().FirstOrDefaultAsync(b => b.Hash == hash, ct);

    public Task<List<MediaBlobDTO>> ListAsync(int skip = 0, int take = 100, CancellationToken ct = default) =>
        _context.Set<MediaBlobDTO>()
            .AsNoTracking()
            .OrderByDescending(b => b.CreatedUtc)
            .Skip(skip < 0 ? 0 : skip)
            .Take(take < 1 ? 1 : take)
            .ToListAsync(ct);

    public Task<byte[]?> GetBytesAsync(string hash, CancellationToken ct = default) =>
        _blobStore.GetAsync(hash, ct);

    public async Task<(bool Success, string? ErrorMessage)> DeleteAsync(string hash, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hash)) return (false, "A media blob hash is required.");

        // Any version referencing the blob counts, not just the published one — an editor can still
        // restore an older version, and that version's image must not have been deleted underneath it.
        if (await _context.Set<ImageDTO>().AnyAsync(i => i.BlobHash == hash, ct))
            return (false, ReferencedMessage);

        var blob = await _context.Set<MediaBlobDTO>().FirstOrDefaultAsync(b => b.Hash == hash, ct);
        if (blob == null) return (false, "That image no longer exists.");

        await _blobStore.DeleteAsync(hash, ct);

        _context.Set<MediaBlobDTO>().Remove(blob);
        await _context.SaveChangesAsync(ct);

        return (true, null);
    }
}
