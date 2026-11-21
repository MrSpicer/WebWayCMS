using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using WebWayCMS.Caching;
using WebWayCMS.Data.Services.Caching;
using WebWayCMS.Mcp;

namespace WebWayCMS.Startup;

[ExcludeFromCodeCoverage]
internal static class CmsCachingRegistration
{
    /// <summary>
    /// Registers the caching stack in both deployment modes (a rendering-only host caches too —
    /// that is where the split-deployment staleness is solved: its poller picks up the admin host's
    /// publishes). The output cache and the data cache are always registered (cheap, and the
    /// invalidation subscriber needs both targets), while the policy, the poller, and the
    /// read-through cache honor <c>Caching:Enabled</c>.
    /// </summary>
    internal static void ConfigureCaching(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(CacheOptions.SectionName);
        var enabled = section.Get<CacheOptions>()?.Enabled ?? true;

        services.Configure<CacheOptions>(section);
        services.AddSingleton<IValidateOptions<CacheOptions>, CacheOptionsValidator>();
        services.AddOptions<CacheOptions>().ValidateOnStart();

        // The interceptor is registered before the database so AddDbContext's interceptor list can
        // resolve it; it is harmless on hosts that never save (the poller/invalidation still work).
        services.AddSingleton<PublicContentChangeInterceptor>();
        services.AddSingleton<ICacheGenerationStore, CacheGenerationStore>();
        services.AddSingleton<ICmsCacheGeneration, CmsCacheGeneration>();

        // AddOutputCache registers IOutputCacheStore; AddHybridCache registers HybridCache.
        services.AddOutputCache();
        services.AddHybridCache();

        if (enabled)
        {
            // The middleware reads policies only from OutputCacheOptions' base policies (or endpoint
            // metadata): a DI registration alone is never consulted.
            services.AddSingleton<CmsPublicOutputCachePolicy>();
            services.AddOptions<OutputCacheOptions>()
                .Configure<CmsPublicOutputCachePolicy>((options, policy) => options.AddBasePolicy(policy));
            services.AddSingleton<IContentReadCache>(sp => new HybridContentReadCache(
                sp.GetRequiredService<HybridCache>(),
                sp.GetRequiredService<ICmsCacheGeneration>(),
                sp.GetRequiredService<IOptions<CacheOptions>>().Value.DataCacheExpiration));
            services.AddHostedService<CacheGenerationMonitor>();
        }
        else
        {
            services.AddSingleton<IContentReadCache, NullContentReadCache>();
        }

        // Last: the subscriber needs every registry, which the rendering registration adds.
        services.AddSingleton<CmsCacheInvalidation>();
    }
}
