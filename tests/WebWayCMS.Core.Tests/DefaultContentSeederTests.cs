using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Services;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class DefaultContentSeederTests
{
    private IContentStore<PageDTO> _pageStore = null!;
    private ICMSRouteService _routeService = null!;
    private DefaultContentSeeder _seeder = null!;

    [SetUp]
    public void SetUp()
    {
        _pageStore = Substitute.For<IContentStore<PageDTO>>();
        _routeService = Substitute.For<ICMSRouteService>();
        _seeder = new DefaultContentSeeder(_pageStore, _routeService);
    }

    private void StubSaveToAssignNode()
    {
        _pageStore.SaveDraftAsync(Arg.Any<PageDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true))
            .AndDoes(c => c.Arg<PageDTO>().Version.Node = new ContentNode { Id = Guid.NewGuid() });
        _pageStore.PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));
    }

    [Test]
    public void Constructor_NullPageStore_Throws()
    {
        Assert.That(
            () => new DefaultContentSeeder(null!, Substitute.For<ICMSRouteService>()),
            Throws.ArgumentNullException);
    }

    [Test]
    public void Constructor_NullRouteService_Throws()
    {
        Assert.That(
            () => new DefaultContentSeeder(Substitute.For<IContentStore<PageDTO>>(), null!),
            Throws.ArgumentNullException);
    }

    [Test]
    public async Task SeedDefaultPages_WhenNoHome_SeedsHomePage()
    {
        _routeService.MatchRouteAsync("/", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CMSRouteMatchResult?>(null));
        StubSaveToAssignNode();

        await _seeder.SeedDefaultPagesAsync(false);

        await _pageStore.Received(1).SaveDraftAsync(Arg.Any<PageDTO>(), null, Arg.Any<CancellationToken>());
        await _pageStore.Received(1).PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _routeService.Received(1).UpsertAsync(Arg.Is<CMSRouteDTO>(r => r.Pattern == "/"), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SeedDefaultPages_WhenHomeExists_SkipsSeeding()
    {
        _routeService.MatchRouteAsync("/", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CMSRouteMatchResult?>(new CMSRouteMatchResult()));

        await _seeder.SeedDefaultPagesAsync(false);

        await _pageStore.DidNotReceive().SaveDraftAsync(Arg.Any<PageDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SeedDefaultPages_SeedAdminPageFalse_DoesNotSeedAdmin()
    {
        _routeService.MatchRouteAsync("/", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CMSRouteMatchResult?>(new CMSRouteMatchResult()));

        await _seeder.SeedDefaultPagesAsync(false);

        await _routeService.DidNotReceive().MatchRouteAsync("/wadmin", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SeedDefaultPages_SeedAdminPageTrue_NoAdminRoute_SeedsAdminPage()
    {
        _routeService.MatchRouteAsync("/", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CMSRouteMatchResult?>(new CMSRouteMatchResult()));
        _routeService.MatchRouteAsync("/wadmin", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CMSRouteMatchResult?>(null));
        StubSaveToAssignNode();

        await _seeder.SeedDefaultPagesAsync(true);

        await _pageStore.Received(1).SaveDraftAsync(Arg.Any<PageDTO>(), null, Arg.Any<CancellationToken>());
        await _routeService.Received(1).UpsertAsync(Arg.Is<CMSRouteDTO>(r => r.Pattern == "/wadmin"), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SeedDefaultPages_SeedAdminPageTrue_AdminRouteExists_SkipsAdminSeeding()
    {
        _routeService.MatchRouteAsync("/", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CMSRouteMatchResult?>(new CMSRouteMatchResult()));
        _routeService.MatchRouteAsync("/wadmin", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CMSRouteMatchResult?>(new CMSRouteMatchResult()));

        await _seeder.SeedDefaultPagesAsync(true);

        await _pageStore.DidNotReceive().SaveDraftAsync(Arg.Any<PageDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
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
