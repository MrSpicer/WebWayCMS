using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Pages;

namespace WebWayCMS.Routing.Tests;

[TestFixture]
public class PageRouteTransformerTests
{
    private IPageService _service = null!;
    private IPageControllerRegistry _registry = null!;
    private ISubRouteContent _resolver = null!;
    private PageRouteTransformer _transformer = null!;

    [SetUp]
    public void SetUp()
    {
        _service = Substitute.For<IPageService>();
        _registry = Substitute.For<IPageControllerRegistry>();
        _resolver = Substitute.For<ISubRouteContent>();
        // Default: nothing matches and no sub-route resolves unless a test arranges it.
        _service.GetByRouteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((PageDTO?)null);
        _resolver.CanResolveSubRouteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _transformer = new PageRouteTransformer(_service, _registry, new[] { _resolver });
    }

    private void ArrangeSubRoute(string subRoute) =>
        _resolver.CanResolveSubRouteAsync(subRoute, Arg.Any<CancellationToken>()).Returns(true);

    private static HttpContext ContextWith(string? path)
    {
        var context = new DefaultHttpContext();
        if (path != null)
            context.Request.Path = path;
        return context;
    }

    private static PageDTO Page(string controllerName, string json = "{}") =>
        new() { ControllerName = controllerName, ConfigurationJson = json };

    private void ArrangeRoute(string route, PageDTO page) =>
        _service.GetByRouteAsync(route, Arg.Any<CancellationToken>()).Returns(page);

    private void ArrangeController(string name, Type? configType = null) =>
        _registry.GetByName(name).Returns(new PageControllerInfo { Name = name, ConfigurationType = configType });

    [Test]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new PageRouteTransformer(null!, _registry, new[] { _resolver }), Throws.ArgumentNullException);
            Assert.That(() => new PageRouteTransformer(_service, null!, new[] { _resolver }), Throws.ArgumentNullException);
            Assert.That(() => new PageRouteTransformer(_service, _registry, null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public async Task TransformAsync_ExactMatchNoConfig_ReturnsControllerRoute()
    {
        var page = Page("Article");
        ArrangeRoute("/about", page);
        ArrangeController("Article");
        var context = ContextWith("/about");

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.Multiple(() =>
        {
            Assert.That(result["controller"], Is.EqualTo("Article"));
            Assert.That(result["action"], Is.EqualTo("Index"));
            Assert.That(context.Items["CMS:PageData"], Is.SameAs(page));
            Assert.That(context.Items.ContainsKey("CMS:SubRoute"), Is.False);
            Assert.That(context.Items.ContainsKey("CMS:PageConfig"), Is.False);
        });
    }

    [Test]
    public async Task TransformAsync_NullPath_TreatedAsRoot()
    {
        ArrangeRoute("/", Page("Home"));
        ArrangeController("Home");

        var result = await _transformer.TransformAsync(ContextWith(null), new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task TransformAsync_TrailingSlash_IsTrimmed()
    {
        ArrangeRoute("/about", Page("Article"));
        ArrangeController("Article");

        var result = await _transformer.TransformAsync(ContextWith("/about/"), new RouteValueDictionary());

        Assert.That(result["controller"], Is.EqualTo("Article"));
    }

    [Test]
    public async Task TransformAsync_MixedCasePath_IsLowercased()
    {
        ArrangeRoute("/about", Page("Article"));
        ArrangeController("Article");

        var result = await _transformer.TransformAsync(ContextWith("/About"), new RouteValueDictionary());

        Assert.That(result["controller"], Is.EqualTo("Article"));
    }

    [Test]
    public async Task TransformAsync_SubRouteResolvable_StoresSubRoute()
    {
        // "/a/b/c": exact null, "/a/b" null, "/a" matches; a resolver resolves "b/c"
        // -> the page serves the request with the sub-route stored.
        ArrangeRoute("/a", Page("Article"));
        ArrangeController("Article");
        ArrangeSubRoute("b/c");
        var context = ContextWith("/a/b/c");

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.Multiple(() =>
        {
            Assert.That(result["controller"], Is.EqualTo("Article"));
            Assert.That(context.Items["CMS:SubRoute"], Is.EqualTo("b/c"));
        });
    }

    [Test]
    public async Task TransformAsync_SubRouteUnresolvable_ReturnsNull()
    {
        // "/a" matches as a parent page, but no resolver can serve "b/c" (e.g. it is a
        // controller route or non-existent), so the page must NOT swallow it: resolve to
        // null so the request falls through to the controller route table (-> 404).
        ArrangeRoute("/a", Page("Article"));
        ArrangeController("Article");
        var context = ContextWith("/a/b/c");

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(context.Items.ContainsKey("CMS:SubRoute"), Is.False);
        });
    }

    [Test]
    public async Task TransformAsync_RootPageIsNotCatchAllParent_ReturnsNull()
    {
        // A root page exists but the path nests under no matching parent. The root
        // page must NOT swallow unmatched top-level paths, so this resolves to null
        // (-> 404) rather than rendering the home page.
        ArrangeRoute("/", Page("Home"));
        ArrangeController("Home");
        var context = ContextWith("/foo/bar");

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(context.Items.ContainsKey("CMS:SubRoute"), Is.False);
        });
    }

    [Test]
    public async Task TransformAsync_NoMatch_ReturnsNull()
    {
        var result = await _transformer.TransformAsync(ContextWith("/missing/page"), new RouteValueDictionary());

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task TransformAsync_RootNoMatch_ReturnsNull()
    {
        var result = await _transformer.TransformAsync(ContextWith("/"), new RouteValueDictionary());

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task TransformAsync_ControllerNotRegistered_ReturnsNull()
    {
        ArrangeRoute("/about", Page("Ghost"));
        _registry.GetByName("Ghost").Returns((PageControllerInfo?)null);

        var result = await _transformer.TransformAsync(ContextWith("/about"), new RouteValueDictionary());

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task TransformAsync_ValidConfigJson_DeserializesConfig()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new SamplePageConfig { Title = "Hi" });
        ArrangeRoute("/about", Page("Configured", json));
        ArrangeController("Configured", typeof(SamplePageConfig));
        var context = ContextWith("/about");

        await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(context.Items["CMS:PageConfig"], Is.InstanceOf<SamplePageConfig>());
        Assert.That(((SamplePageConfig)context.Items["CMS:PageConfig"]!).Title, Is.EqualTo("Hi"));
    }

    [Test]
    public async Task TransformAsync_InvalidConfigJson_FallsBackToDefaultInstance()
    {
        ArrangeRoute("/about", Page("Configured", "{ not json"));
        ArrangeController("Configured", typeof(SamplePageConfig));
        var context = ContextWith("/about");

        await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(context.Items["CMS:PageConfig"], Is.InstanceOf<SamplePageConfig>());
    }

    [Test]
    public async Task TransformAsync_WhitespaceConfigJson_SkipsConfig()
    {
        ArrangeRoute("/about", Page("Configured", "   "));
        ArrangeController("Configured", typeof(SamplePageConfig));
        var context = ContextWith("/about");

        await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(context.Items.ContainsKey("CMS:PageConfig"), Is.False);
    }
}