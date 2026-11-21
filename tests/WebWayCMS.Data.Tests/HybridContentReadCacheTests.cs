using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

using WebWayCMS.Data.Services.Caching;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class HybridContentReadCacheTests
{
    private HybridCache _cache = null!;
    private CmsCacheGeneration _generation = null!;
    private HybridContentReadCache _sut = null!;
    private ServiceProvider _services = null!;

    [SetUp]
    public void SetUp()
    {
        _services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        _cache = _services.GetRequiredService<HybridCache>();
        _generation = new CmsCacheGeneration();
        _sut = new HybridContentReadCache(_cache, _generation, TimeSpan.FromMinutes(10));
    }

    [TearDown]
    public void TearDown() => _services.Dispose();

    [Test]
    public async Task GetOrCreateAsync_MissRunsFactory_ThenHits()
    {
        int calls = 0;
        var value = await _sut.GetOrCreateAsync("k", _ =>
        {
            calls++;
            return new ValueTask<string>("v1");
        });

        var hit = await _sut.GetOrCreateAsync("k", _ =>
        {
            calls++;
            return new ValueTask<string>("v2");
        });

        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo("v1"));
            Assert.That(hit, Is.EqualTo("v1"));
            Assert.That(calls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GetOrCreateAsync_NullValue_IsCached()
    {
        int calls = 0;
        Func<CancellationToken, ValueTask<string?>> factory = _ =>
        {
            calls++;
            return calls == 1 ? ValueTask.FromResult<string?>(null) : new ValueTask<string?>("late");
        };
        var miss = await _sut.GetOrCreateAsync("k", factory);
        var hit = await _sut.GetOrCreateAsync("k", factory);

        Assert.Multiple(() =>
        {
            Assert.That(miss, Is.Null);
            Assert.That(hit, Is.Null);
            Assert.That(calls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GetOrCreateAsync_GenerationMoves_OldKeyIsDead()
    {
        int calls = 0;
        Func<CancellationToken, ValueTask<string>> factory = _ =>
        {
            calls++;
            return new ValueTask<string>($"v{calls}");
        };

        _ = await _sut.GetOrCreateAsync("k", factory);
        _generation.Advance(1); // a publish: entries under the old generation are stale

        var afterBump = await _sut.GetOrCreateAsync("k", factory);

        Assert.Multiple(() =>
        {
            Assert.That(afterBump, Is.EqualTo("v2"), "the pre-bump entry must not be served under the new generation");
            Assert.That(calls, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task GetOrCreateAsync_TagEviction_DropsAllEntries()
    {
        int calls = 0;
        Func<CancellationToken, ValueTask<string>> factory = _ =>
        {
            calls++;
            return new ValueTask<string>($"v{calls}");
        };

        _ = await _sut.GetOrCreateAsync("k1", factory);
        _ = await _sut.GetOrCreateAsync("k2", factory);
        await _cache.RemoveByTagAsync(HybridContentReadCache.Tag, default);

        _ = await _sut.GetOrCreateAsync("k1", factory);
        _ = await _sut.GetOrCreateAsync("k2", factory);

        Assert.That(calls, Is.EqualTo(4));
    }

    [Test]
    public async Task GetOrCreateAsync_SerializationFails_BypassesTypeAndSurvives()
    {
        int calls = 0;
        Func<CancellationToken, ValueTask<Circular>> factory = _ =>
        {
            calls++;
            var node = new Circular();
            node.Next = node;
            return new ValueTask<Circular>(node);
        };

        // First call: the factory runs, HybridCache throws serializing the cycle, the wrapper
        // re-runs the factory and bypasses the type from then on.
        var first = await _sut.GetOrCreateAsync("k", factory);
        // Second call: bypassed straight — one factory run, no exception.
        var second = await _sut.GetOrCreateAsync("k", factory);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(calls, Is.EqualTo(3), "factory ran twice on the failed call, once on the bypassed call");
        });
    }

    [Test]
    public async Task GetOrCreateAsync_FactoryFails_PropagatesAndKeepsTypeEligible()
    {
        int calls = 0;
        Func<CancellationToken, ValueTask<string>> failing = _ =>
        {
            calls++;
            throw new InvalidOperationException("db down");
        };

        Assert.ThrowsAsync<InvalidOperationException>(() => _sut.GetOrCreateAsync("k", failing));

        // The type stays eligible: a later success is cached.
        var value = await _sut.GetOrCreateAsync("k", _ =>
        {
            calls++;
            return new ValueTask<string>("ok");
        });
        var hit = await _sut.GetOrCreateAsync("k", _ =>
        {
            calls++;
            return new ValueTask<string>("late");
        });

        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo("ok"));
            Assert.That(hit, Is.EqualTo("ok"));
            Assert.That(calls, Is.EqualTo(2));
        });
    }

    [Test]
    public void GetOrCreateAsync_FactoryFails_KeepsTheFactorysStackTrace()
    {
        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.GetOrCreateAsync<string>("k", FailingLoad));

        Assert.That(ex!.StackTrace, Does.Contain(nameof(FailingLoad)));
    }

    private static ValueTask<string> FailingLoad(CancellationToken ct) => throw new InvalidOperationException("db down");

    [Test]
    public async Task GetOrCreateAsync_CallerCanceled_PropagatesAndKeepsTypeEligible()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sut = new HybridContentReadCache(new ThrowingHybridCache(new OperationCanceledException(cts.Token)), _generation, TimeSpan.FromMinutes(1));
        var calls = 0;

        Assert.ThrowsAsync<OperationCanceledException>(() => sut.GetOrCreateAsync("k", _ =>
        {
            calls++;
            return new ValueTask<string>("v");
        }, cts.Token));

        // Not bypassed: the next read goes to the cache again (which throws again here), it does not
        // short-circuit to the factory.
        Assert.ThrowsAsync<OperationCanceledException>(() => sut.GetOrCreateAsync("k", _ =>
        {
            calls++;
            return new ValueTask<string>("v");
        }, cts.Token));
        Assert.That(calls, Is.Zero);
        await Task.CompletedTask;
    }

    [Test]
    public async Task GetOrCreateAsync_CacheSideCancellation_WithoutCallerCancel_Bypasses()
    {
        var sut = new HybridContentReadCache(new ThrowingHybridCache(new OperationCanceledException()), _generation, TimeSpan.FromMinutes(1));

        var value = await sut.GetOrCreateAsync("k", _ => new ValueTask<string>("v"));

        Assert.That(value, Is.EqualTo("v"));
    }

    /// <summary>A <see cref="HybridCache"/> whose read always throws the given exception.</summary>
    private sealed class ThrowingHybridCache(Exception exception) : HybridCache
    {
        public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state,
            Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
            => ValueTask.FromException<T>(exception);

        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) => default;

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) => default;

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) => default;
    }

    [Test]
    public void Ctor_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(
                () => new HybridContentReadCache(null!, _generation, TimeSpan.FromSeconds(1)));
            Assert.Throws<ArgumentNullException>(
                () => new HybridContentReadCache(_cache, null!, TimeSpan.FromSeconds(1)));
        });
    }

    private sealed class Circular
    {
        public Circular? Next { get; set; }
    }
}
