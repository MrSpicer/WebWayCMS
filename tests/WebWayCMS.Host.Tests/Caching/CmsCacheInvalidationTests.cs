using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Hybrid;
using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Caching;
using WebWayCMS.ContentZones;
using WebWayCMS.Data.Services;
using WebWayCMS.Data.Services.Caching;
using WebWayCMS.Forms;
using WebWayCMS.Pages;

namespace WebWayCMS.Host.Tests.Caching;

[TestFixture]
public class CmsCacheInvalidationTests
{
    private CmsCacheGeneration _generation = null!;
    private IOutputCacheStore _outputStore = null!;
    private HybridCache _dataCache = null!;
    private ICMSRouteRegistry _routeRegistry = null!;
    private IWidgetRegistry _widgetRegistry = null!;
    private IPageControllerRegistry _pageControllerRegistry = null!;
    private IFormComponentRegistry _formComponentRegistry = null!;

    [SetUp]
    public void SetUp()
    {
        _generation = new CmsCacheGeneration();
        _outputStore = Substitute.For<IOutputCacheStore>();
        _dataCache = Substitute.For<HybridCache>();
        _routeRegistry = Substitute.For<ICMSRouteRegistry>();
        _widgetRegistry = Substitute.For<IWidgetRegistry>();
        _pageControllerRegistry = Substitute.For<IPageControllerRegistry>();
        _formComponentRegistry = Substitute.For<IFormComponentRegistry>();
    }

    private CmsCacheInvalidation New() =>
        new(_generation, _outputStore, _dataCache, _routeRegistry, _widgetRegistry, _pageControllerRegistry, _formComponentRegistry);

    [Test]
    public void Ctor_NullArguments_Throw()
    {
        var g = _generation;
        var o = _outputStore;
        var d = _dataCache;
        var r = _routeRegistry;
        var w = _widgetRegistry;
        var p = _pageControllerRegistry;
        var f = _formComponentRegistry;
        Assert.That(() => new CmsCacheInvalidation(null!, o, d, r, w, p, f), Throws.ArgumentNullException);
        Assert.That(() => new CmsCacheInvalidation(g, null!, d, r, w, p, f), Throws.ArgumentNullException);
        Assert.That(() => new CmsCacheInvalidation(g, o, null!, r, w, p, f), Throws.ArgumentNullException);
        Assert.That(() => new CmsCacheInvalidation(g, o, d, null!, w, p, f), Throws.ArgumentNullException);
        Assert.That(() => new CmsCacheInvalidation(g, o, d, r, null!, p, f), Throws.ArgumentNullException);
        Assert.That(() => new CmsCacheInvalidation(g, o, d, r, w, null!, f), Throws.ArgumentNullException);
        Assert.That(() => new CmsCacheInvalidation(g, o, d, r, w, p, null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task InvalidateAsync_EvictsOutputCacheDataCache_AndInvalidatesAllRegistries()
    {
        await New().InvalidateAsync(3);

        await _outputStore.Received(1).EvictByTagAsync(CmsPublicOutputCachePolicy.Tag, default);
        await _dataCache.Received(1).RemoveByTagAsync(HybridContentReadCache.Tag, default);
        _routeRegistry.Received(1).Invalidate();
        _widgetRegistry.Received(1).Invalidate();
        _pageControllerRegistry.Received(1).Invalidate();
        _formComponentRegistry.Received(1).Invalidate();
    }

    [Test]
    public void Ctor_GenerationChanged_EvictsEverything()
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _formComponentRegistry.When(r => r.Invalidate()).Do(_ => finished.TrySetResult());

        New();
        _generation.Advance(5);

        finished.Task.Wait(TimeSpan.FromSeconds(5));

        Assert.That(finished.Task.IsCompleted, Is.True, "invalidation did not reach the last registry");
    }

    [Test]
    public async Task InvalidateAsync_OutputEvictionFails_StillEvictsTheRest()
    {
        _outputStore.EvictByTagAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException(new InvalidOperationException("output store down")));

        await New().InvalidateAsync(4);

        await _dataCache.Received(1).RemoveByTagAsync(HybridContentReadCache.Tag, default);
        _routeRegistry.Received(1).Invalidate();
        _widgetRegistry.Received(1).Invalidate();
        _pageControllerRegistry.Received(1).Invalidate();
        _formComponentRegistry.Received(1).Invalidate();
    }

    [Test]
    public async Task InvalidateAsync_DataEvictionFails_StillInvalidatesRegistries()
    {
        _dataCache.RemoveByTagAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException(new InvalidOperationException("hybrid cache down")));

        await New().InvalidateAsync(4);

        _routeRegistry.Received(1).Invalidate();
        _widgetRegistry.Received(1).Invalidate();
        _pageControllerRegistry.Received(1).Invalidate();
        _formComponentRegistry.Received(1).Invalidate();
    }

    [Test]
    public async Task InvalidateAsync_RegistryInvalidationFails_DoesNotThrow()
    {
        _formComponentRegistry.When(r => r.Invalidate()).Throws(new InvalidOperationException("registry down"));

        Assert.That(async () => await New().InvalidateAsync(4), Throws.Nothing);

        await _outputStore.Received(1).EvictByTagAsync(CmsPublicOutputCachePolicy.Tag, default);
        await _dataCache.Received(1).RemoveByTagAsync(HybridContentReadCache.Tag, default);
        _routeRegistry.Received(1).Invalidate();
        _widgetRegistry.Received(1).Invalidate();
        _pageControllerRegistry.Received(1).Invalidate();
    }
}
