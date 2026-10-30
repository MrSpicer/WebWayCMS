using Microsoft.Extensions.Caching.Memory;

namespace WebWayCMS.Mcp;

/// <summary>
/// Counts failed MCP token authentications per client and blocks a client after
/// <see cref="MaxFailures"/> within <see cref="Window"/> (fixed window from the first failure).
/// Only failures count and only failures are blocked (the handler checks after a token fails to
/// validate), so a valid agent, even one sharing an address with a failing client, is never throttled.
/// </summary>
public sealed class McpAuthFailureThrottle
{
    public const int MaxFailures = 10;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private const string KeyPrefix = "WebWayCMS:McpAuthFailures:";

    private readonly IMemoryCache _cache;

    // GetOrCreate isn't atomic: two concurrent first failures could each cache their own counter and lose counts.
    private readonly Lock _gate = new();

    public McpAuthFailureThrottle(IMemoryCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <summary>Whether <paramref name="clientKey"/> has used up its failures. A client with no key is never blocked.</summary>
    public bool IsBlocked(string? clientKey) =>
        clientKey != null
        && _cache.TryGetValue(KeyPrefix + clientKey, out Counter? counter)
        && Volatile.Read(ref counter!.Count) >= MaxFailures;

    /// <summary>Records one failed authentication for <paramref name="clientKey"/>.</summary>
    public void RecordFailure(string? clientKey)
    {
        if (clientKey == null) return;

        Counter counter;
        lock (_gate)
        {
            counter = _cache.GetOrCreate(KeyPrefix + clientKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = Window;
                return new Counter();
            })!;
        }
        Interlocked.Increment(ref counter.Count);
    }

    private sealed class Counter
    {
        public int Count;
    }
}
