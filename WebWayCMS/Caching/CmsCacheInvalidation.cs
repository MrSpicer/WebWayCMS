using Microsoft.AspNetCore.OutputCaching;

using Microsoft.Extensions.Caching.Hybrid;

using Serilog;

using WebWayCMS.ContentZones;
using WebWayCMS.Data.Services;
using WebWayCMS.Data.Services.Caching;
using WebWayCMS.Forms;
using WebWayCMS.Pages;

namespace WebWayCMS.Caching;

/// <summary>
/// The local half of cross-host invalidation: when the generation moves (this host's own write
/// through <see cref="PublicContentChangeInterceptor"/>, or another host's write picked up by
/// <see cref="CacheGenerationMonitor"/>), evict the output cache by <see cref="CmsPublicOutputCachePolicy.Tag"/>,
/// evict the data cache by <see cref="HybridContentReadCache.Tag"/>, and drop the four in-memory
/// registries (routes, widgets, page types, form components) that the seeder may have rewritten.
/// Each step is independent: one failing target never blocks the others, and a failure never
/// surfaces to the writer — the cache TTL is the backstop.
/// </summary>
public sealed class CmsCacheInvalidation
{
    private readonly IOutputCacheStore _outputCacheStore;
    private readonly HybridCache _dataCache;
    private readonly ICMSRouteRegistry _routeRegistry;
    private readonly IWidgetRegistry _widgetRegistry;
    private readonly IPageControllerRegistry _pageControllerRegistry;
    private readonly IFormComponentRegistry _formComponentRegistry;
    private readonly ILogger _logger = Log.ForContext<CmsCacheInvalidation>();

    public CmsCacheInvalidation(
        ICmsCacheGeneration generation,
        IOutputCacheStore outputCacheStore,
        HybridCache dataCache,
        ICMSRouteRegistry routeRegistry,
        IWidgetRegistry widgetRegistry,
        IPageControllerRegistry pageControllerRegistry,
        IFormComponentRegistry formComponentRegistry)
    {
        _outputCacheStore = outputCacheStore ?? throw new ArgumentNullException(nameof(outputCacheStore));
        _dataCache = dataCache ?? throw new ArgumentNullException(nameof(dataCache));
        _routeRegistry = routeRegistry ?? throw new ArgumentNullException(nameof(routeRegistry));
        _widgetRegistry = widgetRegistry ?? throw new ArgumentNullException(nameof(widgetRegistry));
        _pageControllerRegistry = pageControllerRegistry ?? throw new ArgumentNullException(nameof(pageControllerRegistry));
        _formComponentRegistry = formComponentRegistry ?? throw new ArgumentNullException(nameof(formComponentRegistry));
        (generation ?? throw new ArgumentNullException(nameof(generation))).Changed += OnGenerationChanged;
    }

    private void OnGenerationChanged(long value) => _ = InvalidateAsync(value);

    /// <summary>Evicts every cache layer for a generation bump; never throws.</summary>
    public async Task InvalidateAsync(long generation)
    {
        try
        {
            await _outputCacheStore.EvictByTagAsync(CmsPublicOutputCachePolicy.Tag, default);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Output cache tag eviction failed (generation {Generation})", generation);
        }

        try
        {
            await _dataCache.RemoveByTagAsync(HybridContentReadCache.Tag, default);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Data cache tag eviction failed (generation {Generation})", generation);
        }

        try
        {
            _routeRegistry.Invalidate();
            _widgetRegistry.Invalidate();
            _pageControllerRegistry.Invalidate();
            _formComponentRegistry.Invalidate();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Registry invalidation failed (generation {Generation})", generation);
        }
    }
}
