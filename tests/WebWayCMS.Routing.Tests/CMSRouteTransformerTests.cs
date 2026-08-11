using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Pages;
using WebWayCMS.Routing;

namespace WebWayCMS.Routing.Tests;

[TestFixture]
public class CMSRouteTransformerTests
{
    private ICMSRouteService _routeService = null!;
    private IPageControllerRegistry _registry = null!;
    private IPageService _pageService = null!;
    private CMSRouteTransformer _transformer = null!;

    [SetUp]
    public void SetUp()
    {
        _routeService = Substitute.For<ICMSRouteService>();
        _registry = Substitute.For<IPageControllerRegistry>();
        _pageService = Substitute.For<IPageService>();
        _transformer = new CMSRouteTransformer(_routeService, _registry, _pageService);
    }

    private static HttpContext CreateHttpContext(string path = "/test")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        return context;
    }

    private static CMSRouteMatchResult CreateMatchResult(
        string owningContentType,
        string controllerName,
        string action = "Index",
        string? dataTokensJson = null,
        string? pattern = "/test",
        Guid? owningContentMasterId = null,
        Dictionary<string, string>? routeValues = null)
    {
        var route = new CMSRouteDTO
        {
            Pattern = pattern ?? "/test",
            DefaultsJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                { "controller", controllerName },
                { "action", action }
            }),
            DataTokensJson = dataTokensJson ?? "{}",
            OwningContentType = owningContentType,
            OwningContentMasterId = owningContentMasterId,
            ContentMeta = new ContentDTO { IsPublished = true }
        };

        return new CMSRouteMatchResult
        {
            Route = route,
            RouteValues = routeValues ?? new Dictionary<string, string>()
        };
    }

    [Test]
    public async Task TransformAsync_MatchReturnsNull_ReturnsNull()
    {
        var context = CreateHttpContext();
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns((CMSRouteMatchResult?)null);

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task TransformAsync_NoControllerInDefaults_ReturnsNull()
    {
        var context = CreateHttpContext();
        var route = new CMSRouteDTO
        {
            Pattern = "/test",
            DefaultsJson = "{}",
            OwningContentType = "Page",
            ContentMeta = new ContentDTO { IsPublished = true }
        };
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(new CMSRouteMatchResult
        {
            Route = route,
            RouteValues = new Dictionary<string, string>()
        });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task TransformAsync_NonCodeBased_ControllerNotInRegistry_ReturnsNull()
    {
        var context = CreateHttpContext();
        var match = CreateMatchResult("Page", "Missing");
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);
        _registry.GetByName("Missing").Returns((PageControllerInfo?)null);

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task TransformAsync_CodeBased_BypassesRegistryLookup()
    {
        var context = CreateHttpContext();
        var match = CreateMatchResult("CodeBased", "MyPage", action: "Index");
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        Assert.That(result!["controller"], Is.EqualTo("MyPage"));
        Assert.That(result["action"], Is.EqualTo("Index"));
    }

    [Test]
    public async Task TransformAsync_CodeBased_DoesNotCallRegistry()
    {
        var context = CreateHttpContext();
        var match = CreateMatchResult("CodeBased", "MyPage");
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        await _transformer.TransformAsync(context, new RouteValueDictionary());

        _registry.DidNotReceive().GetByName(Arg.Any<string>());
    }

    [Test]
    public async Task TransformAsync_CodeBased_DoesNotCallPageService()
    {
        var context = CreateHttpContext();
        var match = CreateMatchResult("CodeBased", "MyPage");
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        await _transformer.TransformAsync(context, new RouteValueDictionary());

        await _pageService.DidNotReceive().GetAllVersionsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TransformAsync_CodeBased_SetsRouteDataItem()
    {
        var context = CreateHttpContext();
        var match = CreateMatchResult("CodeBased", "MyPage");
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(context.Items["CMS:RouteData"], Is.SameAs(match.Route));
    }

    [Test]
    public async Task TransformAsync_CodeBased_DoesNotSetPageDataOrConfig()
    {
        var context = CreateHttpContext();
        var match = CreateMatchResult("CodeBased", "MyPage");
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(context.Items.ContainsKey(CMSRouteTransformer.PageDataItemKey), Is.False);
        Assert.That(context.Items.ContainsKey(CMSRouteTransformer.PageConfigItemKey), Is.False);
    }

    [Test]
    public async Task TransformAsync_CodeBased_ReturnsRouteValuesInDictionary()
    {
        var context = CreateHttpContext();
        var match = CreateMatchResult("CodeBased", "MyPage",
            routeValues: new Dictionary<string, string> { { "slug", "hello" } });
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result!["slug"], Is.EqualTo("hello"));
    }

    [Test]
    public async Task TransformAsync_CodeBased_IncludesRouteValuesInResult()
    {
        var context = CreateHttpContext();
        var match = CreateMatchResult("CodeBased", "MyPage",
            routeValues: new Dictionary<string, string> { { "slug", "hello" }, { "id", "42" } });
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result!["slug"], Is.EqualTo("hello"));
        Assert.That(result["id"], Is.EqualTo("42"));
    }

    [Test]
    public async Task TransformAsync_CodeBased_UsesActionFromDefaults()
    {
        var context = CreateHttpContext();
        var match = CreateMatchResult("CodeBased", "MyPage", action: "Detail");
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result!["action"], Is.EqualTo("Detail"));
    }

    [Test]
    public async Task TransformAsync_CodeBased_NullOwningContentType_GoesThroughRegistry()
    {
        var context = CreateHttpContext();
        var match = CreateMatchResult(null!, "MyPage");
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);
        _registry.GetByName("MyPage").Returns((PageControllerInfo?)null);

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task TransformAsync_PageType_LoadsPageData()
    {
        var context = CreateHttpContext();
        var pageId = Guid.NewGuid();
        var match = CreateMatchResult("Page", "MyPage", owningContentMasterId: pageId);
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        var pageInfo = new PageControllerInfo { Name = "MyPage" };
        _registry.GetByName("MyPage").Returns(pageInfo);

        var pageDto = new PageDTO { ContentMeta = new ContentDTO { Title = "Test Page" } };
        _pageService.GetAllVersionsAsync(pageId, Arg.Any<CancellationToken>())
            .Returns(new List<PageDTO> { pageDto });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        Assert.That(context.Items[CMSRouteTransformer.PageDataItemKey], Is.SameAs(pageDto));
    }

    [Test]
    public async Task TransformAsync_PageType_UsesControllerAndActionFromDefaults()
    {
        var context = CreateHttpContext();
        var match = CreateMatchResult("Page", "MyPage", action: "Custom");
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("MyPage").Returns(new PageControllerInfo { Name = "MyPage" });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result!["controller"], Is.EqualTo("MyPage"));
        Assert.That(result["action"], Is.EqualTo("Custom"));
    }

    [Test]
    public async Task TransformAsync_NonPageType_LoadsPageDataFromDataTokens()
    {
        var context = CreateHttpContext();
        var parentPageId = Guid.NewGuid();
        var dataTokens = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            { "ParentPageMasterId", parentPageId.ToString() }
        });
        var match = CreateMatchResult("Widget", "MyWidget", dataTokensJson: dataTokens,
            owningContentMasterId: Guid.NewGuid());
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("MyWidget").Returns(new PageControllerInfo { Name = "MyWidget" });

        var pageDto = new PageDTO { ContentMeta = new ContentDTO { Title = "Parent Page" } };
        _pageService.GetAllVersionsAsync(parentPageId, Arg.Any<CancellationToken>())
            .Returns(new List<PageDTO> { pageDto });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        Assert.That(context.Items[CMSRouteTransformer.PageDataItemKey], Is.SameAs(pageDto));
    }

    [Test]
    public async Task TransformAsync_NonPageType_InvalidParentPageMasterId_NoPageDataLoaded()
    {
        var context = CreateHttpContext();
        var dataTokens = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            { "ParentPageMasterId", "not-a-guid" }
        });
        var match = CreateMatchResult("Widget", "MyWidget", dataTokensJson: dataTokens,
            owningContentMasterId: Guid.NewGuid());
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("MyWidget").Returns(new PageControllerInfo { Name = "MyWidget" });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        Assert.That(context.Items.ContainsKey(CMSRouteTransformer.PageDataItemKey), Is.False);
    }

    [Test]
    public async Task TransformAsync_WithConfiguration_DeserializesAndSetsConfig()
    {
        var context = CreateHttpContext();
        var pageId = Guid.NewGuid();
        var config = new SamplePageConfig { Title = "Hello", PageSize = 10 };
        var configJson = JsonSerializer.Serialize(config);
        var dataTokens = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            { "ConfigurationJson", configJson }
        });
        var match = CreateMatchResult("Page", "Configured", dataTokensJson: dataTokens,
            owningContentMasterId: pageId);
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("Configured").Returns(new PageControllerInfo
        {
            Name = "Configured",
            ConfigurationType = typeof(SamplePageConfig)
        });

        var pageDto = new PageDTO
        {
            ContentMeta = new ContentDTO { Title = "Test Page" },
            ConfigurationJson = configJson
        };
        _pageService.GetAllVersionsAsync(pageId, Arg.Any<CancellationToken>())
            .Returns(new List<PageDTO> { pageDto });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        var loadedConfig = context.Items[CMSRouteTransformer.PageConfigItemKey] as SamplePageConfig;
        Assert.That(loadedConfig, Is.Not.Null);
        Assert.That(loadedConfig!.Title, Is.EqualTo("Hello"));
        Assert.That(loadedConfig.PageSize, Is.EqualTo(10));
    }

    [Test]
    public async Task TransformAsync_WithConfiguration_InvalidJson_CreatesDefaultConfig()
    {
        var context = CreateHttpContext();
        var pageId = Guid.NewGuid();
        var match = CreateMatchResult("Page", "Configured", owningContentMasterId: pageId);
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("Configured").Returns(new PageControllerInfo
        {
            Name = "Configured",
            ConfigurationType = typeof(SamplePageConfig)
        });

        var pageDto = new PageDTO
        {
            ContentMeta = new ContentDTO { Title = "Test Page" },
            ConfigurationJson = "{ invalid json"
        };
        _pageService.GetAllVersionsAsync(pageId, Arg.Any<CancellationToken>())
            .Returns(new List<PageDTO> { pageDto });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        var loadedConfig = context.Items[CMSRouteTransformer.PageConfigItemKey];
        Assert.That(loadedConfig, Is.InstanceOf<SamplePageConfig>());
    }

    [Test]
    public async Task TransformAsync_WithConfiguration_NullConfigJson_CreatesDefaultConfig()
    {
        var context = CreateHttpContext();
        var pageId = Guid.NewGuid();
        var match = CreateMatchResult("Page", "Configured", owningContentMasterId: pageId);
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("Configured").Returns(new PageControllerInfo
        {
            Name = "Configured",
            ConfigurationType = typeof(SamplePageConfig)
        });

        var pageDto = new PageDTO
        {
            ContentMeta = new ContentDTO { Title = "Test Page" },
            ConfigurationJson = null!
        };
        _pageService.GetAllVersionsAsync(pageId, Arg.Any<CancellationToken>())
            .Returns(new List<PageDTO> { pageDto });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        var loadedConfig = context.Items[CMSRouteTransformer.PageConfigItemKey];
        Assert.That(loadedConfig, Is.InstanceOf<SamplePageConfig>());
    }

    [Test]
    public async Task TransformAsync_WithConfiguration_EmptyConfigJson_CreatesDefaultConfig()
    {
        var context = CreateHttpContext();
        var pageId = Guid.NewGuid();
        var match = CreateMatchResult("Page", "Configured", owningContentMasterId: pageId);
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("Configured").Returns(new PageControllerInfo
        {
            Name = "Configured",
            ConfigurationType = typeof(SamplePageConfig)
        });

        var pageDto = new PageDTO
        {
            ContentMeta = new ContentDTO { Title = "Test Page" },
            ConfigurationJson = "{}"
        };
        _pageService.GetAllVersionsAsync(pageId, Arg.Any<CancellationToken>())
            .Returns(new List<PageDTO> { pageDto });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        var loadedConfig = context.Items[CMSRouteTransformer.PageConfigItemKey];
        Assert.That(loadedConfig, Is.InstanceOf<SamplePageConfig>());
    }

    [Test]
    public async Task TransformAsync_WithConfiguration_BlankConfigJson_CreatesDefaultConfig()
    {
        var context = CreateHttpContext();
        var pageId = Guid.NewGuid();
        var match = CreateMatchResult("Page", "Configured", owningContentMasterId: pageId);
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("Configured").Returns(new PageControllerInfo
        {
            Name = "Configured",
            ConfigurationType = typeof(SamplePageConfig)
        });

        var pageDto = new PageDTO
        {
            ContentMeta = new ContentDTO { Title = "Test Page" },
            ConfigurationJson = ""
        };
        _pageService.GetAllVersionsAsync(pageId, Arg.Any<CancellationToken>())
            .Returns(new List<PageDTO> { pageDto });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        var loadedConfig = context.Items[CMSRouteTransformer.PageConfigItemKey];
        Assert.That(loadedConfig, Is.InstanceOf<SamplePageConfig>());
    }

    [Test]
    public async Task TransformAsync_NoConfigurationType_DoesNotSetConfig()
    {
        var context = CreateHttpContext();
        var match = CreateMatchResult("Page", "Simple");
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("Simple").Returns(new PageControllerInfo
        {
            Name = "Simple",
            ConfigurationType = null
        });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        Assert.That(context.Items.ContainsKey(CMSRouteTransformer.PageConfigItemKey), Is.False);
    }

    [Test]
    public async Task TransformAsync_WithConfiguration_NullJsonLiteral_CreatesDefaultConfig()
    {
        var context = CreateHttpContext();
        var pageId = Guid.NewGuid();
        var match = CreateMatchResult("Page", "Configured", owningContentMasterId: pageId);
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("Configured").Returns(new PageControllerInfo
        {
            Name = "Configured",
            ConfigurationType = typeof(SamplePageConfig)
        });

        var pageDto = new PageDTO
        {
            ContentMeta = new ContentDTO { Title = "Test Page" },
            ConfigurationJson = "null"
        };
        _pageService.GetAllVersionsAsync(pageId, Arg.Any<CancellationToken>())
            .Returns(new List<PageDTO> { pageDto });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        var loadedConfig = context.Items[CMSRouteTransformer.PageConfigItemKey];
        Assert.That(loadedConfig, Is.InstanceOf<SamplePageConfig>());
    }

    [Test]
    public async Task TransformAsync_NoPageData_SetsDefaultConfig()
    {
        var context = CreateHttpContext();
        var match = CreateMatchResult("Page", "Configured", owningContentMasterId: null);
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("Configured").Returns(new PageControllerInfo
        {
            Name = "Configured",
            ConfigurationType = typeof(SamplePageConfig)
        });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        var loadedConfig = context.Items[CMSRouteTransformer.PageConfigItemKey];
        Assert.That(loadedConfig, Is.InstanceOf<SamplePageConfig>());
    }

    [Test]
    public async Task TransformAsync_CmsRouteWithPatternParams_IncludesRouteValuesInResult()
    {
        var context = CreateHttpContext();
        var match = CreateMatchResult("CodeBased", "Blog",
            routeValues: new Dictionary<string, string> { { "slug", "my-post" }, { "year", "2026" } });
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result!["controller"], Is.EqualTo("Blog"));
        Assert.That(result["action"], Is.EqualTo("Index"));
        Assert.That(result["slug"], Is.EqualTo("my-post"));
        Assert.That(result["year"], Is.EqualTo("2026"));
    }

    [Test]
    public async Task TransformAsync_HandlesTrailingSlash()
    {
        var context = CreateHttpContext("/test/");
        var match = CreateMatchResult("CodeBased", "MyPage");
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        Assert.That(result!["controller"], Is.EqualTo("MyPage"));
    }

    [Test]
    public async Task TransformAsync_HandlesEmptyPath()
    {
        var context = CreateHttpContext(string.Empty);
        var match = CreateMatchResult("CodeBased", "Home");
        _routeService.MatchRouteAsync("/", Arg.Any<CancellationToken>()).Returns(match);

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        Assert.That(result!["controller"], Is.EqualTo("Home"));
    }

    [Test]
    public async Task TransformAsync_PageType_NoMasterId_GoesToElseBranch()
    {
        var context = CreateHttpContext();
        var match = CreateMatchResult("Page", "MyPage", owningContentMasterId: null);
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("MyPage").Returns(new PageControllerInfo { Name = "MyPage" });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        Assert.That(result!["controller"], Is.EqualTo("MyPage"));
    }

    [Test]
    public async Task TransformAsync_NonPageType_NoParentPageMasterIdKey_NoPageDataLoaded()
    {
        var context = CreateHttpContext();
        var dataTokens = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            { "SomeOtherKey", "some-value" }
        });
        var match = CreateMatchResult("Widget", "MyWidget", dataTokensJson: dataTokens,
            owningContentMasterId: Guid.NewGuid());
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("MyWidget").Returns(new PageControllerInfo { Name = "MyWidget" });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        Assert.That(context.Items.ContainsKey(CMSRouteTransformer.PageDataItemKey), Is.False);
    }

    [Test]
    public async Task TransformAsync_PageType_EmptyVersions_NoPageDataLoaded()
    {
        var context = CreateHttpContext();
        var pageId = Guid.NewGuid();
        var match = CreateMatchResult("Page", "MyPage", owningContentMasterId: pageId);
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("MyPage").Returns(new PageControllerInfo { Name = "MyPage" });
        _pageService.GetAllVersionsAsync(pageId, Arg.Any<CancellationToken>())
            .Returns(new List<PageDTO>());

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        Assert.That(context.Items.ContainsKey(CMSRouteTransformer.PageDataItemKey), Is.False);
    }

    [Test]
    public void Constructor_NullRouteService_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new CMSRouteTransformer(null!, _registry, _pageService));
        Assert.That(ex!.ParamName, Is.EqualTo("routeService"));
    }

    [Test]
    public void Constructor_NullRegistry_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new CMSRouteTransformer(_routeService, null!, _pageService));
        Assert.That(ex!.ParamName, Is.EqualTo("registry"));
    }

    [Test]
    public void Constructor_NullPageService_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new CMSRouteTransformer(_routeService, _registry, null!));
        Assert.That(ex!.ParamName, Is.EqualTo("pageService"));
    }

    [Test]
    public async Task TransformAsync_MalformedDefaultsJson_ReturnsNull()
    {
        var context = CreateHttpContext();
        var route = new CMSRouteDTO
        {
            Pattern = "/test",
            DefaultsJson = "{ bad }",
            OwningContentType = "Page",
            ContentMeta = new ContentDTO { IsPublished = true }
        };
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(new CMSRouteMatchResult
        {
            Route = route,
            RouteValues = new Dictionary<string, string>()
        });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task TransformAsync_MalformedDataTokensJson_DoesNotCrash()
    {
        var context = CreateHttpContext();
        var route = new CMSRouteDTO
        {
            Pattern = "/test",
            DefaultsJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                { "controller", "MyPage" },
                { "action", "Index" }
            }),
            DataTokensJson = "{ bad }",
            OwningContentType = "Page",
            ContentMeta = new ContentDTO { IsPublished = true }
        };
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(new CMSRouteMatchResult
        {
            Route = route,
            RouteValues = new Dictionary<string, string>()
        });

        _registry.GetByName("MyPage").Returns(new PageControllerInfo { Name = "MyPage" });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task TransformAsync_NullDefaultsJson_ReturnsNull()
    {
        var context = CreateHttpContext();
        var route = new CMSRouteDTO
        {
            Pattern = "/test",
            DefaultsJson = null!,
            OwningContentType = "Page",
            ContentMeta = new ContentDTO { IsPublished = true }
        };
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(new CMSRouteMatchResult
        {
            Route = route,
            RouteValues = new Dictionary<string, string>()
        });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task TransformAsync_NullDataTokensJson_DoesNotCrash()
    {
        var context = CreateHttpContext();
        var route = new CMSRouteDTO
        {
            Pattern = "/test",
            DefaultsJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                { "controller", "MyPage" },
                { "action", "Index" }
            }),
            DataTokensJson = null!,
            OwningContentType = "Page",
            ContentMeta = new ContentDTO { IsPublished = true }
        };
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(new CMSRouteMatchResult
        {
            Route = route,
            RouteValues = new Dictionary<string, string>()
        });

        _registry.GetByName("MyPage").Returns(new PageControllerInfo { Name = "MyPage" });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());
        Assert.That(result, Is.Not.Null);
    }
}
