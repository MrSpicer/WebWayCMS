using WebWayCMS.Data.Services.Caching;

namespace WebWayCMS.Data.Tests;

/// <summary>
/// In-memory <see cref="IContentReadCache"/> double: records its keys and how often the factory ran,
/// so the store/zone-service tests can assert on cache hits and bypasses without a real cache.
/// </summary>
internal sealed class FakeContentReadCache : IContentReadCache
{
    private readonly Dictionary<string, Box> _entries = new();
    public int FactoryCalls { get; private set; }
    public List<string> Keys { get; } = new();
    public List<CancellationToken> Tokens { get; } = new();

    public async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory, CancellationToken ct = default)
    {
        lock (_entries)
        {
            Tokens.Add(ct);
            if (_entries.TryGetValue(key, out var box))
                return (T)box.Value!;
            Keys.Add(key);
        }
        var value = await factory(ct);
        lock (_entries)
        {
            _entries[key] = new Box(value);
        }
        FactoryCalls++;
        return value;
    }

    private sealed record Box(object? Value);
}
