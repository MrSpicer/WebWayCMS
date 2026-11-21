using NUnit.Framework;

using WebWayCMS.Data.Services.Caching;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class NullContentReadCacheTests
{
    [Test]
    public async Task GetOrCreateAsync_RunsFactoryEveryTime_AndPassesTheValueThrough()
    {
        var cache = new NullContentReadCache();
        var calls = 0;
        Func<CancellationToken, ValueTask<string>> factory = _ =>
        {
            calls++;
            return new ValueTask<string>($"v{calls}");
        };

        var first = await cache.GetOrCreateAsync("k", factory);
        var second = await cache.GetOrCreateAsync("k", factory);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo("v1"));
            Assert.That(second, Is.EqualTo("v2"), "nothing is stored: every call runs the factory");
            Assert.That(calls, Is.EqualTo(2));
        });
    }
}
