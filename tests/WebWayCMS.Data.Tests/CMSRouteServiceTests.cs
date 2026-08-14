using Microsoft.EntityFrameworkCore;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class CMSRouteServiceTests
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

    private async Task SeedAsync(params CMSRouteDTO[] routes)
    {
        await using var ctx = NewContext();
        ctx.Set<CMSRouteDTO>().AddRange(routes);
        await ctx.SaveChangesAsync();

        var active = await ctx.Set<CMSRouteDTO>()
            .OrderBy(r => r.Order)
            .ThenBy(r => r.Pattern.Length)
            .ToListAsync();
        _registry.GetActiveRoutes().Returns(active);
    }

    private static CMSRouteDTO RouteRow(string pattern, bool isReserved = false,
        Guid? id = null, Guid? owningContentNodeId = null, int order = 0) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Pattern = pattern,
        IsReserved = isReserved,
        OwningContentType = "Page",
        OwningContentNodeId = owningContentNodeId,
        Order = order
    };

    [Test]
    public void Constructor_NullContext_Throws()
    {
        Assert.That(() => new CMSRouteService(null!, _registry), Throws.ArgumentNullException);
    }

    [Test]
    public void Constructor_NullRegistry_Throws()
    {
        Assert.That(() => new CMSRouteService(NewContext(), null!), Throws.ArgumentNullException);
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
            RouteRow("/test", isReserved: true, id: Guid.NewGuid()),
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
    public async Task MatchRouteAsync_NonReservedRoute_NonMatchingPath_ReturnsNull()
    {
        await SeedAsync(RouteRow("/test", isReserved: false));

        var result = await NewService().MatchRouteAsync("/other");

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
    public async Task IsPatternAvailableAsync_ExcludeNodeId_AllowsSelf()
    {
        var owningNodeId = Guid.NewGuid();
        await SeedAsync(RouteRow("/test", owningContentNodeId: owningNodeId, isReserved: true));

        var available = await NewService().IsPatternAvailableAsync("/test", excludeNodeId: owningNodeId);

        Assert.That(available, Is.True);
    }

    [Test]
    public async Task IsPatternAvailableAsync_ExcludeRouteId_AllowsSelf()
    {
        var route = RouteRow("/test", isReserved: true);
        await SeedAsync(route);

        var available = await NewService().IsPatternAvailableAsync("/test", excludeRouteId: route.Id);

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
    public async Task GetAllRoutesAsync_ReturnsAllRoutes()
    {
        await SeedAsync(RouteRow("/a"), RouteRow("/b"));

        var routes = await NewService().GetAllRoutesAsync();

        Assert.That(routes, Has.Count.EqualTo(2));
    }

    [Test]
    public void UpsertAsync_Null_Throws()
    {
        Assert.That(async () => await NewService().UpsertAsync(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task UpsertAsync_StoresIsReserved()
    {
        var route = new CMSRouteDTO
        {
            Pattern = "/test",
            IsReserved = true
        };

        await NewService().UpsertAsync(route);

        _registry.Received().Invalidate();

        await using var verify = NewContext();
        var stored = await verify.Set<CMSRouteDTO>().FirstAsync(r => r.Pattern == "/test");
        Assert.That(stored.IsReserved, Is.True);
    }

    [Test]
    public async Task UpsertAsync_ExistingRoute_ReplacesIt()
    {
        var owningNodeId = Guid.NewGuid();
        var existing = RouteRow("/test", owningContentNodeId: owningNodeId, id: Guid.NewGuid());
        await SeedAsync(existing);

        var replacement = new CMSRouteDTO
        {
            Pattern = "/test",
            OwningContentNodeId = owningNodeId,
            IsReserved = true
        };

        await NewService().UpsertAsync(replacement);

        _registry.Received().Invalidate();

        var routes = await NewService().GetByOwningContentAsync(owningNodeId);
        Assert.That(routes, Has.Count.EqualTo(1));
        Assert.That(routes[0].IsReserved, Is.True);
    }

    [Test]
    public async Task UpsertAsync_PresetId_IsKept()
    {
        var presetId = Guid.NewGuid();
        var route = new CMSRouteDTO { Id = presetId, Pattern = "/preset" };

        await NewService().UpsertAsync(route);

        await using var verify = NewContext();
        var stored = await verify.Set<CMSRouteDTO>().SingleAsync(r => r.Id == presetId);
        Assert.That(stored.Id, Is.EqualTo(presetId));
    }

    [Test]
    public async Task GetByIdAsync_ReturnsIsReserved()
    {
        var route = RouteRow("/test", isReserved: true);
        await SeedAsync(route);

        var result = await NewService().GetByIdAsync(route.Id);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.IsReserved, Is.True);
    }

    [Test]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        Assert.That(await NewService().GetByIdAsync(Guid.NewGuid()), Is.Null);
    }

    [Test]
    public async Task DeleteAsync_RemovesRoute()
    {
        var route = RouteRow("/test");
        await SeedAsync(route);

        var deleted = await NewService().DeleteAsync(route.Id);

        _registry.Received().Invalidate();

        Assert.That(deleted, Is.True);
        await using var verify = NewContext();
        Assert.That(await verify.Set<CMSRouteDTO>().AnyAsync(r => r.Id == route.Id), Is.False);
    }

    [Test]
    public async Task DeleteAsync_NotFound_ReturnsFalse()
    {
        Assert.That(await NewService().DeleteAsync(Guid.NewGuid()), Is.False);
    }

    [Test]
    public async Task DeleteByOwningContentAsync_FoundRoute_Deletes()
    {
        var owningNodeId = Guid.NewGuid();
        await SeedAsync(RouteRow("/test", owningContentNodeId: owningNodeId, isReserved: true));

        var deleted = await NewService().DeleteByOwningContentAsync(owningNodeId);

        _registry.Received().Invalidate();

        Assert.That(deleted, Is.True);
    }

    [Test]
    public async Task DeleteByOwningContentAsync_NoRoutes_ReturnsFalse()
    {
        Assert.That(await NewService().DeleteByOwningContentAsync(Guid.NewGuid()), Is.False);
    }

    [Test]
    public async Task GetByOwningContentAsync_ReturnsRoutes()
    {
        var owningNodeId = Guid.NewGuid();
        await SeedAsync(RouteRow("/test", owningContentNodeId: owningNodeId, isReserved: true));

        var routes = await NewService().GetByOwningContentAsync(owningNodeId);

        Assert.That(routes, Has.Count.EqualTo(1));
        Assert.That(routes[0].IsReserved, Is.True);
    }

    [TestCase("/", "/")]
    [TestCase("/test", "/test")]
    [TestCase("/TEST", "/test")]
    [TestCase("", "/")]
    [TestCase("  ", "/")]
    [TestCase("/test/", "/test")]
    [TestCase("test", "/test")]
    [TestCase("TEST", "/test")]
    public void NormalizePattern_NormalizesCorrectly(string input, string expected)
    {
        var result = CMSRouteService.NormalizePattern(input);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void TryMatchPattern_ExactMatch_ReturnsEmptyDictionary()
    {
        var result = CMSRouteService.TryMatchPattern("/test", "/test");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!, Is.Empty);
    }

    [Test]
    public void TryMatchPattern_NoBrace_NoMatch_ReturnsNull()
    {
        var result = CMSRouteService.TryMatchPattern("/test", "/other");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryMatchPattern_SimpleParameter_ReturnsParamValue()
    {
        var result = CMSRouteService.TryMatchPattern("/{slug}", "/hello");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!["slug"], Is.EqualTo("hello"));
    }

    [Test]
    public void TryMatchPattern_MultipleParameters_ReturnsAll()
    {
        var result = CMSRouteService.TryMatchPattern("/{category}/{slug}", "/news/article1");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!["category"], Is.EqualTo("news"));
        Assert.That(result["slug"], Is.EqualTo("article1"));
    }

    [Test]
    public void TryMatchPattern_CatchAll_ReturnsRemaining()
    {
        var result = CMSRouteService.TryMatchPattern("/docs/{**slug}", "/docs/a/b/c");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!["slug"], Is.EqualTo("a/b/c"));
    }

    [Test]
    public void TryMatchPattern_OptionalParameter_MatchesWhenPresent()
    {
        var result = CMSRouteService.TryMatchPattern("/page/{id?}", "/page/42");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!["id"], Is.EqualTo("42"));
    }

    [Test]
    public void TryMatchPattern_OptionalParameter_MatchesWhenMissing()
    {
        var result = CMSRouteService.TryMatchPattern("/page/{id?}", "/page");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!, Does.Not.ContainKey("id"));
    }

    [Test]
    public void TryMatchPattern_IntConstraint_MatchesDigits()
    {
        var result = CMSRouteService.TryMatchPattern("/product/{id:int}", "/product/99");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!["id"], Is.EqualTo("99"));
    }

    [Test]
    public void TryMatchPattern_IntConstraint_RejectsNonDigits()
    {
        var result = CMSRouteService.TryMatchPattern("/product/{id:int}", "/product/abc");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryMatchPattern_RegexConstraint_MatchesPattern()
    {
        var result = CMSRouteService.TryMatchPattern("/code/{token:regex(^[a-z]+$)}", "/code/hello");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!["token"], Is.EqualTo("hello"));
    }

    [Test]
    public void TryMatchPattern_RegexConstraint_RejectsNonMatching()
    {
        var result = CMSRouteService.TryMatchPattern("/code/{token:regex(^[a-z]+$)}", "/code/123");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryMatchPattern_GuidConstraint_MatchesGuid()
    {
        var id = Guid.NewGuid().ToString();
        var result = CMSRouteService.TryMatchPattern("/item/{id:guid}", $"/item/{id}");
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public void TryMatchPattern_GuidConstraint_RejectsNonGuid()
    {
        var result = CMSRouteService.TryMatchPattern("/item/{id:guid}", "/item/not-a-guid");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryMatchPattern_BoolConstraint_MatchesTrue()
    {
        var result = CMSRouteService.TryMatchPattern("/flag/{val:bool}", "/flag/true");
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public void TryMatchPattern_SegmentCountMismatch_ReturnsNull()
    {
        var result = CMSRouteService.TryMatchPattern("/a/{b}", "/a/b/c");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryMatchPattern_OptionalWithConstraintMismatch_SkipsParam()
    {
        var result = CMSRouteService.TryMatchPattern("/item/{id:int?}", "/item");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!, Does.Not.ContainKey("id"));
    }

    [Test]
    public void TryMatchPattern_LiteralAndParamPrefix_Match()
    {
        var result = CMSRouteService.TryMatchPattern("/p{page:int}", "/p42");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!["page"], Is.EqualTo("42"));
    }

    [Test]
    public void TryMatchPattern_LiteralAndParamSuffix_Match()
    {
        var result = CMSRouteService.TryMatchPattern("/{slug}.html", "/article.html");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!["slug"], Is.EqualTo("article"));
    }

    [Test]
    public void TryMatchPattern_ExtraPatternSegmentsWithOptionals_AllowsMissing()
    {
        var result = CMSRouteService.TryMatchPattern("/a/{b?}/{c?}", "/a");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!, Is.Empty);
    }

    [Test]
    public void TryMatchPattern_InvalidRegex_ReturnsNull()
    {
        var result = CMSRouteService.TryMatchPattern("/code/{token:regex(***)}", "/code/abc");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryMatchPattern_LiteralPrefixNoMatch_ReturnsNull()
    {
        var result = CMSRouteService.TryMatchPattern("/x{id:int}", "/y42");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryMatchPattern_LiteralSuffixNoMatch_ReturnsNull()
    {
        var result = CMSRouteService.TryMatchPattern("/{slug}.htm", "/article.html");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryMatchPattern_LiteralExactMatch_ReturnsEmpty()
    {
        var result = CMSRouteService.TryMatchPattern("/hello", "/hello");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!, Is.Empty);
    }

    [Test]
    public void TryMatchPattern_LiteralParamPrefixRegex_Matches()
    {
        var result = CMSRouteService.TryMatchPattern("/p{page:regex(^\\d+$)}", "/p42");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!["page"], Is.EqualTo("42"));
    }

    [Test]
    public void TryMatchPattern_LiteralParamPrefixRegex_RejectsNonMatch()
    {
        var result = CMSRouteService.TryMatchPattern("/p{page:regex(^\\d+$)}", "/pabc");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryMatchPattern_UnbalancedBrace_ReturnsNull()
    {
        var result = CMSRouteService.TryMatchPattern("/bad{slug", "/badsomething");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryMatchPattern_OptionalWithConstraintFails_Continues()
    {
        var result = CMSRouteService.TryMatchPattern("/item/{id:int?}", "/item/abc");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!, Does.Not.ContainKey("id"));
    }

    [Test]
    public void TryMatchPattern_OptionalWithEmptyPathSeg_Continues()
    {
        var result = CMSRouteService.TryMatchPattern("/{id?}", "/");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!, Does.Not.ContainKey("id"));
    }

    [Test]
    public void TryMatchPattern_CatchAll_NoExtraPathSegments_ReturnsEmpty()
    {
        var result = CMSRouteService.TryMatchPattern("/docs/{**slug}", "/docs");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!, Is.Empty);
    }

    [Test]
    public void TryMatchPattern_ExtraRequiredSegmentAfterOptional_ReturnsNull()
    {
        var result = CMSRouteService.TryMatchPattern("/a/{b?}/{c}", "/a");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryMatchPattern_UnknownConstraint_AcceptsValue()
    {
        var result = CMSRouteService.TryMatchPattern("/item/{id:unknown}", "/item/value");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!["id"], Is.EqualTo("value"));
    }

    [Test]
    public void TryMatchPattern_LiteralPrefixParam_NoConstraint_Matches()
    {
        var result = CMSRouteService.TryMatchPattern("/p{page}", "/p42");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!["page"], Is.EqualTo("42"));
    }

    [Test]
    public void TryMatchPattern_LiteralPrefixParam_ConstraintFails_ReturnsNull()
    {
        var result = CMSRouteService.TryMatchPattern("/p{id:int}", "/pabc");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryMatchPattern_LiteralSuffixParam_WithConstraint_Matches()
    {
        var result = CMSRouteService.TryMatchPattern("/{id:int}.html", "/42.html");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!["id"], Is.EqualTo("42"));
    }

    [Test]
    public void TryMatchPattern_LiteralSuffixParam_ConstraintFails_ReturnsNull()
    {
        var result = CMSRouteService.TryMatchPattern("/{id:int}.html", "/abc.html");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryMatchPattern_ComplexLiteralParamMismatch_ReturnsNull()
    {
        var result = CMSRouteService.TryMatchPattern("/a{b}c", "/aXc");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryMatchPattern_UnclosedBraceInLiteralParam_ReturnsNull()
    {
        var result = CMSRouteService.TryMatchPattern("/a{b}c{d", "/aXcY");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void NormalizePattern_HandlesNull_ReturnsSlash()
    {
        var result = CMSRouteService.NormalizePattern(null!);
        Assert.That(result, Is.EqualTo("/"));
    }
}
