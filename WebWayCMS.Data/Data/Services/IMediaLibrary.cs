using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// The media catalog: deduplicating adds, metadata reads, and byte reads delegated to the
/// configured <see cref="IMediaBlobStore"/>. Callers work in terms of content hashes.
/// </summary>
public interface IMediaLibrary
{
    /// <summary>
    /// Stores bytes and catalogs them, returning the content hash. Bytes already present are not
    /// stored twice — the existing hash is returned instead.
    /// </summary>
    Task<(bool Success, string? ErrorMessage, MediaBlobDTO? Blob)> AddAsync(
        byte[] bytes, string contentType, string originalFileName, int width, int height, CancellationToken ct = default);

    Task<MediaBlobDTO?> GetMetadataAsync(string hash, CancellationToken ct = default);

    Task<List<MediaBlobDTO>> ListAsync(int skip = 0, int take = 100, CancellationToken ct = default);

    Task<byte[]?> GetBytesAsync(string hash, CancellationToken ct = default);

    /// <summary>Removes a blob and its catalog row. Refused while any image version references it.</summary>
    Task<(bool Success, string? ErrorMessage)> DeleteAsync(string hash, CancellationToken ct = default);

    /// <summary>Computes the lowercase hex SHA-256 used as a blob's content address.</summary>
    string ComputeHash(byte[] bytes);
}
