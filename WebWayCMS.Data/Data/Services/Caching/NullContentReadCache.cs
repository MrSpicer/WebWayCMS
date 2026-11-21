namespace WebWayCMS.Data.Services.Caching;

/// <summary>
/// The <see cref="IContentReadCache"/> used when <c>Caching:Enabled</c> is false: every read runs its
/// factory, nothing is stored.
/// </summary>
public sealed class NullContentReadCache : IContentReadCache
{
    public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory, CancellationToken ct = default)
        => factory(ct).AsTask();
}
