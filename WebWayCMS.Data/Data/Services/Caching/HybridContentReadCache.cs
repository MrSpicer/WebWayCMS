using System.Collections.Concurrent;

using Microsoft.Extensions.Caching.Hybrid;

using Serilog;

namespace WebWayCMS.Data.Services.Caching;

/// <summary>
/// The <see cref="IContentReadCache"/> backed by <see cref="HybridCache"/>. Every key is prefixed
/// with the current generation, so a factory that races a publish lands under a dead key and is
/// evicted by the tag anyway. A type that fails to serialize is logged once and bypassed for the
/// process lifetime (a host DTO that doesn't round-trip must not break reads).
/// </summary>
public sealed class HybridContentReadCache : IContentReadCache
{
    /// <summary>The cache tag every entry carries; evicted wholesale on a generation change.</summary>
    public const string Tag = "cms-content";

    private readonly HybridCache _cache;
    private readonly ICmsCacheGeneration _generation;
    private readonly HybridCacheEntryOptions _entryOptions;
    private readonly ConcurrentDictionary<Type, byte> _bypassed = new();
    private readonly ILogger _logger = Log.ForContext<HybridContentReadCache>();

    public HybridContentReadCache(HybridCache cache, ICmsCacheGeneration generation, TimeSpan expiration)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _generation = generation ?? throw new ArgumentNullException(nameof(generation));
        _entryOptions = new HybridCacheEntryOptions
        {
            Expiration = expiration,
            LocalCacheExpiration = expiration,
        };
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory, CancellationToken ct = default)
    {
        var type = typeof(T);
        if (_bypassed.ContainsKey(type))
            return await factory(ct);

        try
        {
            // The key carries the generation: entries outlive their generation only until the tag
            // eviction, and a render racing a publish can never be served under the new generation.
            return await _cache.GetOrCreateAsync(
                $"cms:{_generation.Key}:{key}",
                ct2 => GuardedFactory(factory, ct2),
                _entryOptions,
                new[] { Tag },
                ct);
        }
        catch (FactoryFaultedException faulted)
        {
            // The factory's own failure: propagate it untouched (original stack), leave the type eligible.
            // Awaiting a faulted task rethrows through ExceptionDispatchInfo, keeping the query's stack.
            return await Task.FromException<T>(faulted.Inner);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller gave up (an aborted request): not a cache failure, the type stays eligible.
            throw;
        }
        catch (Exception ex)
        {
            // A cache-side failure (serialization of a type that doesn't round-trip): bypass it.
            if (_bypassed.TryAdd(type, 0))
                _logger.Warning(ex, "Content read cache bypassed for {Type}: {Message}", type.FullName, ex.Message);
            return await factory(ct);
        }
    }

    private static async ValueTask<T> GuardedFactory<T>(Func<CancellationToken, ValueTask<T>> factory, CancellationToken ct)
    {
        try
        {
            return await factory(ct);
        }
        catch (Exception ex)
        {
            throw new FactoryFaultedException(ex);
        }
    }

    /// <summary>Wraps a factory exception so the caller can tell it apart from a cache-side failure.</summary>
    private sealed class FactoryFaultedException(Exception inner) : Exception
    {
        public Exception Inner { get; } = inner;
    }
}
