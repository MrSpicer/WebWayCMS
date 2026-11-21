using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Serilog;

using WebWayCMS.Data.Services.Caching;

namespace WebWayCMS.Caching;

/// <summary>
/// Polls the database generation stamp on <see cref="CacheOptions.GenerationPollInterval"/> and
/// advances the local generation past whatever it reads. In a split deployment (an admin host that
/// writes and rendering-only hosts that share the database) this is how a render host learns about
/// publishes made elsewhere — within one poll interval. A failing poll is logged and skipped: the
/// cache TTL is the backstop, so a transient database outage must not stop the host from serving.
/// </summary>
public class CacheGenerationMonitor : BackgroundService
{
    private readonly ICmsCacheGeneration _generation;
    private readonly ICacheGenerationStore _store;
    private readonly TimeSpan _interval;
    private readonly ILogger _logger = Log.ForContext<CacheGenerationMonitor>();

    public CacheGenerationMonitor(
        ICmsCacheGeneration generation,
        ICacheGenerationStore store,
        IOptions<CacheOptions> options)
    {
        _generation = generation ?? throw new ArgumentNullException(nameof(generation));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _interval = options?.Value.GenerationPollInterval ?? throw new ArgumentNullException(nameof(options));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_interval <= TimeSpan.Zero)
            return;

        using var timer = CreateTimer(_interval);
        while (true)
        {
            bool ticked;
            try
            {
                ticked = await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // The timer was disposed (the only way WaitForNextTickAsync returns false).
            if (!ticked)
                return;

            try
            {
                var value = await _store.ReadAsync(stoppingToken);
                _generation.Advance(value);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Cache generation poll failed; the cache TTL is the backstop");
            }
        }
    }

    protected virtual PeriodicTimer CreateTimer(TimeSpan interval) => new(interval);
}
