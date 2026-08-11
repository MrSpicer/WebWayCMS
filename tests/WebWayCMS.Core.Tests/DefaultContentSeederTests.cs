using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Services;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class DefaultContentSeederTests
{
    private IPageService _pageService = null!;
    private ICMSRouteService _routeService = null!;
    private DefaultContentSeeder _seeder = null!;

    [SetUp]
    public void SetUp()
    {
        _pageService = Substitute.For<IPageService>();
        _routeService = Substitute.For<ICMSRouteService>();
        _seeder = new DefaultContentSeeder(_pageService, _routeService);
    }

    [Test]
    public void Constructor_NullPageService_Throws()
    {
        Assert.That(
            () => new DefaultContentSeeder(null!, Substitute.For<ICMSRouteService>()),
            Throws.ArgumentNullException);
    }

    [Test]
    public void Constructor_NullRouteService_Throws()
    {
        Assert.That(
            () => new DefaultContentSeeder(Substitute.For<IPageService>(), null!),
            Throws.ArgumentNullException);
    }

    [Test]
    public async Task SeedDefaultPages_WhenNoHome_SeedsHomePage()
    {
        _routeService.MatchRouteAsync("/", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CMSRouteMatchResult?>(null));
        var created = new PageDTO { ContentMeta = new ContentDTO { Id = Guid.NewGuid(), MasterId = Guid.NewGuid(), Title = "Home" } };
        _pageService.CreateAsync(Arg.Any<PageDTO>(), Arg.Any<CancellationToken>()).Returns(created);

        await _seeder.SeedDefaultPagesAsync(false);

        await _pageService.Received(1).CreateAsync(Arg.Any<PageDTO>(), Arg.Any<CancellationToken>());
        await _routeService.Received(1).UpsertAsync(Arg.Is<CMSRouteDTO>(r => r.Pattern == "/"), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SeedDefaultPages_WhenHomeExists_SkipsSeeding()
    {
        _routeService.MatchRouteAsync("/", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CMSRouteMatchResult?>(new CMSRouteMatchResult()));

        await _seeder.SeedDefaultPagesAsync(false);

        await _pageService.DidNotReceive().CreateAsync(Arg.Any<PageDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SeedDefaultPages_SeedAdminPageFalse_DoesNotSeedAdmin()
    {
        _routeService.MatchRouteAsync("/", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CMSRouteMatchResult?>(new CMSRouteMatchResult()));

        await _seeder.SeedDefaultPagesAsync(false);

        await _routeService.DidNotReceive().MatchRouteAsync("/admin", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SeedDefaultPages_SeedAdminPageTrue_NoAdminRoute_SeedsAdminPage()
    {
        _routeService.MatchRouteAsync("/", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CMSRouteMatchResult?>(new CMSRouteMatchResult()));
        _routeService.MatchRouteAsync("/admin", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CMSRouteMatchResult?>(null));
        var created = new PageDTO { ContentMeta = new ContentDTO { Id = Guid.NewGuid(), MasterId = Guid.NewGuid(), Title = "Admin" } };
        _pageService.CreateAsync(Arg.Any<PageDTO>(), Arg.Any<CancellationToken>()).Returns(created);

        await _seeder.SeedDefaultPagesAsync(true);

        await _pageService.Received(1).CreateAsync(Arg.Any<PageDTO>(), Arg.Any<CancellationToken>());
        await _routeService.Received(1).UpsertAsync(Arg.Is<CMSRouteDTO>(r => r.Pattern == "/admin"), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SeedDefaultPages_SeedAdminPageTrue_AdminRouteExists_SkipsAdminSeeding()
    {
        _routeService.MatchRouteAsync("/", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CMSRouteMatchResult?>(new CMSRouteMatchResult()));
        _routeService.MatchRouteAsync("/admin", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CMSRouteMatchResult?>(new CMSRouteMatchResult()));

        await _seeder.SeedDefaultPagesAsync(true);

        await _pageService.DidNotReceive().CreateAsync(Arg.Any<PageDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SeedDefaultPages_SkipEnvVar_ReturnsImmediately()
    {
        var previous = Environment.GetEnvironmentVariable("WEBWAYCMS_SKIP_DEFAULTPAGE");
        Environment.SetEnvironmentVariable("WEBWAYCMS_SKIP_DEFAULTPAGE", "true");
        try
        {
            await _seeder.SeedDefaultPagesAsync(true);

            await _routeService.DidNotReceive().MatchRouteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("WEBWAYCMS_SKIP_DEFAULTPAGE", previous);
        }
    }
}
