using System.Security.Claims;
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
    private IContentStore<PageDTO> _pageStore = null!;
    private IPageHistoryService _pageHistory = null!;
    private CMSRouteTransformer _transformer = null!;

    private static readonly CmsDeploymentOptions AdminMode = new() { IsAdminMode = true };

    [SetUp]
    public void SetUp()
    {
        _routeService = Substitute.For<ICMSRouteService>();
        _registry = Substitute.For<IPageControllerRegistry>();
        _pageStore = Substitute.For<IContentStore<PageDTO>>();
        _pageHistory = Substitute.For<IPageHistoryService>();
        _transformer = new CMSRouteTransformer(_routeService, _registry, _pageStore, _pageHistory, AdminMode);
    }

    private static HttpContext CreateHttpContext(string path = "/test")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        return context;
    }

    private static HttpContext CreatePreviewHttpContext(string path, params string[] roles)
    {
        var context = CreateHttpContext(path);
        var identity = new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "Test");
        context.User = new ClaimsPrincipal(identity);
        return context;
    }

    private static CMSRouteMatchResult CreateMatchResult(
        string owningContentType,
        string controllerName,
        string action = "Index",
        string? dataTokensJson = null,
        string? pattern = "/test",
        Guid? owningContentNodeId = null,
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
            OwningContentNodeId = owningContentNodeId
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
            OwningContentType = "Page"
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
    public async Task TransformAsync_CodeBased_DoesNotCallPageStore()
    {
        var context = CreateHttpContext();
        var match = CreateMatchResult("CodeBased", "MyPage");
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        await _transformer.TransformAsync(context, new RouteValueDictionary());

        await _pageStore.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
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
        var pageNodeId = Guid.NewGuid();
        var match = CreateMatchResult("Page", "MyPage", owningContentNodeId: pageNodeId);
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        var pageInfo = new PageControllerInfo { Name = "MyPage" };
        _registry.GetByName("MyPage").Returns(pageInfo);

        var pageDto = new PageDTO { ControllerName = "MyPage" };
        _pageStore.GetAsync(pageNodeId, Arg.Any<CancellationToken>())
            .Returns(pageDto);

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
        var parentPageNodeId = Guid.NewGuid();
        var dataTokens = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            { "ParentPageNodeId", parentPageNodeId.ToString() }
        });
        var match = CreateMatchResult("Widget", "MyWidget", dataTokensJson: dataTokens,
            owningContentNodeId: Guid.NewGuid());
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("MyWidget").Returns(new PageControllerInfo { Name = "MyWidget" });

        var pageDto = new PageDTO { ControllerName = "MyWidget" };
        _pageStore.GetAsync(parentPageNodeId, Arg.Any<CancellationToken>())
            .Returns(pageDto);

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        Assert.That(context.Items[CMSRouteTransformer.PageDataItemKey], Is.SameAs(pageDto));
    }

    [Test]
    public async Task TransformAsync_NonPageType_InvalidParentPageNodeId_NoPageDataLoaded()
    {
        var context = CreateHttpContext();
        var dataTokens = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            { "ParentPageNodeId", "not-a-guid" }
        });
        var match = CreateMatchResult("Widget", "MyWidget", dataTokensJson: dataTokens,
            owningContentNodeId: Guid.NewGuid());
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("MyWidget").Returns(new PageControllerInfo { Name = "MyWidget" });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        Assert.That(context.Items.ContainsKey(CMSRouteTransformer.PageDataItemKey), Is.False);
    }

    [Test]
    public async Task TransformAsync_NonPageType_ParentPageNotVisible_Returns404()
    {
        var context = CreateHttpContext();
        var parentPageNodeId = Guid.NewGuid();
        var dataTokens = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            { "ParentPageNodeId", parentPageNodeId.ToString() }
        });
        var match = CreateMatchResult("Widget", "MyWidget", dataTokensJson: dataTokens,
            owningContentNodeId: Guid.NewGuid());
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("MyWidget").Returns(new PageControllerInfo { Name = "MyWidget" });
        _pageStore.GetAsync(parentPageNodeId, Arg.Any<CancellationToken>()).Returns((PageDTO?)null);

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        // A page hidden by containment (e.g. under an unpublished parent) is a 404, never the bare controller.
        Assert.That(result, Is.Null);
        Assert.That(context.Items.ContainsKey(CMSRouteTransformer.PageDataItemKey), Is.False);
    }

    [Test]
    public async Task TransformAsync_WithConfiguration_DeserializesAndSetsConfig()
    {
        var context = CreateHttpContext();
        var pageNodeId = Guid.NewGuid();
        var config = new SamplePageConfig { Title = "Hello", PageSize = 10 };
        var configJson = JsonSerializer.Serialize(config);
        var dataTokens = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            { "ConfigurationJson", configJson }
        });
        var match = CreateMatchResult("Page", "Configured", dataTokensJson: dataTokens,
            owningContentNodeId: pageNodeId);
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("Configured").Returns(new PageControllerInfo
        {
            Name = "Configured",
            ConfigurationType = typeof(SamplePageConfig)
        });

        var pageDto = new PageDTO
        {
            ControllerName = "Configured",
            ConfigurationJson = configJson
        };
        _pageStore.GetAsync(pageNodeId, Arg.Any<CancellationToken>())
            .Returns(pageDto);

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
        var pageNodeId = Guid.NewGuid();
        var match = CreateMatchResult("Page", "Configured", owningContentNodeId: pageNodeId);
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("Configured").Returns(new PageControllerInfo
        {
            Name = "Configured",
            ConfigurationType = typeof(SamplePageConfig)
        });

        var pageDto = new PageDTO
        {
            ControllerName = "Configured",
            ConfigurationJson = "{ invalid json"
        };
        _pageStore.GetAsync(pageNodeId, Arg.Any<CancellationToken>())
            .Returns(pageDto);

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        var loadedConfig = context.Items[CMSRouteTransformer.PageConfigItemKey];
        Assert.That(loadedConfig, Is.InstanceOf<SamplePageConfig>());
    }

    [Test]
    public async Task TransformAsync_WithConfiguration_NullConfigJson_CreatesDefaultConfig()
    {
        var context = CreateHttpContext();
        var pageNodeId = Guid.NewGuid();
        var match = CreateMatchResult("Page", "Configured", owningContentNodeId: pageNodeId);
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("Configured").Returns(new PageControllerInfo
        {
            Name = "Configured",
            ConfigurationType = typeof(SamplePageConfig)
        });

        var pageDto = new PageDTO
        {
            ControllerName = "Configured",
            ConfigurationJson = null!
        };
        _pageStore.GetAsync(pageNodeId, Arg.Any<CancellationToken>())
            .Returns(pageDto);

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        var loadedConfig = context.Items[CMSRouteTransformer.PageConfigItemKey];
        Assert.That(loadedConfig, Is.InstanceOf<SamplePageConfig>());
    }

    [Test]
    public async Task TransformAsync_WithConfiguration_EmptyConfigJson_CreatesDefaultConfig()
    {
        var context = CreateHttpContext();
        var pageNodeId = Guid.NewGuid();
        var match = CreateMatchResult("Page", "Configured", owningContentNodeId: pageNodeId);
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("Configured").Returns(new PageControllerInfo
        {
            Name = "Configured",
            ConfigurationType = typeof(SamplePageConfig)
        });

        var pageDto = new PageDTO
        {
            ControllerName = "Configured",
            ConfigurationJson = "{}"
        };
        _pageStore.GetAsync(pageNodeId, Arg.Any<CancellationToken>())
            .Returns(pageDto);

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        var loadedConfig = context.Items[CMSRouteTransformer.PageConfigItemKey];
        Assert.That(loadedConfig, Is.InstanceOf<SamplePageConfig>());
    }

    [Test]
    public async Task TransformAsync_WithConfiguration_BlankConfigJson_CreatesDefaultConfig()
    {
        var context = CreateHttpContext();
        var pageNodeId = Guid.NewGuid();
        var match = CreateMatchResult("Page", "Configured", owningContentNodeId: pageNodeId);
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("Configured").Returns(new PageControllerInfo
        {
            Name = "Configured",
            ConfigurationType = typeof(SamplePageConfig)
        });

        var pageDto = new PageDTO
        {
            ControllerName = "Configured",
            ConfigurationJson = ""
        };
        _pageStore.GetAsync(pageNodeId, Arg.Any<CancellationToken>())
            .Returns(pageDto);

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
        var pageNodeId = Guid.NewGuid();
        var match = CreateMatchResult("Page", "Configured", owningContentNodeId: pageNodeId);
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("Configured").Returns(new PageControllerInfo
        {
            Name = "Configured",
            ConfigurationType = typeof(SamplePageConfig)
        });

        var pageDto = new PageDTO
        {
            ControllerName = "Configured",
            ConfigurationJson = "null"
        };
        _pageStore.GetAsync(pageNodeId, Arg.Any<CancellationToken>())
            .Returns(pageDto);

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        var loadedConfig = context.Items[CMSRouteTransformer.PageConfigItemKey];
        Assert.That(loadedConfig, Is.InstanceOf<SamplePageConfig>());
    }

    [Test]
    public async Task TransformAsync_NoPageData_SetsDefaultConfig()
    {
        var context = CreateHttpContext();
        var match = CreateMatchResult("Page", "Configured", owningContentNodeId: null);
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
    public async Task TransformAsync_PageType_NoNodeId_GoesToElseBranch()
    {
        var context = CreateHttpContext();
        var match = CreateMatchResult("Page", "MyPage", owningContentNodeId: null);
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("MyPage").Returns(new PageControllerInfo { Name = "MyPage" });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        Assert.That(result!["controller"], Is.EqualTo("MyPage"));
    }

    [Test]
    public async Task TransformAsync_NonPageType_NoParentPageNodeIdKey_NoPageDataLoaded()
    {
        var context = CreateHttpContext();
        var dataTokens = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            { "SomeOtherKey", "some-value" }
        });
        var match = CreateMatchResult("Widget", "MyWidget", dataTokensJson: dataTokens,
            owningContentNodeId: Guid.NewGuid());
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("MyWidget").Returns(new PageControllerInfo { Name = "MyWidget" });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        Assert.That(context.Items.ContainsKey(CMSRouteTransformer.PageDataItemKey), Is.False);
    }

    [Test]
    public async Task TransformAsync_PageType_PageNotVisible_Returns404()
    {
        var context = CreateHttpContext();
        var pageNodeId = Guid.NewGuid();
        var match = CreateMatchResult("Page", "MyPage", owningContentNodeId: pageNodeId);
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("MyPage").Returns(new PageControllerInfo { Name = "MyPage" });
        _pageStore.GetAsync(pageNodeId, Arg.Any<CancellationToken>()).Returns((PageDTO?)null);

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        // A page hidden by containment (e.g. under an unpublished parent) is a 404, never the bare controller.
        Assert.That(result, Is.Null);
        Assert.That(context.Items.ContainsKey(CMSRouteTransformer.PageDataItemKey), Is.False);
    }

    [Test]
    public void Constructor_NullRouteService_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new CMSRouteTransformer(null!, _registry, _pageStore, _pageHistory, AdminMode));
        Assert.That(ex!.ParamName, Is.EqualTo("routeService"));
    }

    [Test]
    public void Constructor_NullRegistry_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new CMSRouteTransformer(_routeService, null!, _pageStore, _pageHistory, AdminMode));
        Assert.That(ex!.ParamName, Is.EqualTo("registry"));
    }

    [Test]
    public void Constructor_NullPageStore_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new CMSRouteTransformer(_routeService, _registry, null!, _pageHistory, AdminMode));
        Assert.That(ex!.ParamName, Is.EqualTo("pageStore"));
    }

    [Test]
    public void Constructor_NullDeploymentOptions_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new CMSRouteTransformer(_routeService, _registry, _pageStore, _pageHistory, null!));
        Assert.That(ex!.ParamName, Is.EqualTo("deploymentOptions"));
    }

    [Test]
    public async Task TransformAsync_MalformedDefaultsJson_ReturnsNull()
    {
        var context = CreateHttpContext();
        var route = new CMSRouteDTO
        {
            Pattern = "/test",
            DefaultsJson = "{ bad }",
            OwningContentType = "Page"
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
            OwningContentType = "Page"
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
            OwningContentType = "Page"
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
            OwningContentType = "Page"
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

    // ─── preview path ─────────────────────────────────────────────────────────

    [Test]
    public async Task TransformAsync_Preview_ResolvesNeverPublishedDraft()
    {
        var nodeId = Guid.NewGuid();
        var context = CreatePreviewHttpContext($"/_preview/{nodeId}", "Admin");

        var page = new PageDTO { ControllerName = "MyPage", ConfigurationJson = "{}" };
        _pageStore.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _registry.GetByName("MyPage").Returns(new PageControllerInfo { Name = "MyPage" });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!["controller"], Is.EqualTo("MyPage"));
            Assert.That(result["action"], Is.EqualTo("Index"));
            Assert.That(context.Items[CMSRouteTransformer.PageDataItemKey], Is.SameAs(page));
            Assert.That(context.Items["CMS:RouteData"], Is.TypeOf<CMSRouteDTO>());
        });
        await _routeService.DidNotReceive().MatchRouteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TransformAsync_Preview_EditorRole_Resolves()
    {
        var nodeId = Guid.NewGuid();
        var context = CreatePreviewHttpContext($"/_preview/{nodeId}", "Editor");

        var page = new PageDTO { ControllerName = "MyPage", ConfigurationJson = "{}" };
        _pageStore.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _registry.GetByName("MyPage").Returns(new PageControllerInfo { Name = "MyPage" });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        Assert.That(result!["controller"], Is.EqualTo("MyPage"));
    }

    [Test]
    public async Task TransformAsync_Preview_WithConfiguration_SetsConfig()
    {
        var nodeId = Guid.NewGuid();
        var context = CreatePreviewHttpContext($"/_preview/{nodeId}", "Admin");

        var page = new PageDTO { ControllerName = "Configured", ConfigurationJson = "{\"Title\":\"Hello\"}" };
        _pageStore.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _registry.GetByName("Configured").Returns(new PageControllerInfo
        {
            Name = "Configured",
            ConfigurationType = typeof(SamplePageConfig)
        });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        var config = context.Items[CMSRouteTransformer.PageConfigItemKey] as SamplePageConfig;
        Assert.That(config, Is.Not.Null);
    }

    [Test]
    public async Task TransformAsync_Preview_Resolved_SetsDraftRequestFlag()
    {
        var nodeId = Guid.NewGuid();
        var context = CreatePreviewHttpContext($"/_preview/{nodeId}", "Editor");
        _pageStore.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>())
            .Returns(new PageDTO { ControllerName = "MyPage", ConfigurationJson = "{}" });
        _registry.GetByName("MyPage").Returns(new PageControllerInfo { Name = "MyPage" });

        await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(context.Items[PreviewConstants.DraftRequestItemKey], Is.EqualTo(true));
    }

    [TestCase(null)]
    [TestCase("Viewer")]
    [TestCase("Admin")]
    public async Task TransformAsync_Preview_Rejected_DoesNotSetDraftRequestFlag(string? role)
    {
        var nodeId = Guid.NewGuid();
        var context = role == null
            ? CreateHttpContext($"/_preview/{nodeId}")
            : CreatePreviewHttpContext($"/_preview/{nodeId}", role);
        // The Admin case is authorized but has no draft to preview.
        _pageStore.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns((PageDTO?)null);

        await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(context.Items.ContainsKey(PreviewConstants.DraftRequestItemKey), Is.False);
    }

    [Test]
    public async Task TransformAsync_Preview_Unauthenticated_ReturnsNull()
    {
        var nodeId = Guid.NewGuid();
        var context = CreateHttpContext($"/_preview/{nodeId}");

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task TransformAsync_Preview_RenderingOnlyMode_ReturnsNull()
    {
        var transformer = new CMSRouteTransformer(_routeService, _registry, _pageStore, _pageHistory,
            new CmsDeploymentOptions { IsAdminMode = false });
        var nodeId = Guid.NewGuid();
        var context = CreatePreviewHttpContext($"/_preview/{nodeId}", "Admin");

        var result = await transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Null);
        await _pageStore.DidNotReceive().GetCurrentDraftAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        Assert.That(context.Items.ContainsKey(PreviewConstants.DraftRequestItemKey), Is.False);
    }

    [Test]
    public async Task TransformAsync_Preview_AdminModeExplicit_Resolves()
    {
        var transformer = new CMSRouteTransformer(_routeService, _registry, _pageStore, _pageHistory,
            new CmsDeploymentOptions { IsAdminMode = true });
        var nodeId = Guid.NewGuid();
        var context = CreatePreviewHttpContext($"/_preview/{nodeId}", "Admin");
        _pageStore.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>())
            .Returns(new PageDTO { ControllerName = "MyPage", ConfigurationJson = "{}" });
        _registry.GetByName("MyPage").Returns(new PageControllerInfo { Name = "MyPage" });

        var result = await transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Not.Null);
        Assert.That(result!["controller"], Is.EqualTo("MyPage"));
    }

    [Test]
    public async Task TransformAsync_Preview_NonAdminOrEditor_ReturnsNull()
    {
        var nodeId = Guid.NewGuid();
        var context = CreatePreviewHttpContext($"/_preview/{nodeId}", "Viewer");

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task TransformAsync_Preview_PageNotFound_ReturnsNull()
    {
        var nodeId = Guid.NewGuid();
        var context = CreatePreviewHttpContext($"/_preview/{nodeId}", "Admin");
        _pageStore.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns((PageDTO?)null);

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task TransformAsync_Preview_ControllerNotFound_ReturnsNull()
    {
        var nodeId = Guid.NewGuid();
        var context = CreatePreviewHttpContext($"/_preview/{nodeId}", "Admin");
        var page = new PageDTO { ControllerName = "Missing", ConfigurationJson = "{}" };
        _pageStore.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _registry.GetByName("Missing").Returns((PageControllerInfo?)null);

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task TransformAsync_Preview_MalformedNodeId_FallsThrough()
    {
        var context = CreateHttpContext("/_preview/not-a-guid");
        _routeService.MatchRouteAsync("/_preview/not-a-guid", Arg.Any<CancellationToken>()).Returns((CMSRouteMatchResult?)null);

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Null);
        await _pageStore.DidNotReceive().GetCurrentDraftAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TransformAsync_Preview_ExtraSegment_FallsThrough()
    {
        var nodeId = Guid.NewGuid();
        var context = CreateHttpContext($"/_preview/{nodeId}/extra");
        _routeService.MatchRouteAsync($"/_preview/{nodeId}/extra", Arg.Any<CancellationToken>()).Returns((CMSRouteMatchResult?)null);

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Null);
        await _pageStore.DidNotReceive().GetCurrentDraftAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TransformAsync_PageOwner_StashesConfigViaSharedHelper()
    {
        var context = CreateHttpContext();
        var pageNodeId = Guid.NewGuid();
        var configJson = "{\"Title\":\"Hello\",\"PageSize\":10}";
        var match = CreateMatchResult("Page", "Configured", owningContentNodeId: pageNodeId);
        _routeService.MatchRouteAsync("/test", Arg.Any<CancellationToken>()).Returns(match);

        _registry.GetByName("Configured").Returns(new PageControllerInfo
        {
            Name = "Configured",
            ConfigurationType = typeof(SamplePageConfig)
        });

        var pageDto = new PageDTO { ControllerName = "Configured", ConfigurationJson = configJson };
        _pageStore.GetAsync(pageNodeId, Arg.Any<CancellationToken>()).Returns(pageDto);

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(context.Items[CMSRouteTransformer.PageDataItemKey], Is.SameAs(pageDto));
            var config = context.Items[CMSRouteTransformer.PageConfigItemKey] as SamplePageConfig;
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Title, Is.EqualTo("Hello"));
            Assert.That(config.PageSize, Is.EqualTo(10));
        });
    }

    [Test]
    public async Task ResolvePreviewAsync_StashesConfigViaSharedHelper()
    {
        var nodeId = Guid.NewGuid();
        var context = CreatePreviewHttpContext($"/_preview/{nodeId}", "Admin");
        var configJson = "{\"Title\":\"Hello\",\"PageSize\":10}";

        var page = new PageDTO { ControllerName = "Configured", ConfigurationJson = configJson };
        _pageStore.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _registry.GetByName("Configured").Returns(new PageControllerInfo
        {
            Name = "Configured",
            ConfigurationType = typeof(SamplePageConfig)
        });

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(context.Items[CMSRouteTransformer.PageDataItemKey], Is.SameAs(page));
            var config = context.Items[CMSRouteTransformer.PageConfigItemKey] as SamplePageConfig;
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Title, Is.EqualTo("Hello"));
            Assert.That(config.PageSize, Is.EqualTo(10));
        });
    }

    // ─── page-history preview: /_preview/{page}/history/{entry} ──────────────

    [Test]
    public void Constructor_NullPageHistory_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new CMSRouteTransformer(_routeService, _registry, _pageStore, null!, AdminMode));
        Assert.That(ex!.ParamName, Is.EqualTo("pageHistory"));
    }

    /// <summary>Wires an entry for <paramref name="pageNodeId"/> whose page version uses controller "MyPage".</summary>
    private (Guid EntryId, PageDTO Page, ContentReadSnapshot Read) WireHistoryEntry(Guid pageNodeId)
    {
        var entryId = Guid.NewGuid();
        var page = new PageDTO { VersionId = Guid.NewGuid(), ControllerName = "MyPage", ConfigurationJson = "{}" };
        var read = new ContentReadSnapshot([pageNodeId], [page.VersionId]);
        _pageHistory.GetAsync(entryId, Arg.Any<CancellationToken>())
            .Returns(new PageSetSnapshot { Id = entryId, PageNodeId = pageNodeId, PageVersionId = page.VersionId });
        _pageHistory.GetReadSnapshotAsync(entryId, Arg.Any<CancellationToken>()).Returns(read);
        _pageStore.GetVersionAsync(page.VersionId, Arg.Any<CancellationToken>()).Returns(page);
        _registry.GetByName("MyPage").Returns(new PageControllerInfo { Name = "MyPage" });
        return (entryId, page, read);
    }

    [Test]
    public async Task TransformAsync_HistoryPreview_Admin_RendersTheCapturedPageAndMarksTheRequest()
    {
        var pageNodeId = Guid.NewGuid();
        var (entryId, page, read) = WireHistoryEntry(pageNodeId);
        var context = CreatePreviewHttpContext($"/_preview/{pageNodeId}/history/{entryId}", "Admin");

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        var route = (CMSRouteDTO)context.Items["CMS:RouteData"]!;
        Assert.Multiple(() =>
        {
            Assert.That(result["controller"], Is.EqualTo("MyPage"));
            Assert.That(result["action"], Is.EqualTo("Index"));
            Assert.That(context.Items[CMSRouteTransformer.PageDataItemKey], Is.SameAs(page));
            Assert.That(context.Items[PreviewConstants.SnapshotRequestItemKey], Is.SameAs(read));
            Assert.That(context.Items.ContainsKey(PreviewConstants.DraftRequestItemKey), Is.False);
            Assert.That(route.Pattern, Is.EqualTo($"/_preview/{pageNodeId}/history/{entryId}"));
            Assert.That(route.OwningContentNodeId, Is.EqualTo(pageNodeId));
            Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo(PreviewConstants.DraftCacheControl));
        });
    }

    [TestCase(null)]
    [TestCase("Editor")]
    [TestCase("Viewer")]
    public async Task TransformAsync_HistoryPreview_NotAnAdmin_ReturnsNull(string? role)
    {
        var pageNodeId = Guid.NewGuid();
        var (entryId, _, _) = WireHistoryEntry(pageNodeId);
        var path = $"/_preview/{pageNodeId}/history/{entryId}";
        var context = role == null ? CreateHttpContext(path) : CreatePreviewHttpContext(path, role);

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.Null);
            Assert.That(context.Items.ContainsKey(PreviewConstants.SnapshotRequestItemKey), Is.False);
            await _pageHistory.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task TransformAsync_HistoryPreview_RenderingOnlyMode_ReturnsNull()
    {
        var transformer = new CMSRouteTransformer(_routeService, _registry, _pageStore, _pageHistory,
            new CmsDeploymentOptions { IsAdminMode = false });
        var pageNodeId = Guid.NewGuid();
        var (entryId, _, _) = WireHistoryEntry(pageNodeId);
        var context = CreatePreviewHttpContext($"/_preview/{pageNodeId}/history/{entryId}", "Admin");

        var result = await transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Null);
        await _pageHistory.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TransformAsync_HistoryPreview_UnknownOrForeignEntry_ReturnsNull()
    {
        var pageNodeId = Guid.NewGuid();
        var (entryId, _, _) = WireHistoryEntry(pageNodeId);
        _pageHistory.GetAsync(Arg.Is<Guid>(id => id != entryId), Arg.Any<CancellationToken>()).Returns((PageSetSnapshot?)null);

        var unknown = await _transformer.TransformAsync(
            CreatePreviewHttpContext($"/_preview/{pageNodeId}/history/{Guid.NewGuid()}", "Admin"), new RouteValueDictionary());
        var foreign = await _transformer.TransformAsync(
            CreatePreviewHttpContext($"/_preview/{Guid.NewGuid()}/history/{entryId}", "Admin"), new RouteValueDictionary());

        Assert.Multiple(() =>
        {
            Assert.That(unknown, Is.Null);
            Assert.That(foreign, Is.Null);
        });
    }

    [Test]
    public async Task TransformAsync_HistoryPreview_ControllerNotFound_ReturnsNull()
    {
        var pageNodeId = Guid.NewGuid();
        var (entryId, _, _) = WireHistoryEntry(pageNodeId);
        _registry.GetByName("MyPage").Returns((PageControllerInfo?)null);
        var context = CreatePreviewHttpContext($"/_preview/{pageNodeId}/history/{entryId}", "Admin");

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(context.Items.ContainsKey(PreviewConstants.SnapshotRequestItemKey), Is.False);
        });
    }

    [TestCase("/_preview/{0}/history/not-a-guid")]
    [TestCase("/_preview/not-a-guid/history/{0}")]
    [TestCase("/_preview/{0}/versions/{0}")]
    [TestCase("/elsewhere/{0}/history/{0}")]
    [TestCase("/_preview/{0}/history/{0}/extra")]
    public async Task TransformAsync_NotAHistoryPreviewPath_FallsThrough(string pattern)
    {
        var path = string.Format(pattern, Guid.NewGuid());
        var context = CreatePreviewHttpContext(path, "Admin");
        _routeService.MatchRouteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((CMSRouteMatchResult?)null);

        var result = await _transformer.TransformAsync(context, new RouteValueDictionary());

        Assert.That(result, Is.Null);
        await _pageHistory.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _routeService.Received(1).MatchRouteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
