using Microsoft.Extensions.Caching.Memory;

using NUnit.Framework;

namespace WebWayCMS.Mcp.Tests;

[TestFixture]
public class McpAuthFailureThrottleTests
{
    private MemoryCache _cache = null!;
    private McpAuthFailureThrottle _throttle = null!;

    [SetUp]
    public void SetUp()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
        _throttle = new McpAuthFailureThrottle(_cache);
    }

    [TearDown]
    public void TearDown() => _cache.Dispose();

    [Test]
    public void Constructor_NullCache_Throws() =>
        Assert.That(() => new McpAuthFailureThrottle(null!), Throws.ArgumentNullException);

    [Test]
    public void IsBlocked_AfterMaxFailures_BlocksOnlyThatClient()
    {
        for (var i = 0; i < McpAuthFailureThrottle.MaxFailures - 1; i++)
            _throttle.RecordFailure("10.0.0.1");

        var beforeLimit = _throttle.IsBlocked("10.0.0.1");
        _throttle.RecordFailure("10.0.0.1");

        Assert.Multiple(() =>
        {
            Assert.That(beforeLimit, Is.False);
            Assert.That(_throttle.IsBlocked("10.0.0.1"), Is.True);
            Assert.That(_throttle.IsBlocked("10.0.0.2"), Is.False, "another client keeps its own count");
        });
    }

    [Test]
    public void RecordFailure_ConcurrentFirstFailures_AreAllCounted()
    {
        // Repeated over fresh clients so a lost first-failure race would show up as an unblocked client.
        for (var round = 0; round < 50; round++)
        {
            var client = $"10.1.0.{round}";
            Parallel.For(0, McpAuthFailureThrottle.MaxFailures, new ParallelOptions { MaxDegreeOfParallelism = 8 },
                _ => _throttle.RecordFailure(client));

            Assert.That(_throttle.IsBlocked(client), Is.True, $"round {round}");
        }
    }

    [Test]
    public void NullClient_IsNeverCountedOrBlocked()
    {
        for (var i = 0; i < McpAuthFailureThrottle.MaxFailures * 2; i++)
            _throttle.RecordFailure(null);

        Assert.That(_throttle.IsBlocked(null), Is.False);
    }

    [Test]
    public void Window_IsOneMinute() =>
        Assert.That(McpAuthFailureThrottle.Window, Is.EqualTo(TimeSpan.FromMinutes(1)));
}
