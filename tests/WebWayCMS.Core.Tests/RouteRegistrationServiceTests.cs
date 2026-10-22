using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Interfaces;
using WebWayCMS.Services;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class RouteRegistrationServiceTests
{
    private ICMSRouteService _routeService = null!;
    private IWidgetRegistry _widgetRegistry = null!;
    private RouteRegistrationService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _routeService = Substitute.For<ICMSRouteService>();
        _widgetRegistry = Substitute.For<IWidgetRegistry>();
        _service = new RouteRegistrationService(_routeService, Array.Empty<IRoutableViewComponent>(), _widgetRegistry);
    }

    [Test]
    public async Task RegisterContentRoutesAsync_EmptyPattern_DoesNothing()
    {
        await _service.RegisterContentRoutesAsync(
            new TestRoutableContent(), "", "TestCtrl", Guid.NewGuid());

        await _routeService.DidNotReceive().UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RegisterContentRoutesAsync_ValidPattern_CreatesRoute()
    {
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await _service.RegisterContentRoutesAsync(
            new TestRoutableContent(), "/test", "TestCtrl", Guid.NewGuid());

        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.Pattern == "/test"
                && !r.DataTokensJson!.Contains("ConfigurationJson")
                && r.DataTokensJson.Contains("RouteContentType")
                && r.DefaultsJson.Contains("controller")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RegisterContentRoutesAsync_OverlongNavigationName_IsTruncatedToColumnLength()
    {
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        // A page title allows far more characters than the NavigationName column stores; letting one
        // through would fail the insert, which UpsertAsync can only report as a pattern collision.
        var longName = new string('x', CMSRouteDTO.NavigationNameMaxLength + 1);

        await _service.RegisterContentRoutesAsync(
            new TestRoutableContent(), "/test", "TestCtrl", Guid.NewGuid(), longName);

        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.NavigationName!.Length == CMSRouteDTO.NavigationNameMaxLength),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RegisterContentRoutesAsync_NavigationNameAtColumnLength_IsKeptWhole()
    {
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        var name = new string('x', CMSRouteDTO.NavigationNameMaxLength);

        await _service.RegisterContentRoutesAsync(
            new TestRoutableContent(), "/test", "TestCtrl", Guid.NewGuid(), name);

        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.NavigationName == name), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnregisterContentRoutesAsync_DeactivatesRoutes()
    {
        await _service.UnregisterContentRoutesAsync(Guid.NewGuid());

        await _routeService.Received(1).DeleteByOwningContentAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_Inactive_ReturnsSuccessTuple()
    {
        var result = await _service.TryRegisterWidgetRoutesAsync("Widget", Guid.NewGuid(), Guid.NewGuid(), false, null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ErrorMessage, Is.Null);
        await _routeService.Received(1).DeleteByOwningContentAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_NoParentPageNode_ReturnsSuccessTuple()
    {
        var result = await _service.TryRegisterWidgetRoutesAsync("Widget", Guid.NewGuid(), null, true, null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ErrorMessage, Is.Null);
        await _routeService.DidNotReceive().GetByOwningContentAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_NoMatchingWidget_ReturnsSuccessTuple()
    {
        var result = await _service.TryRegisterWidgetRoutesAsync("NonExistent", Guid.NewGuid(), Guid.NewGuid(), true, null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ErrorMessage, Is.Null);
        await _routeService.DidNotReceive().GetByOwningContentAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_NoPageRoute_ReturnsSuccessTuple()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");

        var service = new RouteRegistrationService(_routeService, new[] { routable }, _widgetRegistry);
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>());

        var result = await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true, null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ErrorMessage, Is.Null);
        await routable.DidNotReceive().GenerateRoutesAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<WidgetDisplayMode>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_RegistrationFails_PropagatesFailureTuple()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<WidgetDisplayMode>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { new() { Pattern = "/article" } });

        var service = new RouteRegistrationService(_routeService, new[] { routable }, _widgetRegistry);
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns((false, "collision", null));

        var result = await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true, null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("collision"));
        });
    }

    [Test]
    public async Task RegisterWidgetRoutesAsync_ExistingStaleRouteForOwner_DeletedBeforeReRegistering()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<WidgetDisplayMode>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { new() { Pattern = "/article" } });

        var service = new RouteRegistrationService(_routeService, new[] { routable }, _widgetRegistry);
        var contentZoneItemNodeId = Guid.NewGuid();
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        var result = await service.RegisterWidgetRoutesAsync(
            routable, contentZoneItemNodeId, "/page", "{}", Guid.NewGuid(), WidgetDisplayMode.Single);

        Assert.That(result.Success, Is.True);
        await _routeService.Received(1).DeleteByOwningContentAsync(
            contentZoneItemNodeId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_GeneratesWidgetRoutes()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<WidgetDisplayMode>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/article" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable }, _widgetRegistry);
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true, null);

        await routable.Received(1).GenerateRoutesAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<WidgetDisplayMode>(), Arg.Any<CancellationToken>());
        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.Pattern == "/page/article"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_MultiRouteWidget_UpsertsAllRoutes()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<WidgetDisplayMode>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/a" },
                new() { Pattern = "/b" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable }, _widgetRegistry);
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true, null);

        await _routeService.Received(2).UpsertAsync(
            Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RegisterContentRoutesAsync_NavigationName_ReachesTheRoute()
    {
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await _service.RegisterContentRoutesAsync(
            new TestRoutableContent(), "/test", "TestCtrl", Guid.NewGuid(), "About Us");

        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.NavigationName == "About Us"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RegisterContentRoutesAsync_NoNavigationName_LeavesItNull()
    {
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await _service.RegisterContentRoutesAsync(
            new TestRoutableContent(), "/test", "TestCtrl", Guid.NewGuid());

        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.NavigationName == null),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RegisterContentRoutesAsync_UpsertFailure_Propagates()
    {
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns((false, "collision", null));

        var result = await _service.RegisterContentRoutesAsync(
            new TestRoutableContent(), "/test", "TestCtrl", Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("collision"));
        });
    }

    [Test]
    public async Task RegisterWidgetRoutesAsync_UpsertFailure_Propagates()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<WidgetDisplayMode>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { new() { Pattern = "/article" } });

        var service = new RouteRegistrationService(_routeService, new[] { routable }, _widgetRegistry);
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns((false, "collision", null));

        var result = await service.RegisterWidgetRoutesAsync(
            routable, Guid.NewGuid(), "/page", "{}", Guid.NewGuid(), WidgetDisplayMode.Single);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("collision"));
        });
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_OwnsContentTypePreserved()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<WidgetDisplayMode>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/article", OwningContentType = "Custom" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable }, _widgetRegistry);
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true, null);

        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.OwningContentType == "Custom"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_WidgetPatternWithoutLeadingSlash_Normalizes()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<WidgetDisplayMode>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "article" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable }, _widgetRegistry);
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true, null);

        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.Pattern == "/page/article"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_RootParentRoute_NormalizesCorrectly()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Widget");
        routable.GenerateRoutesAsync("/", Arg.Any<Guid>(), Arg.Any<WidgetDisplayMode>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/article" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable }, _widgetRegistry);
        var pageRoute = new CMSRouteDTO { Pattern = "/", DefaultsJson = "{}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Widget", Guid.NewGuid(), Guid.NewGuid(), true, null);

        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.Pattern == "/article"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_EmptyWidgetPattern_NormalizesToSlash()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Widget");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<WidgetDisplayMode>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable }, _widgetRegistry);
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Widget", Guid.NewGuid(), Guid.NewGuid(), true, null);

        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.Pattern == "/page"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_TrailingSlashInWidgetPattern_Trimmed()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Widget");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<WidgetDisplayMode>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/article/" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable }, _widgetRegistry);
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Widget", Guid.NewGuid(), Guid.NewGuid(), true, null);

        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.Pattern == "/page/article"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_EmptyDefaultsJson_TryDeserializeReturnsNull()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<WidgetDisplayMode>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/article" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable }, _widgetRegistry);
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true, null);

        await _routeService.Received(1).UpsertAsync(
            Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_InvalidDefaultsJson_TryDeserializeCatchesAndReturnsNull()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<WidgetDisplayMode>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/article" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable }, _widgetRegistry);
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{invalid" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true, null);

        await _routeService.Received(1).UpsertAsync(
            Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_InvalidWidgetRouteDefaultsJson_TryDeserializeCatches()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<WidgetDisplayMode>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/article", DefaultsJson = "{invalid" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable }, _widgetRegistry);
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{\"controller\":\"Test\"}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true, null);

        await _routeService.Received(1).UpsertAsync(
            Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_ValidDefaultsJson_TryDeserializeSucceeds()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<WidgetDisplayMode>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/article", DefaultsJson = "{\"action\":\"Detail\"}" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable }, _widgetRegistry);
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{\"controller\":\"Test\"}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true, null);

        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.DefaultsJson!.Contains("\"action\"") && r.DefaultsJson.Contains("\"controller\"")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_InvalidDataTokensJson_TryDeserializeCatches()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<WidgetDisplayMode>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/article", DataTokensJson = "{invalid" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable }, _widgetRegistry);
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true, null);

        await _routeService.Received(1).UpsertAsync(
            Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_EmptyWidgetDataTokensJson_TryDeserializeReturnsNull()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<WidgetDisplayMode>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/article", DataTokensJson = "{}" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable }, _widgetRegistry);
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true, null);

        await _routeService.Received(1).UpsertAsync(
            Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void Constructor_NullRouteService_Throws()
    {
        Assert.That(
            () => new RouteRegistrationService(null!, Array.Empty<IRoutableViewComponent>(), _widgetRegistry),
            Throws.ArgumentNullException);
    }

    [Test]
    public void Constructor_NullWidgets_Throws()
    {
        Assert.That(
            () => new RouteRegistrationService(Substitute.For<ICMSRouteService>(), null!, _widgetRegistry),
            Throws.ArgumentNullException);
    }

    [Test]
    public void Constructor_NullWidgetRegistry_Throws()
    {
        Assert.That(
            () => new RouteRegistrationService(Substitute.For<ICMSRouteService>(), Array.Empty<IRoutableViewComponent>(), null!),
            Throws.ArgumentNullException);
    }

    private const string ListMetadata = "{\"displayMode\":\"List\"}";

    /// <summary>A routable widget named "Article" on a page routed at /page, with route upserts succeeding.</summary>
    private (RouteRegistrationService Service, IRoutableViewComponent Widget) ArrangeRoutableArticle(bool supportsList)
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<WidgetDisplayMode>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { new() { Pattern = "{slug}" } });
        _widgetRegistry.GetByName("Article").Returns(new WidgetRegistrationInfo { Name = "Article", SupportsList = supportsList });
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { new() { Pattern = "/page", DefaultsJson = "{}" } });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));
        return (new RouteRegistrationService(_routeService, new[] { routable }, _widgetRegistry), routable);
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_StoredListOnListCapableWidget_GeneratesForList()
    {
        var (service, widget) = ArrangeRoutableArticle(supportsList: true);

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true, ListMetadata);

        await widget.Received(1).GenerateRoutesAsync("/page", Arg.Any<Guid>(), WidgetDisplayMode.List, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_StoredListOnNonListWidget_GeneratesForSingle()
    {
        // A grandfathered List on a widget that no longer supports lists renders (and so routes) as Single.
        var (service, widget) = ArrangeRoutableArticle(supportsList: false);

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true, ListMetadata);

        await widget.Received(1).GenerateRoutesAsync("/page", Arg.Any<Guid>(), WidgetDisplayMode.Single, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_StoredListOnUnregisteredWidget_GeneratesForSingle()
    {
        var (service, widget) = ArrangeRoutableArticle(supportsList: true);
        _widgetRegistry.GetByName("Article").Returns((WidgetRegistrationInfo?)null);

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true, ListMetadata);

        await widget.Received(1).GenerateRoutesAsync("/page", Arg.Any<Guid>(), WidgetDisplayMode.Single, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_NoStoredDisplayMode_GeneratesForSingle()
    {
        var (service, widget) = ArrangeRoutableArticle(supportsList: true);

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true, "{}");

        await widget.Received(1).GenerateRoutesAsync("/page", Arg.Any<Guid>(), WidgetDisplayMode.Single, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_WidgetLeavesOwningTypeUnset_DefaultsToComponentNameWidget()
    {
        var (service, _) = ArrangeRoutableArticle(supportsList: true);

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true, null);

        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.OwningContentType == "ArticleWidget"), Arg.Any<CancellationToken>());
    }

    [Test]
    public void NormalizePattern_Whitespace_ReturnsSlash()
    {
        var method = typeof(RouteRegistrationService).GetMethod("NormalizePattern",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.That(method, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(method.Invoke(null, new object[] { " " }), Is.EqualTo("/"));
            Assert.That(method.Invoke(null, new object[] { null! }), Is.EqualTo("/"));
            Assert.That(method.Invoke(null, new object[] { "" }), Is.EqualTo("/"));
        });
    }

    [Test]
    public void NormalizePattern_NoLeadingSlash_PrependsSlash()
    {
        var method = typeof(RouteRegistrationService).GetMethod("NormalizePattern",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        Assert.That(method.Invoke(null, new object[] { "page" }), Is.EqualTo("/page"));
    }

    [Test]
    public void NormalizePattern_TrailingSlash_Trims()
    {
        var method = typeof(RouteRegistrationService).GetMethod("NormalizePattern",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        Assert.That(method.Invoke(null, new object[] { "/page/" }), Is.EqualTo("/page"));
    }

    [Test]
    public void NormalizePattern_SingleSlash_ReturnsSlash()
    {
        var method = typeof(RouteRegistrationService).GetMethod("NormalizePattern",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        Assert.That(method.Invoke(null, new object[] { "/" }), Is.EqualTo("/"));
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_NullDefaultsJson_TryDeserializeReturnsNull()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<WidgetDisplayMode>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/article" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable }, _widgetRegistry);
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = null! };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true, null);

        await _routeService.Received(1).UpsertAsync(
            Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_EmptyStringDefaultsJson_TryDeserializeReturnsNull()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<WidgetDisplayMode>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/article" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable }, _widgetRegistry);
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = string.Empty };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true, null);

        await _routeService.Received(1).UpsertAsync(
            Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>());
    }

    private sealed class TestRoutableContent : IRoutableContent
    {
        public string RouteContentType => "TestType";
        public Task<IReadOnlyList<CMSRouteDTO>> GetRoutesAsync(Guid contentNodeId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<CMSRouteDTO>>(Array.Empty<CMSRouteDTO>());
    }
}
