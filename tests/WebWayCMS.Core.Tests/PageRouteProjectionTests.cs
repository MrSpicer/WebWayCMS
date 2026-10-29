using Microsoft.AspNetCore.Http;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.Page;
using WebWayCMS.Pages;
using WebWayCMS.Services;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class PageRouteProjectionTests
{
    private IContentStore<PageDTO> _store = null!;
    private IContentStore<RouteDTO> _routeStore = null!;
    private ICMSRouteService _routeService = null!;
    private IRouteRegistrationService _routeRegistration = null!;
    private IContentZoneService _zones = null!;
    private IPageHistoryService _history = null!;
    private ICmsTransaction _transaction = null!;
    private ICMSRouteRegistry _routeRegistry = null!;
    private PageModel _model = null!;

    [SetUp]
    public void SetUp()
    {
        _store = Substitute.For<IContentStore<PageDTO>>();
        _routeStore = Substitute.For<IContentStore<RouteDTO>>();
        _routeService = Substitute.For<ICMSRouteService>();
        _routeRegistration = Substitute.For<IRouteRegistrationService>();
        _zones = Substitute.For<IContentZoneService>();
        _history = Substitute.For<IPageHistoryService>();
        _transaction = TestSupport.PassThroughTransaction();
        _routeRegistry = Substitute.For<ICMSRouteRegistry>();

        _store.PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true, VersionId: Guid.NewGuid()));
        _store.FindCurrentDraftsRelatedAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<PageDTO>());
        _store.FindCurrentDraftsWithoutRelationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<PageDTO>());
        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<PageDTO>());
        _routeStore.SaveDraftAsync(Arg.Any<RouteDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true, NodeId: Guid.NewGuid()));
        _routeStore.PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true, VersionId: Guid.NewGuid()));
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(ci => (true, (string?)null, (CMSRouteDTO?)ci.Arg<CMSRouteDTO>()));
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>());
        _routeService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _zones.PublishPageZonesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new PageZonePublishResult([], []));

        _model = new PageModel(_store, TestSupport.CreateMapper(),
            Substitute.For<IPageControllerRegistry>(), Substitute.For<IViewDiscoveryService>(),
            _routeRegistration, _routeService, _zones, _history, _routeStore, _transaction, _routeRegistry,
            Substitute.For<IChangeSetScope>(), TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes());
    }

    private static PageDTO Page(Guid? nodeId = null, Guid? parentId = null, string slug = "docs",
        string controller = "GenericPage", params Guid[] routeIds)
    {
        var id = nodeId ?? Guid.NewGuid();
        var page = new PageDTO
        {
            ControllerName = controller,
            ConfigurationJson = "{}",
            Version = new ContentVersion
            {
                Node = new ContentNode { Id = id },
                Title = "Docs",
                Slug = slug,
                VersionNumber = 1,
                State = ContentVersionState.Draft,
                IsCurrentDraft = true
            }
        };
        page.Version.Relations = routeIds.Select((r, i) => new ContentRelation
        {
            Name = PageModel.RouteRelation,
            TargetNodeId = r,
            Ordinal = i,
            Kind = RelationKind.Owned
        }).ToList();
        if (parentId.HasValue)
            page.Version.Relations.Add(new ContentRelation { Name = PageModel.ParentRelation, TargetNodeId = parentId.Value, Kind = RelationKind.Containment });
        return page;
    }

    private static RouteDTO Route(Guid id, RouteMode mode, string pattern = "", string? nav = "Nav")
        => new()
        {
            Mode = mode,
            Pattern = pattern,
            NavigationName = nav,
            Version = new ContentVersion
            {
                Node = new ContentNode { Id = id },
                Title = nav ?? string.Empty,
                VersionNumber = 1,
                State = ContentVersionState.Published
            }
        };

    [Test]
    public async Task Publish_ExplicitAndDerivedRoutes_AreProjected()
    {
        var pageId = Guid.NewGuid();
        var explicitId = Guid.NewGuid();
        var derivedId = Guid.NewGuid();
        var page = Page(pageId, routeIds: [explicitId, derivedId]);
        _store.GetCurrentDraftAsync(pageId, Arg.Any<CancellationToken>()).Returns(page);
        _routeStore.GetCurrentDraftAsync(explicitId, Arg.Any<CancellationToken>())
            .Returns(Route(explicitId, RouteMode.Explicit, "/documentation"));
        _routeStore.GetCurrentDraftAsync(derivedId, Arg.Any<CancellationToken>())
            .Returns(Route(derivedId, RouteMode.Derived, nav: new string('x', 300)));

        var result = await _model.PublishPageAsync(pageId);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.DescendantsUpdated, Is.EqualTo(0));
        });
        await _routeService.Received().UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.Pattern == "/documentation" && r.RouteNodeId == explicitId),
            Arg.Any<CancellationToken>());
        await _routeService.Received().UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.Pattern == "/docs" && r.RouteNodeId == derivedId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Publish_DerivedPrefixChange_ReprojectsLiveDescendants()
    {
        var pageId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var childRouteId = Guid.NewGuid();
        var page = Page(pageId, routeIds: [routeId]);
        var child = Page(childId, pageId, "intro", routeIds: [childRouteId]);

        _store.GetCurrentDraftAsync(pageId, Arg.Any<CancellationToken>()).Returns(page);
        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { page, child });
        _routeStore.GetCurrentDraftAsync(routeId, Arg.Any<CancellationToken>())
            .Returns(Route(routeId, RouteMode.Derived));
        _routeStore.GetCurrentDraftAsync(childRouteId, Arg.Any<CancellationToken>())
            .Returns(Route(childRouteId, RouteMode.Derived));
        _routeService.GetByOwningContentAsync(pageId, Arg.Any<CancellationToken>())
            .Returns(
                new List<CMSRouteDTO> { new() { Pattern = "/old", RouteNodeId = routeId, OwningContentNodeId = pageId } },
                new List<CMSRouteDTO> { new() { Pattern = "/docs", RouteNodeId = routeId, OwningContentNodeId = pageId } });
        // Re-projection reads the child's published page and route versions.
        child.Version.State = ContentVersionState.Published;
        _store.GetAllVersionsAsync(childId, Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { child });
        _routeStore.GetAllVersionsAsync(childRouteId, Arg.Any<CancellationToken>())
            .Returns(new List<RouteDTO> { Route(childRouteId, RouteMode.Derived) });
        _routeService.GetByOwningContentAsync(childId, Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { new() { Pattern = "/old/intro", RouteNodeId = childRouteId } });
        _routeService.GetByOwningContentAsync(Arg.Is<Guid>(g => g != pageId && g != childId), Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { new() { Pattern = "/docs" } });

        var result = await _model.PublishPageAsync(pageId);

        Assert.Multiple(() =>
        {
            Assert.That(result.DescendantsUpdated, Is.EqualTo(1));
            Assert.That(result.ErrorMessage, Does.Contain("1 child URL"));
        });
        await _routeService.Received().UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.Pattern == "/docs/intro" && r.RouteNodeId == childRouteId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Publish_RemovedRoute_DeletesProjectionAndRouteItem()
    {
        var pageId = Guid.NewGuid();
        var currentRouteId = Guid.NewGuid();
        var page = Page(pageId, routeIds: [currentRouteId]);
        var oldRouteId = Guid.NewGuid();
        _store.GetCurrentDraftAsync(pageId, Arg.Any<CancellationToken>()).Returns(page);
        _routeStore.GetCurrentDraftAsync(currentRouteId, Arg.Any<CancellationToken>())
            .Returns(Route(currentRouteId, RouteMode.Explicit, "/new"));
        _routeService.GetByOwningContentAsync(pageId, Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { new() { Pattern = "/old", RouteNodeId = oldRouteId } });

        var result = await _model.PublishPageAsync(pageId);

        Assert.That(result.Success, Is.True);
        await _routeStore.Received(1).DeleteAsync(oldRouteId, false, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Publish_ProjectionFailure_RevertsAndRestoresPreviousRoute()
    {
        var pageId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        var page = Page(pageId, routeIds: [routeId]);
        _store.GetCurrentDraftAsync(pageId, Arg.Any<CancellationToken>()).Returns(page);
        _routeStore.GetCurrentDraftAsync(routeId, Arg.Any<CancellationToken>())
            .Returns(Route(routeId, RouteMode.Explicit, "/taken"));
        var previous = new CMSRouteDTO { Pattern = "/old", RouteNodeId = routeId };
        _routeService.GetByOwningContentAsync(pageId, Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { previous });
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns((false, "This route pattern is already in use.", (CMSRouteDTO?)null));
        var outcomes = CaptureTransactionOutcomes();

        var result = await _model.PublishPageAsync(pageId);

        // The page publish and the projection share one transaction, so the rollback (the work
        // returning false) is what restores the previous state: no compensating writes.
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("already in use"));
            Assert.That(outcomes, Is.EqualTo(new[] { false }));
        });
        await _store.Received(1).PublishAsync(pageId, Arg.Any<CancellationToken>());
        await _store.DidNotReceiveWithAnyArgs().RevertPublishAsync(default, default, default, default);
        _routeRegistry.Received(1).Invalidate();
    }

    /// <summary>Runs the transaction's work and records what it returned (false = rolled back).</summary>
    private List<bool> CaptureTransactionOutcomes()
    {
        var outcomes = new List<bool>();
        _transaction.RunAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(async c =>
            {
                var committed = await c.Arg<Func<CancellationToken, Task<bool>>>()(CancellationToken.None);
                outcomes.Add(committed);
                return committed;
            });
        return outcomes;
    }

    [Test]
    public async Task CreatePage_MintsADerivedRoute()
    {
        _store.SaveDraftAsync(Arg.Any<PageDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true, NodeId: Guid.NewGuid()));
        var model = new PageModel(_store, TestSupport.CreateMapper(),
            ControllerRegistry(), Substitute.For<IViewDiscoveryService>(), _routeRegistration,
            _routeService, _zones, _history, _routeStore, _transaction, _routeRegistry, Substitute.For<IChangeSetScope>(),
            TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes());

        var result = await model.SaveUpsertAsync(new PageUpsertViewModel { Title = "Docs", ControllerName = "GenericPage" });

        Assert.That(result.Success, Is.True);
        await _routeStore.Received(1).SaveDraftAsync(
            Arg.Is<RouteDTO>(r => r.Mode == RouteMode.Derived), null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreatePage_MintFailure_ReturnsTheError()
    {
        _routeStore.SaveDraftAsync(Arg.Any<RouteDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(false, "route failed"));
        var model = new PageModel(_store, TestSupport.CreateMapper(),
            ControllerRegistry(), Substitute.For<IViewDiscoveryService>(), _routeRegistration,
            _routeService, _zones, _history, _routeStore, _transaction, _routeRegistry, Substitute.For<IChangeSetScope>(),
            TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes());

        var result = await model.SaveUpsertAsync(new PageUpsertViewModel { Title = "Docs", ControllerName = "GenericPage" });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("route failed"));
        });
    }

    // ─── phase 3a review fixes ────────────────────────────────────────────────

    [Test]
    public void Constructor_NullTransactionOrRegistry_Throws()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new PageModel(_store, TestSupport.CreateMapper(),
                Substitute.For<IPageControllerRegistry>(), Substitute.For<IViewDiscoveryService>(),
                _routeRegistration, _routeService, _zones, _history, _routeStore, null!, _routeRegistry,
                Substitute.For<IChangeSetScope>(), TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes()),
                Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_store, TestSupport.CreateMapper(),
                Substitute.For<IPageControllerRegistry>(), Substitute.For<IViewDiscoveryService>(),
                _routeRegistration, _routeService, _zones, _history, _routeStore, _transaction, null!,
                Substitute.For<IChangeSetScope>(), TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes()),
                Throws.ArgumentNullException);
        });
    }

    [Test]
    public async Task Publish_PagePublishFails_RollsBackWithItsError()
    {
        var pageId = Guid.NewGuid();
        _store.GetCurrentDraftAsync(pageId, Arg.Any<CancellationToken>()).Returns(Page(pageId, routeIds: [Guid.NewGuid()]));
        _store.PublishAsync(pageId, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(false, "stale"));
        var outcomes = CaptureTransactionOutcomes();

        var result = await _model.PublishPageAsync(pageId);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo((false, (string?)"stale", 0)));
            Assert.That(outcomes, Is.EqualTo(new[] { false }));
        });
        await _routeStore.DidNotReceiveWithAnyArgs().PublishAsync(default, default);
    }

    [Test]
    public async Task Publish_MissingRouteItem_FailsNamingTheRoute_AndKeepsThePreviousRows()
    {
        var pageId = Guid.NewGuid();
        var goneId = Guid.NewGuid();
        var unknownId = Guid.NewGuid();
        _store.GetCurrentDraftAsync(pageId, Arg.Any<CancellationToken>()).Returns(Page(pageId, routeIds: [goneId]));
        _routeService.GetByOwningContentAsync(pageId, Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { new() { Id = Guid.NewGuid(), Pattern = "/docs", RouteNodeId = goneId } });
        var outcomes = CaptureTransactionOutcomes();

        var known = await _model.PublishPageAsync(pageId);
        _store.GetCurrentDraftAsync(pageId, Arg.Any<CancellationToken>()).Returns(Page(pageId, routeIds: [unknownId]));
        var unknown = await _model.PublishPageAsync(pageId);

        Assert.Multiple(() =>
        {
            Assert.That(known.Success, Is.False);
            Assert.That(known.ErrorMessage, Is.EqualTo("Route '/docs' no longer exists; edit the page's routes and publish again."));
            Assert.That(unknown.ErrorMessage, Does.StartWith($"Route '{unknownId}' no longer exists"));
            Assert.That(outcomes, Is.EqualTo(new[] { false, false }));
        });
        await _routeService.DidNotReceiveWithAnyArgs().DeleteAsync(default, default);
        await _routeStore.DidNotReceiveWithAnyArgs().DeleteAsync(default, default, default);
    }

    [Test]
    public async Task Publish_FirstPublishWithTwoRoutes_SecondCollides_RollsBackBoth()
    {
        var pageId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        _store.GetCurrentDraftAsync(pageId, Arg.Any<CancellationToken>()).Returns(Page(pageId, routeIds: [firstId, secondId]));
        _routeStore.GetCurrentDraftAsync(firstId, Arg.Any<CancellationToken>()).Returns(Route(firstId, RouteMode.Explicit, "/free"));
        _routeStore.GetCurrentDraftAsync(secondId, Arg.Any<CancellationToken>()).Returns(Route(secondId, RouteMode.Explicit, "/taken"));
        _routeService.UpsertAsync(Arg.Is<CMSRouteDTO>(r => r.Pattern == "/taken"), Arg.Any<CancellationToken>())
            .Returns((false, "This route pattern is already in use by another content item.", (CMSRouteDTO?)null));
        var outcomes = CaptureTransactionOutcomes();

        var result = await _model.PublishPageAsync(pageId);

        // The first row and both route publishes were written inside the transaction the work rolled back.
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("already in use"));
            Assert.That(outcomes, Is.EqualTo(new[] { false }));
        });
        await _routeService.Received(1).UpsertAsync(Arg.Is<CMSRouteDTO>(r => r.Pattern == "/free"), Arg.Any<CancellationToken>());
        await _zones.DidNotReceiveWithAnyArgs().PublishPageZonesAsync(default, default);
        await _history.DidNotReceiveWithAnyArgs().CaptureAsync(default, default);
    }

    /// <summary>A parent renamed from /old to /docs, with <paramref name="children"/> under it.</summary>
    private Guid RenamedParent(params PageDTO[] children)
    {
        var pageId = children.Length > 0 ? children[0].Version.TargetOf(PageModel.ParentRelation)!.Value : Guid.NewGuid();
        var routeId = Guid.NewGuid();
        var page = Page(pageId, routeIds: [routeId]);
        _store.GetCurrentDraftAsync(pageId, Arg.Any<CancellationToken>()).Returns(page);
        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns([page, .. children]);
        _routeStore.GetCurrentDraftAsync(routeId, Arg.Any<CancellationToken>()).Returns(Route(routeId, RouteMode.Derived));
        var renamed = new CMSRouteDTO { Pattern = "/docs", RouteNodeId = routeId, OwningContentNodeId = pageId };
        _routeService.GetByOwningContentAsync(pageId, Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { new() { Pattern = "/old", RouteNodeId = routeId, OwningContentNodeId = pageId } }, new List<CMSRouteDTO> { renamed });
        return pageId;
    }

    /// <summary>A child page whose published version has <paramref name="publishedSlug"/>, its draft <paramref name="draftSlug"/>.</summary>
    private PageDTO LiveChild(Guid parentId, string draftSlug, string? publishedSlug, out Guid routeId, RouteMode mode = RouteMode.Derived)
    {
        var childId = Guid.NewGuid();
        routeId = Guid.NewGuid();
        var draft = Page(childId, parentId, draftSlug, routeIds: [routeId]);
        var versions = new List<PageDTO> { draft };
        if (publishedSlug != null)
        {
            var live = Page(childId, parentId, publishedSlug, routeIds: [routeId]);
            live.Version.State = ContentVersionState.Published;
            live.Version.IsCurrentDraft = false;
            versions.Add(live);
        }
        _store.GetAllVersionsAsync(childId, Arg.Any<CancellationToken>()).Returns(versions);
        _routeStore.GetAllVersionsAsync(routeId, Arg.Any<CancellationToken>())
            .Returns(new List<RouteDTO> { Route(routeId, mode, "/explicit") });
        var id = routeId;
        _routeService.GetByOwningContentAsync(childId, Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { new() { Pattern = "/old/" + (publishedSlug ?? draftSlug), RouteNodeId = id } });
        return draft;
    }

    [Test]
    public async Task Publish_Rename_ReprojectsOnlyLiveChildren_FromTheirPublishedSlug()
    {
        var parentId = Guid.NewGuid();
        var neverPublished = LiveChild(parentId, "draft-only", null, out var neverRoute);
        var live = LiveChild(parentId, "renamed-in-draft", "intro", out var liveRoute);
        RenamedParent(neverPublished, live);

        var result = await _model.PublishPageAsync(parentId);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.DescendantsUpdated, Is.EqualTo(1));
        });
        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.Pattern == "/docs/intro" && r.RouteNodeId == liveRoute), Arg.Any<CancellationToken>());
        await _routeService.DidNotReceive().UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.RouteNodeId == neverRoute), Arg.Any<CancellationToken>());
        await _routeService.DidNotReceive().UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.Pattern.Contains("renamed-in-draft")), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Publish_Rename_SkipsExplicitAndMissingRouteVersions_AndChildrenWithoutRoutes()
    {
        var parentId = Guid.NewGuid();
        var explicitChild = LiveChild(parentId, "a", "a", out var explicitRoute, RouteMode.Explicit);
        var noVersionChild = LiveChild(parentId, "b", "b", out var noVersionRoute);
        _routeStore.GetAllVersionsAsync(noVersionRoute, Arg.Any<CancellationToken>()).Returns((List<RouteDTO>)null!);
        var routeless = LiveChild(parentId, "c", "c", out _);
        _store.GetAllVersionsAsync(routeless.Version.Node.Id, Arg.Any<CancellationToken>())
            .Returns(new List<PageDTO> { Published(Page(routeless.Version.Node.Id, parentId, "c")) });
        RenamedParent(explicitChild, noVersionChild, routeless);

        var result = await _model.PublishPageAsync(parentId);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.DescendantsUpdated, Is.EqualTo(0));
        });
        await _routeService.DidNotReceive().UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.RouteNodeId == explicitRoute || r.RouteNodeId == noVersionRoute), Arg.Any<CancellationToken>());
    }

    private static PageDTO Published(PageDTO page)
    {
        page.Version.State = ContentVersionState.Published;
        return page;
    }

    [Test]
    public async Task Publish_Rename_ChildCollides_RollsBackTheWholePublish()
    {
        var parentId = Guid.NewGuid();
        var first = LiveChild(parentId, "x", "x", out _);
        var second = LiveChild(parentId, "y", "y", out _);
        RenamedParent(first, second);
        _routeService.UpsertAsync(Arg.Is<CMSRouteDTO>(r => r.Pattern == "/docs/x"), Arg.Any<CancellationToken>())
            .Returns((false, "This route pattern is already in use by another content item.", (CMSRouteDTO?)null));
        var outcomes = CaptureTransactionOutcomes();

        var result = await _model.PublishPageAsync(parentId);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo(
                "The publish was rolled back: the child URL '/docs/x' could not be moved. This route pattern is already in use by another content item."));
            Assert.That(outcomes, Is.EqualTo(new[] { false }));
        });
        await _zones.DidNotReceiveWithAnyArgs().PublishPageZonesAsync(default, default);
    }

    [Test]
    public async Task Publish_ChildOfAParentWithDerivedAndExplicitRoutes_UsesTheDerivedPrefix()
    {
        var parentId = Guid.NewGuid();
        var pageId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        _store.GetCurrentDraftAsync(pageId, Arg.Any<CancellationToken>()).Returns(Page(pageId, parentId, "team", routeIds: [routeId]));
        _routeStore.GetCurrentDraftAsync(routeId, Arg.Any<CancellationToken>()).Returns(Route(routeId, RouteMode.Derived));
        // The explicit alias comes first and has the lower order: only the Derived marker picks /about.
        _routeService.GetByOwningContentAsync(parentId, Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            new() { Pattern = "/company", Order = -1, RouteNodeId = Guid.NewGuid(), DataTokensJson = "{\"RouteContentType\":\"Page\",\"RouteMode\":\"Explicit\"}" },
            new() { Pattern = "/about", Order = 5, RouteNodeId = Guid.NewGuid(), DataTokensJson = "{\"RouteContentType\":\"Page\",\"RouteMode\":\"Derived\"}" }
        });

        var result = await _model.PublishPageAsync(pageId);

        Assert.That(result.Success, Is.True);
        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.Pattern == "/about/team" && r.DataTokensJson.Contains("\"RouteMode\":\"Derived\"")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Publish_ParentWithOnlyExplicitRoutes_UsesTheLowestOrderThenPattern()
    {
        var parentId = Guid.NewGuid();
        var pageId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        _store.GetCurrentDraftAsync(pageId, Arg.Any<CancellationToken>()).Returns(Page(pageId, parentId, "team", routeIds: [routeId]));
        _routeStore.GetCurrentDraftAsync(routeId, Arg.Any<CancellationToken>()).Returns(Route(routeId, RouteMode.Derived));
        _routeService.GetByOwningContentAsync(parentId, Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            new() { Pattern = "/z", Order = 1, RouteNodeId = Guid.NewGuid() , DataTokensJson = "{}" },
            new() { Pattern = "/b", Order = 0, RouteNodeId = Guid.NewGuid(), DataTokensJson = "{}" },
            new() { Pattern = "/a", Order = 0, RouteNodeId = Guid.NewGuid(), DataTokensJson = "{}" }
        });

        await _model.PublishPageAsync(pageId);

        await _routeService.Received(1).UpsertAsync(Arg.Is<CMSRouteDTO>(r => r.Pattern == "/a/team"), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SavePage_WithAnotherPagesRoute_IsRefused()
    {
        var pageId = Guid.NewGuid();
        var ownRoute = Guid.NewGuid();
        var foreignRoute = Guid.NewGuid();
        _store.FindCurrentDraftsRelatedAsync(PageModel.RouteRelation, ownRoute, Arg.Any<CancellationToken>())
            .Returns(new List<PageDTO> { Page(pageId) });
        _store.FindCurrentDraftsRelatedAsync(PageModel.RouteRelation, foreignRoute, Arg.Any<CancellationToken>())
            .Returns(new List<PageDTO> { Page(Guid.NewGuid()) });
        _store.SaveDraftAsync(Arg.Any<PageDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true, NodeId: pageId));
        var model = new PageModel(_store, TestSupport.CreateMapper(),
            ControllerRegistry(), Substitute.For<IViewDiscoveryService>(), _routeRegistration,
            _routeService, _zones, _history, _routeStore, _transaction, _routeRegistry, Substitute.For<IChangeSetScope>(),
            TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes());

        var own = await model.SaveUpsertAsync(new PageUpsertViewModel
            { NodeId = pageId, Title = "Docs", ControllerName = "GenericPage", RouteIds = [ownRoute, Guid.NewGuid()] });
        var foreign = await model.SaveUpsertAsync(new PageUpsertViewModel
            { NodeId = pageId, Title = "Docs", ControllerName = "GenericPage", RouteIds = [ownRoute, foreignRoute] });

        Assert.Multiple(() =>
        {
            Assert.That(own.Success, Is.True);
            Assert.That(foreign.Success, Is.False);
            Assert.That(foreign.ErrorField, Is.EqualTo(nameof(PageUpsertViewModel.RouteIds)));
            Assert.That(foreign.ErrorMessage, Is.EqualTo("Routes: a selected route belongs to another page."));
        });
    }

    public sealed class FaqPageConfig
    {
        [ContentRelation("faqs")]
        public Guid? FaqId { get; set; }
    }

    [Test]
    public async Task GetUpsertViewModel_HydratesPageConfigRelationsIntoTheJson()
    {
        var pageId = Guid.NewGuid();
        var faq = Guid.NewGuid();
        var page = Page(pageId);
        page.ConfigurationJson = "{\"heading\":\"Hi\"}";
        page.Version.Relations.Add(new ContentRelation { Name = "config.FaqId", TargetNodeId = faq, Kind = RelationKind.Reference });
        _store.GetCurrentDraftAsync(pageId, Arg.Any<CancellationToken>()).Returns(page);
        var controllers = Substitute.For<IPageControllerRegistry>();
        controllers.GetByName("GenericPage").Returns(new PageControllerInfo { Name = "GenericPage", ConfigurationType = typeof(FaqPageConfig) });
        var model = new PageModel(_store, TestSupport.CreateMapper(), controllers, Substitute.For<IViewDiscoveryService>(),
            _routeRegistration, _routeService, _zones, _history, _routeStore, _transaction, _routeRegistry,
            Substitute.For<IChangeSetScope>(), TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes());

        var vm = (PageUpsertViewModel)(await model.GetUpsertViewModelAsync(pageId, new QueryCollection()))!;

        Assert.That(vm.ConfigurationJson, Does.Contain(faq.ToString()).And.Contain("\"heading\":\"Hi\""));
    }

    private static IPageControllerRegistry ControllerRegistry()
    {
        var registry = Substitute.For<IPageControllerRegistry>();
        registry.GetByName(Arg.Any<string>()).Returns(new PageControllerInfo
        {
            Name = "GenericPage",
            ControllerType = typeof(object)
        });
        registry.ValidateConfiguration(Arg.Any<string>(), Arg.Any<string>()).Returns(new List<string>());
        return registry;
    }
}
