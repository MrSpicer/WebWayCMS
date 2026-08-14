using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Interfaces;
using WebWayCMS.Services;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class RouteRegistrationServiceTests
{
    private ICMSRouteService _routeService = null!;
    private RouteRegistrationService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _routeService = Substitute.For<ICMSRouteService>();
        _service = new RouteRegistrationService(_routeService, Array.Empty<IRoutableViewComponent>());
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
    public async Task UnregisterContentRoutesAsync_DeactivatesRoutes()
    {
        await _service.UnregisterContentRoutesAsync(Guid.NewGuid());

        await _routeService.Received(1).DeleteByOwningContentAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_Inactive_ReturnsSuccessTuple()
    {
        var result = await _service.TryRegisterWidgetRoutesAsync("Widget", Guid.NewGuid(), Guid.NewGuid(), false);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ErrorMessage, Is.Null);
        await _routeService.Received(1).DeleteByOwningContentAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_NoParentPageNode_ReturnsSuccessTuple()
    {
        var result = await _service.TryRegisterWidgetRoutesAsync("Widget", Guid.NewGuid(), null, true);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ErrorMessage, Is.Null);
        await _routeService.DidNotReceive().GetByOwningContentAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_NoMatchingWidget_ReturnsSuccessTuple()
    {
        var result = await _service.TryRegisterWidgetRoutesAsync("NonExistent", Guid.NewGuid(), Guid.NewGuid(), true);

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

        var service = new RouteRegistrationService(_routeService, new[] { routable });
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>());

        var result = await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ErrorMessage, Is.Null);
        await routable.DidNotReceive().GenerateRoutesAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_RegistrationFails_PropagatesFailureTuple()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { new() { Pattern = "/article" } });

        var service = new RouteRegistrationService(_routeService, new[] { routable });
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns((false, "collision", null));

        var result = await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true);

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
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { new() { Pattern = "/article" } });

        var service = new RouteRegistrationService(_routeService, new[] { routable });
        var contentZoneItemNodeId = Guid.NewGuid();
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        var result = await service.RegisterWidgetRoutesAsync(
            routable, contentZoneItemNodeId, "/page", "{}", Guid.NewGuid());

        Assert.That(result.Success, Is.True);
        await _routeService.Received(1).DeleteByOwningContentAsync(
            contentZoneItemNodeId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_GeneratesWidgetRoutes()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/article" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable });
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true);

        await routable.Received(1).GenerateRoutesAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.Pattern == "/page/article"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_MultiRouteWidget_UpsertsAllRoutes()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/a" },
                new() { Pattern = "/b" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable });
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true);

        await _routeService.Received(2).UpsertAsync(
            Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>());
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
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { new() { Pattern = "/article" } });

        var service = new RouteRegistrationService(_routeService, new[] { routable });
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns((false, "collision", null));

        var result = await service.RegisterWidgetRoutesAsync(
            routable, Guid.NewGuid(), "/page", "{}", Guid.NewGuid());

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
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/article", OwningContentType = "Custom" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable });
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true);

        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.OwningContentType == "Custom"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_WidgetPatternWithoutLeadingSlash_Normalizes()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "article" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable });
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true);

        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.Pattern == "/page/article"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_RootParentRoute_NormalizesCorrectly()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Widget");
        routable.GenerateRoutesAsync("/", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/article" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable });
        var pageRoute = new CMSRouteDTO { Pattern = "/", DefaultsJson = "{}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Widget", Guid.NewGuid(), Guid.NewGuid(), true);

        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.Pattern == "/article"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_EmptyWidgetPattern_NormalizesToSlash()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Widget");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable });
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Widget", Guid.NewGuid(), Guid.NewGuid(), true);

        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.Pattern == "/page"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_TrailingSlashInWidgetPattern_Trimmed()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Widget");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/article/" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable });
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Widget", Guid.NewGuid(), Guid.NewGuid(), true);

        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.Pattern == "/page/article"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_EmptyDefaultsJson_TryDeserializeReturnsNull()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/article" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable });
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true);

        await _routeService.Received(1).UpsertAsync(
            Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_InvalidDefaultsJson_TryDeserializeCatchesAndReturnsNull()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/article" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable });
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{invalid" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true);

        await _routeService.Received(1).UpsertAsync(
            Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_InvalidWidgetRouteDefaultsJson_TryDeserializeCatches()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/article", DefaultsJson = "{invalid" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable });
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{\"controller\":\"Test\"}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true);

        await _routeService.Received(1).UpsertAsync(
            Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_ValidDefaultsJson_TryDeserializeSucceeds()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/article", DefaultsJson = "{\"action\":\"Detail\"}" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable });
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{\"controller\":\"Test\"}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true);

        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.DefaultsJson!.Contains("\"action\"") && r.DefaultsJson.Contains("\"controller\"")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_InvalidDataTokensJson_TryDeserializeCatches()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/article", DataTokensJson = "{invalid" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable });
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true);

        await _routeService.Received(1).UpsertAsync(
            Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_EmptyWidgetDataTokensJson_TryDeserializeReturnsNull()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/article", DataTokensJson = "{}" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable });
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = "{}" };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true);

        await _routeService.Received(1).UpsertAsync(
            Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void Constructor_NullRouteService_Throws()
    {
        Assert.That(
            () => new RouteRegistrationService(null!, Array.Empty<IRoutableViewComponent>()),
            Throws.ArgumentNullException);
    }

    [Test]
    public void Constructor_NullWidgets_Throws()
    {
        Assert.That(
            () => new RouteRegistrationService(Substitute.For<ICMSRouteService>(), null!),
            Throws.ArgumentNullException);
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
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/article" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable });
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = null! };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true);

        await _routeService.Received(1).UpsertAsync(
            Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_EmptyStringDefaultsJson_TryDeserializeReturnsNull()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");
        routable.GenerateRoutesAsync("/page", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new() { Pattern = "/article" }
            });

        var service = new RouteRegistrationService(_routeService, new[] { routable });
        var pageRoute = new CMSRouteDTO { Pattern = "/page", DefaultsJson = string.Empty };
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { pageRoute });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true);

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
