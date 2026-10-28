using Microsoft.EntityFrameworkCore;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class RouteProjectionDataTests
{
    private string _db = null!;
    private ICMSRouteRegistry _registry = null!;

    [SetUp]
    public void SetUp()
    {
        _db = TestContexts.NewDb();
        _registry = Substitute.For<ICMSRouteRegistry>();
    }

    private CmsDbContext NewContext() => TestContexts.Cms(_db);

    private CMSRouteService NewService() => new(NewContext(), _registry);

    private static CMSRouteDTO RouteRow(string pattern, Guid? routeNodeId = null, Guid? owner = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Pattern = pattern,
            OwningContentType = "Page",
            OwningContentNodeId = owner,
            RouteNodeId = routeNodeId
        };

    [Test]
    public void Validate_ReservedAndNormalize()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RoutePatternRules.Normalize(" Docs/ "), Is.EqualTo("/docs"));
            Assert.That(RoutePatternRules.Normalize(null), Is.EqualTo("/"));
            Assert.That(RoutePatternRules.Validate("/{action}"), Does.Contain("reserves"));
            Assert.That(RoutePatternRules.Validate("/docs/{page}"), Does.Contain("reserves"));
            Assert.That(RoutePatternRules.Validate("/docs/{pageNumber}"), Is.Null);
        });
    }

    [Test]
    public async Task Upsert_ByRouteNode_ReplacesTheProjectedRowOnAPatternChange()
    {
        var routeNodeId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        await using (var ctx = NewContext())
        {
            ctx.Set<CMSRouteDTO>().Add(RouteRow("/old", routeNodeId, owner));
            await ctx.SaveChangesAsync();
        }

        var service = NewService();
        var result = await service.UpsertAsync(RouteRow("/new", routeNodeId, owner));

        Assert.That(result.Success, Is.True);
        await using var check = NewContext();
        var rows = await check.Set<CMSRouteDTO>().ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Pattern, Is.EqualTo("/new"));
            Assert.That(rows[0].RouteNodeId, Is.EqualTo(routeNodeId));
        });
    }

    [Test]
    public async Task DeleteByRouteNode_RemovesOnlyThatProjection()
    {
        var routeNodeId = Guid.NewGuid();
        await using (var ctx = NewContext())
        {
            ctx.Set<CMSRouteDTO>().AddRange(RouteRow("/a", routeNodeId), RouteRow("/b", Guid.NewGuid()));
            await ctx.SaveChangesAsync();
        }

        var service = NewService();
        var deleted = await service.DeleteByRouteNodeAsync(routeNodeId);
        var missing = await service.DeleteByRouteNodeAsync(Guid.NewGuid());

        await using var check = NewContext();
        var rows = await check.Set<CMSRouteDTO>().ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.True);
            Assert.That(missing, Is.False);
            Assert.That(rows.Select(r => r.Pattern), Is.EqualTo(new[] { "/b" }));
        });
    }
}
