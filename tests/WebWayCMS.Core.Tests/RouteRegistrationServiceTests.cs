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
            new TestRoutableContent(), "", "TestCtrl", null, null, true);

        await _routeService.DidNotReceive().UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RegisterContentRoutesAsync_ValidPattern_CreatesRoute()
    {
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => x.Arg<CMSRouteDTO>());

        await _service.RegisterContentRoutesAsync(
            new TestRoutableContent(), "/test", "TestCtrl", null, null, true);

        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.Pattern == "/test"
                && !r.DataTokensJson!.Contains("ConfigurationJson")
                && r.DataTokensJson.Contains("RouteContentType")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RegisterContentRoutesAsync_NotPublished_SetsIsPublishedFalse()
    {
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => x.Arg<CMSRouteDTO>());

        await _service.RegisterContentRoutesAsync(
            new TestRoutableContent(), "/draft", "TestCtrl", null, null, false);

        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.ContentMeta.IsPublished == false),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnregisterContentRoutesAsync_DeactivatesRoutes()
    {
        await _service.UnregisterContentRoutesAsync(Guid.NewGuid());

        await _routeService.Received(1).DeactivateByOwningContentAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_NoParentPageMaster_ReturnsEarly()
    {
        await _service.TryRegisterWidgetRoutesAsync("Widget", Guid.NewGuid(), null, false);

        await _routeService.DidNotReceive().GetByOwningContentAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_NoMatchingWidget_ReturnsEarly()
    {
        await _service.TryRegisterWidgetRoutesAsync("NonExistent", Guid.NewGuid(), Guid.NewGuid(), false);

        await _routeService.DidNotReceive().GetByOwningContentAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryRegisterWidgetRoutesAsync_NoPageRoute_ReturnsEarly()
    {
        var routable = Substitute.For<IRoutableViewComponent>();
        routable.ComponentName.Returns("Article");

        var service = new RouteRegistrationService(_routeService, new[] { routable });
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>());

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), false);

        await routable.DidNotReceive().GenerateRoutesAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
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
            .Returns(x => x.Arg<CMSRouteDTO>());

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true);

        await routable.Received(1).GenerateRoutesAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.Pattern == "/page/article"),
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
            .Returns(x => x.Arg<CMSRouteDTO>());

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
            .Returns(x => x.Arg<CMSRouteDTO>());

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
            .Returns(x => x.Arg<CMSRouteDTO>());

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
            .Returns(x => x.Arg<CMSRouteDTO>());

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
            .Returns(x => x.Arg<CMSRouteDTO>());

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
            .Returns(x => x.Arg<CMSRouteDTO>());

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
            .Returns(x => x.Arg<CMSRouteDTO>());

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
            .Returns(x => x.Arg<CMSRouteDTO>());

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
            .Returns(x => x.Arg<CMSRouteDTO>());

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
            .Returns(x => x.Arg<CMSRouteDTO>());

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
            .Returns(x => x.Arg<CMSRouteDTO>());

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
            .Returns(x => x.Arg<CMSRouteDTO>());

        await service.TryRegisterWidgetRoutesAsync("Article", Guid.NewGuid(), Guid.NewGuid(), true);

        await _routeService.Received(1).UpsertAsync(
            Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void Constructor_NullWidgets_Throws()
    {
        Assert.That(
            () => new RouteRegistrationService(Substitute.For<ICMSRouteService>(), null!),
            Throws.ArgumentNullException);
    }

    private sealed class TestRoutableContent : IRoutableContent
    {
        public string RouteContentType => "TestType";
        public Task<IReadOnlyList<CMSRouteDTO>> GetRoutesAsync(Guid contentMasterId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<CMSRouteDTO>>(Array.Empty<CMSRouteDTO>());
    }
}
