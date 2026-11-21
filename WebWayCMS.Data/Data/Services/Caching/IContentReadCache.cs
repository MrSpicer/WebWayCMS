namespace WebWayCMS.Data.Services.Caching;

/// <summary>
/// Caches published-mode content reads (the store's read-context methods and the zone service's
/// zone/item reads) so a public render stops paying the DB round trips. Keys are prefixed with the
/// cross-host generation by the implementation, and every entry is evicted by tag on a generation
/// change. Implemented by <c>HybridContentReadCache</c> (HybridCache) and by
/// <see cref="NullContentReadCache"/> when caching is disabled.
/// </summary>
public interface IContentReadCache
{
    /// <summary>
    /// Returns the cached value for <paramref name="key"/> or runs <paramref name="factory"/>, caches
    /// and returns its result. The factory must not throw past here (callers treat a failure as a
    /// bypass, not an error).
    /// </summary>
    Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory, CancellationToken ct = default);
}
