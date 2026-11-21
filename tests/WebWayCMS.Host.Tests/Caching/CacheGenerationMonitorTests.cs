using System.Diagnostics;

using Microsoft.Extensions.Options;
using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Caching;
using WebWayCMS.Data.Services.Caching;

namespace WebWayCMS.Host.Tests.Caching;

[TestFixture]
public class CacheGenerationMonitorTests
{
    private CmsCacheGeneration _generation = null!;

    [SetUp]
    public void SetUp()
    {
        _generation = new CmsCacheGeneration();
    }

    /// <summary>
    /// Plays back a fixed script of reads: the first call gets the first entry, every later call
    /// repeats the last entry (polling continues forever after the script runs out).
    /// </summary>
    private sealed class ScriptedStore : ICacheGenerationStore
    {
        private readonly Func<Task<long>>[] _script;
        private int _calls;

        public ScriptedStore(params Func<Task<long>>[] script) => _script = script;

        public int CallCount => Volatile.Read(ref _calls);

        public Task<long> ReadAsync(CancellationToken ct = default)
        {
            var call = Interlocked.Increment(ref _calls);
            var index = Math.Min(call - 1, _script.Length - 1);
            return _script[index]();
        }

        public Task<(bool Success, long? Value, string? ErrorMessage)> BumpAsync(CancellationToken ct = default) =>
            Task.FromResult((Success: true, Value: (long?)1L, ErrorMessage: (string?)null));
    }

    private static IOptions<CacheOptions> OptionsFor(TimeSpan interval) =>
        Options.Create(new CacheOptions { GenerationPollInterval = interval });

    private sealed class TestableMonitor : CacheGenerationMonitor
    {
        public TestableMonitor(CmsCacheGeneration generation, ICacheGenerationStore store, IOptions<CacheOptions> options)
            : base(generation, store, options)
        {
        }

        public Task Run(CancellationToken ct) => ExecuteAsync(ct);
    }

    private sealed class DisposableTimerMonitor : CacheGenerationMonitor
    {
        public DisposableTimerMonitor(CmsCacheGeneration generation, ICacheGenerationStore store, IOptions<CacheOptions> options)
            : base(generation, store, options)
        {
        }

        public PeriodicTimer? Timer { get; private set; }

        public Task Run(CancellationToken ct) => ExecuteAsync(ct);

        protected override PeriodicTimer CreateTimer(TimeSpan interval) => Timer = new(interval);
    }

    [Test]
    public void Ctor_NullArguments_Throw()
    {
        var options = OptionsFor(TimeSpan.FromSeconds(5));
        var store = Substitute.For<ICacheGenerationStore>();
        Assert.That(() => new CacheGenerationMonitor(null!, store, options), Throws.ArgumentNullException);
        Assert.That(() => new CacheGenerationMonitor(_generation, null!, options), Throws.ArgumentNullException);
        Assert.That(() => new CacheGenerationMonitor(_generation, store, null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task ExecuteAsync_ZeroInterval_ReturnsWithoutPolling()
    {
        var store = new ScriptedStore(() => Task.FromResult(1L));
        var monitor = new TestableMonitor(_generation, store, OptionsFor(TimeSpan.Zero));

        await monitor.Run(CancellationToken.None);

        Assert.That(store.CallCount, Is.EqualTo(0));
        Assert.That(_generation.Current, Is.EqualTo(0));
    }

    [Test]
    public async Task ExecuteAsync_PollsStore_AndAdvancesGeneration()
    {
        var store = new ScriptedStore(() => Task.FromResult(7L));
        var monitor = new TestableMonitor(_generation, store, OptionsFor(TimeSpan.FromMilliseconds(10)));
        var cts = new CancellationTokenSource();
        var run = monitor.Run(cts.Token);

        await WaitFor(() => _generation.Current >= 7);
        cts.Cancel();
        await run;

        Assert.That(_generation.Current, Is.EqualTo(7));
    }

    [Test]
    public async Task ExecuteAsync_FailingPollIsSkipped_AndPollingContinues()
    {
        var store = new ScriptedStore(
            () => Task.FromException<long>(new InvalidOperationException("db down")),
            () => Task.FromResult(9L));
        var monitor = new TestableMonitor(_generation, store, OptionsFor(TimeSpan.FromMilliseconds(10)));
        var cts = new CancellationTokenSource();
        var run = monitor.Run(cts.Token);

        await WaitFor(() => _generation.Current >= 9);
        cts.Cancel();
        await run;

        Assert.That(_generation.Current, Is.EqualTo(9));
    }

    [Test]
    public async Task ExecuteAsync_CanceledRead_Returns()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var store = new ScriptedStore(() => Task.FromCanceled<long>(canceled.Token));
        var monitor = new TestableMonitor(_generation, store, OptionsFor(TimeSpan.FromMilliseconds(10)));

        var run = monitor.Run(CancellationToken.None);

        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(_generation.Current, Is.EqualTo(0));
    }

    [Test]
    public async Task ExecuteAsync_TimerDisposed_StopsTheLoop()
    {
        var store = new ScriptedStore(() => Task.FromResult(7L));
        var monitor = new DisposableTimerMonitor(_generation, store, OptionsFor(TimeSpan.FromMilliseconds(10)));
        var run = monitor.Run(CancellationToken.None);

        await WaitFor(() => _generation.Current >= 7);
        monitor.Timer!.Dispose();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(_generation.Current, Is.EqualTo(7));
        // One or two reads may have happened before the dispose took effect; none after it.
        Assert.That(store.CallCount, Is.InRange(1, 2));
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)), "timed out waiting for condition");
            await Task.Delay(10);
        }
    }
}
