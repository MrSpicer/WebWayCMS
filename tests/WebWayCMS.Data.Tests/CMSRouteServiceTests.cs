using Microsoft.EntityFrameworkCore;

using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class CMSRouteServiceTests
{
    private string _db = null!;

    [SetUp]
    public void SetUp() => _db = TestContexts.NewDb();

    private CmsDbContext NewContext() => TestContexts.Cms(_db);

    private CMSRouteService NewService() => new(NewContext());

    private async Task SeedAsync(params CMSRouteDTO[] routes)
    {
        await using var ctx = NewContext();
        ctx.Set<CMSRouteDTO>().AddRange(routes);
        await ctx.SaveChangesAsync();
    }

    private static CMSRouteDTO RouteRow(string pattern, bool isPublished = true,
        bool isDeleted = false, bool isReserved = false,
        Guid? masterId = null, int version = 0,
        Guid? owningContentMasterId = null)
    {
        var mId = masterId ?? Guid.NewGuid();
        var id = Guid.NewGuid();
        return new CMSRouteDTO
        {
            ContentId = id,
            Pattern = pattern,
            IsReserved = isReserved,
            OwningContentType = "Page",
            OwningContentMasterId = owningContentMasterId,
            ContentMeta = new ContentDTO
            {
                Id = id,
                MasterId = mId,
                Version = version,
                IsPublished = isPublished,
                IsDeleted = isDeleted,
                Title = pattern,
                Slug = pattern.TrimStart('/')
            }
        };
    }

    [Test]
    public void Constructor_NullContext_Throws()
    {
        Assert.That(() => new CMSRouteService(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task MatchRouteAsync_ReservedRoute_ReturnsNull()
    {
        await SeedAsync(RouteRow("/test", isReserved: true));

        var result = await NewService().MatchRouteAsync("/test");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task MatchRouteAsync_ReservedAndNonReserved_ReturnsNonReserved()
    {
        await SeedAsync(
            RouteRow("/test", isReserved: true, masterId: Guid.NewGuid()),
            RouteRow("/test-nonreserved", isReserved: false));

        var result = await NewService().MatchRouteAsync("/test-nonreserved");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Route.Pattern, Is.EqualTo("/test-nonreserved"));
        Assert.That(result.Route.IsReserved, Is.False);
    }

    [Test]
    public async Task MatchRouteAsync_ReservedRouteIsSkipped_NonReservedRouteForOtherPatternStillMatches()
    {
        await SeedAsync(
            RouteRow("/reserved", isReserved: true),
            RouteRow("/available", isReserved: false));

        var result = await NewService().MatchRouteAsync("/available");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Route.Pattern, Is.EqualTo("/available"));
        Assert.That(result.Route.IsReserved, Is.False);
    }

    [Test]
    public async Task MatchRouteAsync_NoMatch_ReturnsNull()
    {
        await SeedAsync(RouteRow("/test", isReserved: true));

        var result = await NewService().MatchRouteAsync("/nonexistent");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task IsPatternAvailableAsync_ReservedRoute_ReturnsFalse()
    {
        await SeedAsync(RouteRow("/test", isReserved: true));

        var available = await NewService().IsPatternAvailableAsync("/test");

        Assert.That(available, Is.False);
    }

    [Test]
    public async Task IsPatternAvailableAsync_NonReservedRoute_ReturnsFalse()
    {
        await SeedAsync(RouteRow("/test", isReserved: false));

        var available = await NewService().IsPatternAvailableAsync("/test");

        Assert.That(available, Is.False);
    }

    [Test]
    public async Task IsPatternAvailableAsync_NoConflictingRoute_ReturnsTrue()
    {
        await SeedAsync(RouteRow("/existing"));

        var available = await NewService().IsPatternAvailableAsync("/new");

        Assert.That(available, Is.True);
    }

    [Test]
    public async Task IsPatternAvailableAsync_ExcludeMasterId_AllowsSelf()
    {
        var owningMasterId = Guid.NewGuid();
        await SeedAsync(RouteRow("/test", owningContentMasterId: owningMasterId, isReserved: true));

        var available = await NewService().IsPatternAvailableAsync("/test", excludeMasterId: owningMasterId);

        Assert.That(available, Is.True);
    }

    [Test]
    public async Task GetActiveRoutesAsync_ReturnsReservedRoutes()
    {
        await SeedAsync(RouteRow("/test", isReserved: true));

        var routes = await NewService().GetActiveRoutesAsync();

        Assert.That(routes, Has.Count.EqualTo(1));
        Assert.That(routes[0].IsReserved, Is.True);
    }

    [Test]
    public async Task UpsertAsync_StoresIsReserved()
    {
        var route = new CMSRouteDTO
        {
            Pattern = "/test",
            IsReserved = true,
            ContentMeta = new ContentDTO
            {
                Id = Guid.NewGuid(),
                MasterId = Guid.NewGuid(),
                Version = 0,
                IsPublished = true,
                Title = "test",
                Slug = "test"
            }
        };

        await NewService().UpsertAsync(route);

        await using var verify = NewContext();
        var stored = await verify.Set<CMSRouteDTO>().FirstAsync(r => r.Pattern == "/test");
        Assert.That(stored.IsReserved, Is.True);
    }

    [Test]
    public async Task GetByIdAsync_ReturnsIsReserved()
    {
        var route = RouteRow("/test", isReserved: true);
        await SeedAsync(route);

        var result = await NewService().GetByIdAsync(route.ContentId);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.IsReserved, Is.True);
    }

    [Test]
    public async Task DeleteAsync_RemovesRoute()
    {
        var route = RouteRow("/test");
        await SeedAsync(route);

        var deleted = await NewService().DeleteAsync(route.ContentId);

        Assert.That(deleted, Is.True);
        await using var verify = NewContext();
        Assert.That(await verify.Set<CMSRouteDTO>().AnyAsync(r => r.ContentId == route.ContentId), Is.False);
    }

    [Test]
    public async Task DeleteAsync_NotFound_ReturnsFalse()
    {
        Assert.That(await NewService().DeleteAsync(Guid.NewGuid()), Is.False);
    }

    [Test]
    public async Task DeactivateByOwningContentAsync_FoundRoute_Deactivates()
    {
        var owningMasterId = Guid.NewGuid();
        await SeedAsync(RouteRow("/test", owningContentMasterId: owningMasterId, isReserved: true));

        var deactivated = await NewService().DeactivateByOwningContentAsync(owningMasterId);

        Assert.That(deactivated, Is.True);
    }

    [Test]
    public async Task DeactivateByOwningContentAsync_NoRoutes_ReturnsFalse()
    {
        Assert.That(await NewService().DeactivateByOwningContentAsync(Guid.NewGuid()), Is.False);
    }

    [Test]
    public async Task GetByOwningContentAsync_ReturnsRoutes()
    {
        var owningMasterId = Guid.NewGuid();
        await SeedAsync(RouteRow("/test", owningContentMasterId: owningMasterId, isReserved: true));

        var routes = await NewService().GetByOwningContentAsync(owningMasterId);

        Assert.That(routes, Has.Count.EqualTo(1));
        Assert.That(routes[0].IsReserved, Is.True);
    }
}