using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class CMSRouteRegistryTests
{
    private ICMSRouteService _routeService = null!;
    private CMSRouteRegistry _registry = null!;

    private static CMSRouteDTO RouteRow(string pattern) => new()
    {
        Pattern = pattern
    };

    [SetUp]
    public void SetUp()
    {
        _routeService = Substitute.For<ICMSRouteService>();

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(ICMSRouteService)).Returns(_routeService);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        _registry = new CMSRouteRegistry(scopeFactory);
    }

    [Test]
    public void GetActiveRoutes_ReturnsRoutesFromDatabase()
    {
        var routes = new List<CMSRouteDTO> { RouteRow("/test"), RouteRow("/other") };
        _routeService.GetActiveRoutesAsync(Arg.Any<CancellationToken>()).Returns(routes);

        var result = _registry.GetActiveRoutes();

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].Pattern, Is.EqualTo("/test"));
        Assert.That(result[1].Pattern, Is.EqualTo("/other"));
    }

    [Test]
    public void GetActiveRoutes_LoadsOnlyOnce_WithinTtl()
    {
        var routes = new List<CMSRouteDTO> { RouteRow("/test") };
        _routeService.GetActiveRoutesAsync(Arg.Any<CancellationToken>()).Returns(routes);

        _registry.GetActiveRoutes();
        _registry.GetActiveRoutes();

        _routeService.Received(1).GetActiveRoutesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public void GetActiveRoutes_EmptyList_DoesNotRequeryOnGetActiveRoutes()
    {
        _routeService.GetActiveRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());

        _registry.GetActiveRoutes();
        _registry.GetActiveRoutes();

        _routeService.Received(1).GetActiveRoutesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public void Invalidate_ForcesReloadOnNextGetActiveRoutes()
    {
        var first = new List<CMSRouteDTO> { RouteRow("/first") };
        var second = new List<CMSRouteDTO> { RouteRow("/second") };
        _routeService.GetActiveRoutesAsync(Arg.Any<CancellationToken>()).Returns(first, second);

        var result1 = _registry.GetActiveRoutes();
        Assert.That(result1[0].Pattern, Is.EqualTo("/first"));

        _registry.Invalidate();

        var result2 = _registry.GetActiveRoutes();
        Assert.That(result2[0].Pattern, Is.EqualTo("/second"));
    }

    [Test]
    public void GetActiveRoutes_ServesStaleDataOnFailure_AfterInitialLoad()
    {
        var routes = new List<CMSRouteDTO> { RouteRow("/cached") };
        _routeService.GetActiveRoutesAsync(Arg.Any<CancellationToken>()).Returns(routes);

        _registry.GetActiveRoutes();

        _routeService.GetActiveRoutesAsync(Arg.Any<CancellationToken>()).Returns<List<CMSRouteDTO>>(
            x => throw new InvalidOperationException("db down"));

        _registry.Invalidate();

        var result = _registry.GetActiveRoutes();
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Pattern, Is.EqualTo("/cached"));
    }

    [Test]
    public void Constructor_NullScopeFactory_Throws()
    {
        Assert.That(() => new CMSRouteRegistry(null!), Throws.ArgumentNullException);
    }
}
