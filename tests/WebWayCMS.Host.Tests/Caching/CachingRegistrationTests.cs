using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using NUnit.Framework;

using WebWayCMS;
using WebWayCMS.Caching;
using WebWayCMS.Data.Services.Caching;

namespace WebWayCMS.Host.Tests.Caching;

[TestFixture]
public class CachingRegistrationTests
{
    private readonly string[] _skipVars =
    {
        "WEBWAYCMS_SKIP_MIGRATIONS", "WEBWAYCMS_SKIP_ROLESEED", "WEBWayCMS_SKIP_CONTENTSEED",
        "WEBWAYCMS_SKIP_DEFAULTWIDGETS", "WEBWAYCMS_SKIP_DEFAULTPAGECONTROLLERS", "WEBWAYCMS_SKIP_CODEBASEDROUTES",
        "WEBWAYCMS_SKIP_DEFAULTFORMCOMPONENTS", "WEBWAYCMS_SKIP_DEFAULTCONTENTTYPES"
    };
    private Dictionary<string, string?> _previous = new();

    [SetUp]
    public void SetUp()
    {
        _previous = _skipVars.ToDictionary(v => v, Environment.GetEnvironmentVariable);
        foreach (var v in _skipVars)
            Environment.SetEnvironmentVariable(v, "true");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var (k, v) in _previous)
            Environment.SetEnvironmentVariable(k, v);
    }

    private static (WebApplication App, IServiceCollection Services) BuildApp(bool cachingEnabled)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;Username=test;Password=test" },
                { "Caching:Enabled", cachingEnabled ? "true" : "false" }
            })
            .Build();

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddWebWayCmsRendering(config);
        var app = builder.Build();
        app.UseWebWayCmsRendering(throwOnError: false);
        return (app, builder.Services);
    }

    [Test]
    public void Enabled_Default_RegistersPolicyReadCacheAndPoller()
    {
        var (app, services) = BuildApp(cachingEnabled: true);
        using var _ = app;

        Assert.Multiple(() =>
        {
            Assert.That(app.Services.GetRequiredService<IContentReadCache>(), Is.InstanceOf<HybridContentReadCache>());
            Assert.That(app.Services.GetRequiredService<CmsPublicOutputCachePolicy>(), Is.Not.Null);
            Assert.That(services.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(CacheGenerationMonitor)), Is.True);
            Assert.That(app.Services.GetRequiredService<CmsCacheInvalidation>(), Is.Not.Null);
        });
    }

    // Regression: CmsCacheInvalidation is a lazy singleton — only UseWebWayCms* resolving it at
    // startup runs its constructor, which is where it subscribes to the generation's Changed event.
    // Without that, a generation advance (this host's write or the poller's) evicts nothing.
    [Test]
    public async Task Startup_GenerationAdvance_EvictsTaggedDataCacheEntries()
    {
        var (app, _) = BuildApp(cachingEnabled: true);
        using var _ = app;

        var generation = app.Services.GetRequiredService<ICmsCacheGeneration>();
        var dataCache = app.Services.GetRequiredService<HybridCache>();
        var options = new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(10) };
        await dataCache.SetAsync("probe", 42, options, new[] { HybridContentReadCache.Tag }, default);

        // Sanity: the entry is cached (the factory below returns a different value and must not run).
        var cached = await dataCache.GetOrCreateAsync("probe", _ => ValueTask.FromResult(41), options,
            new[] { HybridContentReadCache.Tag }, default);
        Assert.That(cached, Is.EqualTo(42), "sanity: the entry is cached");

        generation.Advance(generation.Current + 1);

        // Invalidation is fire-and-forget on the Changed event: poll briefly for the eviction. The
        // probe re-stores itself on a miss, so the miss is observed as the factory's value.
        var evicted = false;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && !evicted)
        {
            evicted = await dataCache.GetOrCreateAsync("probe", _ => ValueTask.FromResult(41), options,
                new[] { HybridContentReadCache.Tag }, default) == 41;
            if (!evicted)
                await Task.Delay(50);
        }

        Assert.That(evicted, Is.True, "a generation advance must evict the tagged data cache entry");
    }

    // Regression: the policy was only a DI registration, which OutputCacheMiddleware never reads, so
    // nothing was ever cached. Drive the real middleware with the app's services: the second
    // anonymous GET of a public path must be served without running the endpoint again.
    [Test]
    public async Task Enabled_SecondAnonymousGet_IsServedFromOutputCache()
    {
        var (app, _) = BuildApp(cachingEnabled: true);
        using var _ = app;

        var renders = 0;
        var pipeline = new ApplicationBuilder(app.Services);
        pipeline.UseOutputCache();
        pipeline.Run(async ctx =>
        {
            renders++;
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            await ctx.Response.WriteAsync("page");
        });
        var handler = pipeline.Build();

        var first = await GetAsync(handler, app.Services, "/about");
        var second = await GetAsync(handler, app.Services, "/about");
        var admin = await GetAsync(handler, app.Services, "/wadmin");
        await GetAsync(handler, app.Services, "/wadmin");

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo("page"));
            Assert.That(second, Is.EqualTo("page"));
            Assert.That(admin, Is.EqualTo("page"));
            Assert.That(renders, Is.EqualTo(3), "/about renders once (then a hit); /wadmin renders every time");
        });
    }

    private static async Task<string> GetAsync(RequestDelegate handler, IServiceProvider services, string path)
    {
        using var scope = services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = HttpMethods.Get;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        context.Request.Path = path;
        using var body = new MemoryStream();
        context.Response.Body = body;
        await handler(context);
        return System.Text.Encoding.UTF8.GetString(body.ToArray());
    }

    [Test]
    public void Disabled_RegistersNullReadCache_AndNoPolicyOrPoller()
    {
        var (app, services) = BuildApp(cachingEnabled: false);
        using var _ = app;

        Assert.Multiple(() =>
        {
            Assert.That(app.Services.GetRequiredService<IContentReadCache>(), Is.InstanceOf<NullContentReadCache>());
            Assert.That(services.Any(d => d.ServiceType == typeof(CmsPublicOutputCachePolicy)), Is.False);
            Assert.That(services.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(CacheGenerationMonitor)), Is.False);
            // The invalidation subscriber and generation are still registered: the interceptor on this
            // host's own writes must have something to notify.
            Assert.That(app.Services.GetRequiredService<CmsCacheInvalidation>(), Is.Not.Null);
            Assert.That(app.Services.GetRequiredService<ICmsCacheGeneration>(), Is.Not.Null);
            Assert.That(app.Services.GetRequiredService<IOptions<CacheOptions>>().Value.Enabled, Is.False);
        });
    }
}
