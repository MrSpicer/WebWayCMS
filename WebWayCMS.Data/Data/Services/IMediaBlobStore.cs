namespace WebWayCMS.Data.Services;

/// <summary>
/// The storage medium for media bytes, and the seam that keeps that medium swappable.
/// Implementations deal only in bytes keyed by content hash; catalog metadata is always kept in
/// the database by <see cref="IMediaLibrary"/>, so swapping the store never loses the catalog.
/// </summary>
/// <remarks>
/// The shipped default is <see cref="MediaBlobStore"/> (Postgres <c>bytea</c>). A filesystem or
/// cloud implementation registers itself through <c>IWebWayCmsBuilder.AddMediaStore</c>.
/// Blobs are content-addressed, so a stored hash is immutable: writing the same hash twice is a
/// no-op rather than an update.
/// </remarks>
public interface IMediaBlobStore
{
    /// <summary>Returns the stored bytes, or <c>null</c> when the hash is not present.</summary>
    Task<byte[]?> GetAsync(string hash, CancellationToken ct = default);

    Task<bool> ExistsAsync(string hash, CancellationToken ct = default);

    /// <summary>Stores bytes under the given hash. Storing an existing hash succeeds as a no-op.</summary>
    Task<(bool Success, string? ErrorMessage)> PutAsync(string hash, byte[] bytes, CancellationToken ct = default);

    /// <summary>Removes the bytes. Returns <c>false</c> when the hash was not present.</summary>
    Task<bool> DeleteAsync(string hash, CancellationToken ct = default);
}
