using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Interfaces;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Page;
using WebWayCMS.Models.PageHistory;
using WebWayCMS.Pages;
using WebWayCMS.Services;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class PageModelTests
{
    private IContentStore<PageDTO> _store = null!;
    private IMapper _mapper = null!;
    private IPageControllerRegistry _registry = null!;
    private IViewDiscoveryService _viewDiscovery = null!;
    private IRouteRegistrationService _routeRegistration = null!;
    private ICMSRouteService _cmsRouteService = null!;
    private IContentZoneService _contentZoneService = null!;
    private IChangeSetScope _changeSetScope = null!;
    private IPageHistoryService _pageHistory = null!;
    private IContentStore<RouteDTO> _routeStore = null!;
    private ICmsTransaction _transaction = null!;
    private ICMSRouteRegistry _routeRegistry = null!;
    private PageModel _model = null!;

    [SetUp]
    public void SetUp()
    {
        _store = Substitute.For<IContentStore<PageDTO>>();
        _mapper = TestSupport.CreateMapper();
        _registry = Substitute.For<IPageControllerRegistry>();
        _registry.GetByName(Arg.Any<string>()).Returns(new PageControllerInfo());
        _viewDiscovery = Substitute.For<IViewDiscoveryService>();
        _routeRegistration = Substitute.For<IRouteRegistrationService>();
        _cmsRouteService = Substitute.For<ICMSRouteService>();
        _contentZoneService = Substitute.For<IContentZoneService>();
        _contentZoneService.PublishPageZonesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new PageZonePublishResult([], []));
        _changeSetScope = Substitute.For<IChangeSetScope>();
        _pageHistory = Substitute.For<IPageHistoryService>();
        _routeStore = Substitute.For<IContentStore<RouteDTO>>();
        _transaction = TestSupport.PassThroughTransaction();
        _routeRegistry = Substitute.For<ICMSRouteRegistry>();
        _routeStore.SaveDraftAsync(Arg.Any<RouteDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true, VersionId: Guid.NewGuid(), NodeId: Guid.NewGuid()));
        _pageHistory.GetSnapshotsReferencingVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new List<PageSetSnapshot>());
        _model = new PageModel(_store, _mapper, _registry, _viewDiscovery, _routeRegistration, _cmsRouteService, _contentZoneService, _pageHistory, _routeStore, _transaction, _routeRegistry, _changeSetScope, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes());
    }

    private static PageDTO Page(string title = "T", ContentVersionState state = ContentVersionState.Published,
        bool hidden = false, Guid? nodeId = null)
    {
        var nid = nodeId ?? Guid.NewGuid();
        return new PageDTO
        {
            VersionId = Guid.NewGuid(),
            ControllerName = "GenericPage",
            ConfigurationJson = "{}",
            Version = new ContentVersion
            {
                Node = new ContentNode { Id = nid, CreatedUtc = DateTime.UtcNow, IsHidden = hidden },
                Title = title,
                Slug = title.ToLowerInvariant(),
                VersionNumber = 0,
                State = state
            }
        };
    }

    private static CMSRouteDTO RouteFor(Guid contentNodeId, string pattern)
    {
        return new CMSRouteDTO
        {
            Id = Guid.NewGuid(),
            Pattern = pattern,
            OwningContentNodeId = contentNodeId,
            OwningContentType = "Page"
        };
    }

    private static IQueryCollection Query(params (string, string)[] pairs) =>
        new QueryCollection(pairs.ToDictionary(p => p.Item1, p => new Microsoft.Extensions.Primitives.StringValues(p.Item2)));

    [Test]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new PageModel(null!, _mapper, _registry, _viewDiscovery, _routeRegistration, _cmsRouteService, _contentZoneService, _pageHistory, _routeStore, _transaction, _routeRegistry, _changeSetScope, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes()), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_store, null!, _registry, _viewDiscovery, _routeRegistration, _cmsRouteService, _contentZoneService, _pageHistory, _routeStore, _transaction, _routeRegistry, _changeSetScope, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes()), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_store, _mapper, null!, _viewDiscovery, _routeRegistration, _cmsRouteService, _contentZoneService, _pageHistory, _routeStore, _transaction, _routeRegistry, _changeSetScope, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes()), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_store, _mapper, _registry, null!, _routeRegistration, _cmsRouteService, _contentZoneService, _pageHistory, _routeStore, _transaction, _routeRegistry, _changeSetScope, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes()), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_store, _mapper, _registry, _viewDiscovery, null!, _cmsRouteService, _contentZoneService, _pageHistory, _routeStore, _transaction, _routeRegistry, _changeSetScope, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes()), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_store, _mapper, _registry, _viewDiscovery, _routeRegistration, null!, _contentZoneService, _pageHistory, _routeStore, _transaction, _routeRegistry, _changeSetScope, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes()), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_store, _mapper, _registry, _viewDiscovery, _routeRegistration, _cmsRouteService, null!, _pageHistory, _routeStore, _transaction, _routeRegistry, _changeSetScope, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes()), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_store, _mapper, _registry, _viewDiscovery, _routeRegistration, _cmsRouteService, _contentZoneService, _pageHistory, null!, _transaction, _routeRegistry, _changeSetScope, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes()), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_store, _mapper, _registry, _viewDiscovery, _routeRegistration, _cmsRouteService, _contentZoneService, _pageHistory, _routeStore, _transaction, _routeRegistry, null!, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes()), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void Metadata()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_model.ContentType, Is.EqualTo("pages"));
            Assert.That(_model.DisplayName, Is.EqualTo("Page"));
            Assert.That(_model.IndexViewPath, Does.Contain("Pages.cshtml"));
            Assert.That(_model.UpsertViewPath, Does.Contain("PageUpsert.cshtml"));
            Assert.That(_model.RegistryHandler, Is.Not.Null);
            Assert.That(_model.SupportsPreview, Is.True);
        });
    }

    [Test]
    public async Task GetPageIndexAsync_BuildsTreeWithRootIntermediateAndLeaf()
    {
        var page1 = Page(title: "Home");
        var page2 = Page(title: "Leaf");
        var page3 = Page(title: "Branch");

        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { page1, page2, page3 });
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            RouteFor(page1.Version.Node.Id, "/"),
            RouteFor(page2.Version.Node.Id, "/a/b"),
            RouteFor(page3.Version.Node.Id, "/a"),
        });

        var vm = await _model.GetPageIndexAsync();

        Assert.Multiple(() =>
        {
            Assert.That(vm.Pages.Any(p => p.Path == "/"), Is.True);
            var branch = vm.Pages.First(p => p.Path == "/a");
            Assert.That(branch.Children.Any(c => c.Path == "/a/b"), Is.True);
        });
    }

    [Test]
    public async Task GetPageIndexAsync_PageNodes_CarryVersionModificationDate()
    {
        var root = Page(title: "Home");
        var leaf = Page(title: "Leaf");
        root.Version.CreatedUtc = new DateTime(2026, 1, 2, 3, 4, 0, DateTimeKind.Utc);
        leaf.Version.CreatedUtc = new DateTime(2026, 5, 6, 7, 8, 0, DateTimeKind.Utc);

        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { root, leaf });
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            RouteFor(root.Version.Node.Id, "/"),
            RouteFor(leaf.Version.Node.Id, "/a/b"),
        });

        var vm = await _model.GetPageIndexAsync();
        var a = vm.Pages.Single(p => p.Path == "/a");

        Assert.Multiple(() =>
        {
            Assert.That(vm.Pages.Single(p => p.Path == "/").ModificationDate, Is.EqualTo(root.Version.CreatedUtc));
            Assert.That(a.ModificationDate, Is.Null, "placeholder node has no date");
            Assert.That(a.Children.Single().ModificationDate, Is.EqualTo(leaf.Version.CreatedUtc));
        });
    }

    [Test]
    public async Task GetPageIndexAsync_DuplicateRootUpdatesExistingNode()
    {
        var first = Page(title: "First");
        var second = Page(title: "Second");

        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { first, second });
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            RouteFor(first.Version.Node.Id, "/"),
            RouteFor(second.Version.Node.Id, "/"),
        });

        var vm = await _model.GetPageIndexAsync();

        Assert.That(vm.Pages.Single(p => p.Path == "/").Title, Is.EqualTo("Second"));
    }

    [Test]
    public async Task GetPageIndexAsync_IntermediateThenLeafForSameSegmentUpdatesNode()
    {
        var page1 = Page(title: "Deep");
        var page2 = Page(title: "RealA");

        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { page1, page2 });
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            RouteFor(page1.Version.Node.Id, "/a/b"),
            RouteFor(page2.Version.Node.Id, "/a"),
        });

        var vm = await _model.GetPageIndexAsync();

        Assert.That(vm.Pages.Single(p => p.Path == "/a").Title, Is.EqualTo("RealA"));
    }

    [Test]
    public async Task GetPageIndexAsync_DeepPath_CreatesIntermediateNonLeafNodes()
    {
        var page = Page(title: "Deep");

        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { page });
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            RouteFor(page.Version.Node.Id, "/x/y/z"),
        });

        var vm = await _model.GetPageIndexAsync();
        var x = vm.Pages.Single(p => p.Path == "/x");
        var y = x.Children.Single(c => c.Path == "/x/y");

        Assert.Multiple(() =>
        {
            Assert.That(x.PageNodeId, Is.Null, "intermediate node has no page id");
            Assert.That(y.Children.Single().Path, Is.EqualTo("/x/y/z"));
            Assert.That(y.Children.Single().Title, Is.EqualTo("Deep"));
        });
    }

    [Test]
    public void SavePageUpsertAsync_NullModel_Throws()
    {
        Assert.That(async () => await _model.SavePageUpsertAsync(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task SavePageUpsertAsync_CreateAndUpdate()
    {
        _store.SaveDraftAsync(Arg.Any<PageDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true), new ContentWriteResult(true), new ContentWriteResult(false, "err"));

        Assert.Multiple(async () =>
        {
            Assert.That((await _model.SavePageUpsertAsync(new PageUpsertViewModel { NodeId = null, Title = "T", Slug = "r", ControllerName = "C" })).Success, Is.True);
            Assert.That((await _model.SavePageUpsertAsync(new PageUpsertViewModel { NodeId = Guid.NewGuid(), Title = "T", Slug = "r", ControllerName = "C" })).Success, Is.True);
            Assert.That((await _model.SavePageUpsertAsync(new PageUpsertViewModel { NodeId = Guid.NewGuid(), Title = "T", Slug = "r", ControllerName = "C" })).Success, Is.False);
        });
    }

    [Test]
    public async Task DeleteSelfAsync_DeletesThePageItsZonesAndRoutes()
    {
        var page = Page();
        _store.GetCurrentDraftAsync(page.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(page);
        _store.DeleteAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>()).Returns(true);

        Assert.That(await ((IContentDeleter)_model).DeleteSelfAsync(page.Version.Node.Id, default), Is.True);
        await _routeRegistration.Received(1).UnregisterContentRoutesAsync(page.Version.Node.Id, Arg.Any<CancellationToken>());
        await _contentZoneService.Received(1).DeletePageZonesAsync(page.Version.Node.Id, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteSelfAsync_NotFound_StillDeletesWithoutUnregistering()
    {
        _store.GetCurrentDraftAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PageDTO?)null);
        _store.DeleteAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>()).Returns(true);

        Assert.That(await ((IContentDeleter)_model).DeleteSelfAsync(Guid.NewGuid(), default), Is.True);
        await _routeRegistration.DidNotReceive().UnregisterContentRoutesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _contentZoneService.DidNotReceive().DeletePageZonesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task VersionHistory_RestoreAndDeleteVersion()
    {
        var nodeId = Guid.NewGuid();
        _store.GetAllVersionsAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { Page(nodeId: nodeId) });
        Assert.That(await _model.GetVersionHistoryAsync(nodeId), Is.Not.Null);

        var historical = Page();
        _store.GetVersionAsync(historical.VersionId, Arg.Any<CancellationToken>()).Returns(historical);
        var current = Page(nodeId: historical.Version.Node.Id);
        current.Version.VersionNumber = 7;
        _store.GetCurrentDraftAsync(historical.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(current);
        var restoreVm = (PageUpsertViewModel)(await _model.GetRestoreVersionViewModelAsync(historical.VersionId))!;
        Assert.That(restoreVm.NodeId, Is.EqualTo(historical.Version.Node.Id));
        Assert.That(restoreVm.ExpectedVersionNumber, Is.EqualTo(7));

        _store.DeleteVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        Assert.That(await _model.DeletePageVersionAsync(Guid.NewGuid()), Is.True);
    }

    [Test]
    public async Task GetRestoreVersionViewModel_NullWhenHistoricalMissing()
    {
        _store.GetVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PageDTO?)null);
        Assert.That(await _model.GetRestoreVersionViewModelAsync(Guid.NewGuid()), Is.Null);
    }

    [Test]
    public async Task GetRestoreVersionViewModel_PopulatesParentage()
    {
        var historical = Page();
        var parentNodeId = Guid.NewGuid();
        historical.Version.Relations.Add(new ContentRelation { Name = PageModel.ParentRelation, TargetNodeId = parentNodeId, Kind = RelationKind.Containment });

        _store.GetVersionAsync(historical.VersionId, Arg.Any<CancellationToken>()).Returns(historical);
        var current = Page(nodeId: historical.Version.Node.Id);
        current.Version.VersionNumber = 7;
        _store.GetCurrentDraftAsync(historical.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(current);
        _cmsRouteService.GetByOwningContentAsync(parentNodeId, Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { new() { Pattern = "/docs" } });

        var restoreVm = (PageUpsertViewModel)(await _model.GetRestoreVersionViewModelAsync(historical.VersionId))!;

        Assert.Multiple(() =>
        {
            Assert.That(restoreVm.ParentNodeId, Is.EqualTo(parentNodeId));
            Assert.That(restoreVm.ParentRoutePrefix, Is.EqualTo("/docs"));
        });
    }

    [Test]
    public async Task AdminHandler_UpsertViewModel_EditFoundMissingAndCreateWithParentRoute()
    {
        var page = Page();
        _store.GetCurrentDraftAsync(page.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(page);
        _store.GetCurrentDraftAsync(Arg.Is<Guid>(g => g != page.Version.Node.Id), Arg.Any<CancellationToken>()).Returns((PageDTO?)null);

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetUpsertViewModelAsync(page.Version.Node.Id, Query()), Is.Not.Null);
            Assert.That(await _model.GetUpsertViewModelAsync(Guid.NewGuid(), Query()), Is.Null);
            var withParent = (PageUpsertViewModel)(await _model.GetUpsertViewModelAsync(null, Query(("parentRoute", "blog/"))))!;
            Assert.That(withParent.ParentRoutePrefix, Is.EqualTo("/blog"));
            var rootParent = (PageUpsertViewModel)(await _model.GetUpsertViewModelAsync(null, Query(("parentRoute", "/"))))!;
            Assert.That(rootParent.ParentRoutePrefix, Is.EqualTo("/"));
            var plain = (PageUpsertViewModel)(await _model.GetUpsertViewModelAsync(null, Query()))!;
            Assert.That(plain.ParentRoutePrefix, Is.Null);
        });
    }

    [Test]
    public async Task AdminHandler_UpsertViewModel_Edit_PopulatesParentage()
    {
        var nodeId = Guid.NewGuid();
        var parentNodeId = Guid.NewGuid();
        var page = Page(nodeId: nodeId);
        page.Version.Relations.Add(new ContentRelation { Name = PageModel.ParentRelation, TargetNodeId = parentNodeId, Kind = RelationKind.Containment });

        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _cmsRouteService.GetByOwningContentAsync(parentNodeId, Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { new() { Pattern = "/docs" } });

        var vm = (PageUpsertViewModel)(await _model.GetUpsertViewModelAsync(nodeId, Query()))!;

        Assert.Multiple(() =>
        {
            Assert.That(vm.ParentNodeId, Is.EqualTo(parentNodeId));
            Assert.That(vm.ParentRoutePrefix, Is.EqualTo("/docs"));
        });
    }

    [Test]
    public async Task AdminHandler_SaveUpsert_RouteConflictAndSuccess()
    {
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(false, true);
        _store.SaveDraftAsync(Arg.Any<PageDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));

        var conflict = await _model.SaveUpsertAsync(new PageUpsertViewModel { Title = "T", Slug = "x", ControllerName = "C" });
        var ok = await _model.SaveUpsertAsync(new PageUpsertViewModel { NodeId = null, Title = "T", Slug = "y", ControllerName = "C" });

        Assert.Multiple(() =>
        {
            Assert.That(conflict.Success, Is.False);
            Assert.That(conflict.ErrorField, Is.EqualTo("Slug"));
            Assert.That(ok.Success, Is.True);
        });
    }

    [Test]
    public async Task AdminHandler_SaveUpsert_BlankSlug_DerivesRouteFromTitle()
    {
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _store.SaveDraftAsync(Arg.Any<PageDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));

        var result = await _model.SaveUpsertAsync(new PageUpsertViewModel { Title = "About", Slug = null, ControllerName = "C" });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
        });
        await _cmsRouteService.Received(1).IsPatternAvailableAsync("/about", null, null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AdminHandler_SaveUpsert_SaveFailureSurfaces()
    {
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _store.SaveDraftAsync(Arg.Any<PageDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(false, "err"));

        var result = await _model.SaveUpsertAsync(new PageUpsertViewModel { NodeId = Guid.NewGuid(), Title = "T", Slug = "x", ControllerName = "C" });

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task AdminHandler_SaveUpsert_ConfigValidationFailure_ReturnsError()
    {
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _registry.ValidateConfiguration("C", Arg.Any<object>()).Returns(new List<string> { "Custom CSS is required." });

        var result = await _model.SaveUpsertAsync(new PageUpsertViewModel
        {
            NodeId = null,
            Title = "T",
            Slug = "x",
            ControllerName = "C",
            ConfigurationJson = "{}"
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorField, Is.EqualTo("ConfigurationJson"));
            Assert.That(result.ErrorMessage, Does.Contain("Custom CSS is required."));
        });
    }

    [Test]
    public async Task AdminHandler_SaveUpsert_UnknownController_ReturnsControllerNameError()
    {
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _registry.GetByName("Missing").Returns((PageControllerInfo?)null);

        var result = await _model.SaveUpsertAsync(new PageUpsertViewModel
        {
            Title = "T",
            Slug = "x",
            ControllerName = "Missing"
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorField, Is.EqualTo("ControllerName"));
            Assert.That(result.ErrorMessage, Does.Contain("Unknown controller: Missing"));
        });
        _registry.DidNotReceive().ValidateConfiguration(Arg.Any<string>(), Arg.Any<object>());
    }

    [Test]
    public async Task AdminHandler_SaveUpsert_DuplicateSiblingSlug_Rejected()
    {
        var parentNodeId = Guid.NewGuid();
        var sibling = Page(title: "Child");
        sibling.Version.Relations.Add(new ContentRelation { Name = PageModel.ParentRelation, TargetNodeId = parentNodeId, Kind = RelationKind.Containment });

        _store.FindCurrentDraftsRelatedAsync(PageModel.ParentRelation, parentNodeId, Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { sibling });
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await _model.SaveUpsertAsync(new PageUpsertViewModel
        {
            Title = "Child",
            Slug = "child",
            ParentNodeId = parentNodeId,
            ControllerName = "C"
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorField, Is.EqualTo("Slug"));
        });
    }

    [Test]
    public async Task AdminHandler_SaveUpsert_SameTitleWithoutExplicitSlug_SecondIsRejected()
    {
        var sibling = Page(title: "Dup Title Page");
        sibling.Version.Slug = "dup-title-page";

        _store.FindCurrentDraftsWithoutRelationAsync(PageModel.ParentRelation, Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { sibling });

        var result = await _model.SaveUpsertAsync(new PageUpsertViewModel
        {
            Title = "Dup Title Page",
            Slug = null,
            ControllerName = "C"
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorField, Is.EqualTo("Slug"));
        });
    }

    [Test]
    public async Task AdminHandler_IndexCreateEmptyDeleteApiRestoreAndDeleteVersion()
    {
        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO>());
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());
        _store.DeleteAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>()).Returns(true);
        _store.DeleteVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetIndexViewModelAsync(), Is.InstanceOf<PageIndexViewModel>());
            Assert.That(_model.CreateEmptyUpsertViewModel(), Is.InstanceOf<PageUpsertViewModel>());
            Assert.That(await _model.DeleteAsync(Guid.NewGuid()), Is.True);
            Assert.That(await _model.GetApiListAsync(), Is.Empty);
            Assert.That(await _model.GetRestoreVersionViewModelAsync(Guid.NewGuid()), Is.Null);
            Assert.That(await _model.DeleteVersionAsync(Guid.NewGuid()), Is.True);
        });
    }

    // --- Publish / Unpublish ---

    [Test]
    public async Task PublishPageAsync_FailureSurfaces()
    {
        var nodeId = Guid.NewGuid();
        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(Page(nodeId: nodeId));
        _cmsRouteService.GetByOwningContentAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _store.PublishAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(false, "err"));

        var result = await _model.PublishPageAsync(nodeId);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("err"));
        });
    }

    [Test]
    public async Task PublishPageAsync_MissingDraft_ReturnsNotFoundWithoutPublishing()
    {
        _store.GetCurrentDraftAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PageDTO?)null);

        var result = await _model.PublishPageAsync(Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("Page not found."));
        });
        await _store.DidNotReceive().PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishPageAsync_Success_RegistersRouteFromSlug()
    {
        var nodeId = Guid.NewGuid();
        var page = Page(title: "Test", nodeId: nodeId);
        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _store.PublishAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));
        _cmsRouteService.GetByOwningContentAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _routeRegistration.RegisterContentRoutesAsync(Arg.Any<IRoutableContent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns((true, null));

        var result = await _model.PublishPageAsync(nodeId);

        Assert.That(result.Success, Is.True);
        await _routeRegistration.Received(1).UnregisterContentRoutesAsync(nodeId, Arg.Any<CancellationToken>());
        await _routeRegistration.Received(1).RegisterContentRoutesAsync(
            _model, "/test", "GenericPage", nodeId, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishPageAsync_NoPriorRoute_SeedsNavigationNameFromTitle()
    {
        var nodeId = Guid.NewGuid();
        var page = Page(title: "Test", nodeId: nodeId);
        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _store.PublishAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));
        _cmsRouteService.GetByOwningContentAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _routeRegistration.RegisterContentRoutesAsync(Arg.Any<IRoutableContent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns((true, null));

        await _model.PublishPageAsync(nodeId);

        await _routeRegistration.Received(1).RegisterContentRoutesAsync(
            _model, "/test", "GenericPage", nodeId, "Test", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishPageAsync_PriorRouteHasNavigationName_CarriesItForward()
    {
        var nodeId = Guid.NewGuid();
        var page = Page(title: "Test", nodeId: nodeId);
        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _store.PublishAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));
        _cmsRouteService.GetByOwningContentAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            new() { Pattern = "/test", NavigationName = "Admin Override" }
        });
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _routeRegistration.RegisterContentRoutesAsync(Arg.Any<IRoutableContent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns((true, null));

        await _model.PublishPageAsync(nodeId);

        await _routeRegistration.Received(1).RegisterContentRoutesAsync(
            _model, "/test", "GenericPage", nodeId, "Admin Override", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishPageAsync_PriorRouteNavigationNameIsBlank_StaysBlank()
    {
        var nodeId = Guid.NewGuid();
        var page = Page(title: "Test", nodeId: nodeId);
        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _store.PublishAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));
        _cmsRouteService.GetByOwningContentAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            new() { Pattern = "/test", NavigationName = "   " }
        });
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _routeRegistration.RegisterContentRoutesAsync(Arg.Any<IRoutableContent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns((true, null));

        await _model.PublishPageAsync(nodeId);

        // A blank name on an existing row is how an admin takes the page out of the navigation
        // widgets, so republishing must not resurrect it from the title.
        await _routeRegistration.Received(1).RegisterContentRoutesAsync(
            _model, "/test", "GenericPage", nodeId, null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishPageAsync_HomeSlug_DerivesRoutePatternToRoot()
    {
        var nodeId = Guid.NewGuid();
        var page = Page(title: "Home", nodeId: nodeId);
        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _store.PublishAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));
        _cmsRouteService.GetByOwningContentAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _routeRegistration.RegisterContentRoutesAsync(Arg.Any<IRoutableContent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns((true, null));

        var result = await _model.PublishPageAsync(nodeId);

        Assert.That(result.Success, Is.True);
        await _routeRegistration.Received(1).RegisterContentRoutesAsync(
            _model, "/", "GenericPage", nodeId, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishPageAsync_ExistingRoutePreservesPrefix()
    {
        var nodeId = Guid.NewGuid();
        var page = Page(title: "New", nodeId: nodeId);
        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _store.PublishAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));
        _cmsRouteService.GetByOwningContentAsync(nodeId, Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { new() { Pattern = "/blog/old-slug" } });
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _routeRegistration.RegisterContentRoutesAsync(Arg.Any<IRoutableContent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns((true, null));

        var result = await _model.PublishPageAsync(nodeId);

        Assert.That(result.Success, Is.True);
        await _routeRegistration.Received(1).RegisterContentRoutesAsync(
            _model, "/blog/new", "GenericPage", nodeId, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishPageAsync_ExistingRootRoute_NoPrefix()
    {
        var nodeId = Guid.NewGuid();
        var page = Page(title: "New", nodeId: nodeId);
        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _store.PublishAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));
        _cmsRouteService.GetByOwningContentAsync(nodeId, Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { new() { Pattern = "/old-slug" } });
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _routeRegistration.RegisterContentRoutesAsync(Arg.Any<IRoutableContent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns((true, null));

        var result = await _model.PublishPageAsync(nodeId);

        Assert.That(result.Success, Is.True);
        await _routeRegistration.Received(1).RegisterContentRoutesAsync(
            _model, "/new", "GenericPage", nodeId, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishPageAsync_WithParent_PublishesNested()
    {
        var nodeId = Guid.NewGuid();
        var parentNodeId = Guid.NewGuid();
        var page = Page(title: "Child", nodeId: nodeId);
        page.Version.Relations.Add(new ContentRelation { Name = PageModel.ParentRelation, TargetNodeId = parentNodeId, Kind = RelationKind.Containment });

        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _store.PublishAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));
        _cmsRouteService.GetByOwningContentAsync(parentNodeId, Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { new() { Pattern = "/docs" } });
        _cmsRouteService.GetByOwningContentAsync(nodeId, Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>());
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _routeRegistration.RegisterContentRoutesAsync(Arg.Any<IRoutableContent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns((true, null));

        var result = await _model.PublishPageAsync(nodeId);

        Assert.That(result.Success, Is.True);
        await _routeRegistration.Received(1).RegisterContentRoutesAsync(
            _model, "/docs/child", "GenericPage", nodeId, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishPageAsync_RouteRegistrationFails_FirstPublish_NoRouteToRestore()
    {
        var nodeId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var page = Page(title: "Test", state: ContentVersionState.Draft, nodeId: nodeId);
        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _store.PublishAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true, VersionId: versionId));
        _cmsRouteService.GetByOwningContentAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _routeRegistration.RegisterContentRoutesAsync(Arg.Any<IRoutableContent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns((false, "collision"));

        var result = await _model.PublishPageAsync(nodeId);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("collision"));
        });
        await _store.Received(1).RevertPublishAsync(nodeId, versionId, Guid.Empty, Arg.Any<CancellationToken>());
        await _cmsRouteService.DidNotReceive().UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishPageAsync_RouteRegistrationFails_Republish_RestoresPreviousRoute()
    {
        var nodeId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var displacedId = Guid.NewGuid();
        var page = Page(title: "Test", state: ContentVersionState.Draft, nodeId: nodeId);
        var existingRoute = RouteFor(nodeId, "/old-slug");
        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _store.PublishAsync(nodeId, Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true, VersionId: versionId, DisplacedVersionId: displacedId));
        _cmsRouteService.GetByOwningContentAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO> { existingRoute });
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _cmsRouteService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));
        _routeRegistration.RegisterContentRoutesAsync(Arg.Any<IRoutableContent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns((false, "collision"));

        var result = await _model.PublishPageAsync(nodeId);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("collision"));
        });
        await _store.Received(1).RevertPublishAsync(nodeId, versionId, displacedId, Arg.Any<CancellationToken>());
        await _cmsRouteService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.Pattern == "/old-slug"), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishPageAsync_RouteRegistrationFails_AlreadyPublished_DoesNotRevertState()
    {
        // Only zone drafts were pending: the publish changed no page version, so there is nothing to
        // revert — reverting would take the live version down.
        var nodeId = Guid.NewGuid();
        var page = Page(title: "Test", state: ContentVersionState.Published, nodeId: nodeId);
        var existingRoute = RouteFor(nodeId, "/test");
        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _store.PublishAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true, VersionId: page.VersionId));
        _cmsRouteService.GetByOwningContentAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO> { existingRoute });
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _cmsRouteService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));
        _routeRegistration.RegisterContentRoutesAsync(Arg.Any<IRoutableContent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns((false, "collision"));

        var result = await _model.PublishPageAsync(nodeId);

        Assert.That(result.Success, Is.False);
        await _store.DidNotReceiveWithAnyArgs().RevertPublishAsync(default, default, default, default);
        await _store.DidNotReceiveWithAnyArgs().UnpublishAsync(default, default);
        await _cmsRouteService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.Pattern == "/test"), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishPageAsync_DuplicateSiblingSlug_FailsLoudly()
    {
        var nodeId = Guid.NewGuid();
        var parentNodeId = Guid.NewGuid();
        var page = Page(title: "Child", nodeId: nodeId);
        page.Version.Relations.Add(new ContentRelation { Name = PageModel.ParentRelation, TargetNodeId = parentNodeId, Kind = RelationKind.Containment });

        var sibling = Page(title: "Sibling");
        sibling.Version.Relations.Add(new ContentRelation { Name = PageModel.ParentRelation, TargetNodeId = parentNodeId, Kind = RelationKind.Containment });
        sibling.Version.Slug = "child";

        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _store.FindCurrentDraftsRelatedAsync(PageModel.ParentRelation, parentNodeId, Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { page, sibling });

        var result = await _model.PublishPageAsync(nodeId);

        Assert.That(result.Success, Is.False);
        await _routeRegistration.DidNotReceive().RegisterContentRoutesAsync(
            Arg.Any<IRoutableContent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _store.DidNotReceive().PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnpublishPageAsync_SuccessAndFailure()
    {
        _store.UnpublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true), new ContentWriteResult(false, "err"));

        Assert.Multiple(async () =>
        {
            Assert.That((await _model.UnpublishPageAsync(Guid.NewGuid())).Success, Is.True);
            Assert.That((await _model.UnpublishPageAsync(Guid.NewGuid())).Success, Is.False);
        });

        await _routeRegistration.Received(1).UnregisterContentRoutesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishAsync_Override_SuccessAndFailure()
    {
        _store.PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));
        _store.GetCurrentDraftAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Page(title: "T"));
        _cmsRouteService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _routeRegistration.RegisterContentRoutesAsync(Arg.Any<IRoutableContent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns((true, null));

        Assert.That((await _model.PublishAsync(Guid.NewGuid())).Success, Is.True);

        _store.PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(false, "err"));
        Assert.That((await _model.PublishAsync(Guid.NewGuid())).Success, Is.False);
    }

    [Test]
    public async Task UnpublishAsync_Override_SuccessAndFailure()
    {
        _store.UnpublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true), new ContentWriteResult(false, "err"));

        Assert.Multiple(async () =>
        {
            Assert.That((await _model.UnpublishAsync(Guid.NewGuid())).Success, Is.True);
            Assert.That((await _model.UnpublishAsync(Guid.NewGuid())).Success, Is.False);
        });
    }

    [Test]
    public async Task RestoreVersionAsync_DelegatesToStore()
    {
        _store.RestoreAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));

        Assert.That((await _model.RestoreVersionAsync(Guid.NewGuid())).Success, Is.True);
    }

    // --- PageRegistryHandler ---

    [Test]
    public void RegistryHandler_GetAll_ReturnsJson()
    {
        _registry.GetAllControllers().Returns(new List<PageControllerInfo>
        {
            new() { Name = "Generic", DisplayName = "Generic", Description = "d", Category = "General" }
        });

        var result = _model.RegistryHandler!.GetAll();

        Assert.That(result, Is.InstanceOf<JsonResult>());
    }

    [Test]
    public void RegistryHandler_GetProperties_EmptyName_BadRequest()
    {
        Assert.That(_model.RegistryHandler!.GetProperties(" "), Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void RegistryHandler_GetProperties_NotFound()
    {
        _registry.GetByName("X").Returns((PageControllerInfo?)null);

        Assert.That(_model.RegistryHandler!.GetProperties("X"), Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public void RegistryHandler_GetProperties_ReturnsJsonWithViews()
    {
        _registry.GetByName("Generic").Returns(new PageControllerInfo
        {
            Name = "Generic",
            DisplayName = "Generic",
            Category = "General",
            Properties = new List<WebWayCMS.Forms.FormPropertyInfo>
            {
                new() { Name = "ViewName", Label = "View", Order = 1 }
            }
        });
        _viewDiscovery.GetControllerViews("Generic").Returns(new[] { "Default" });

        var result = _model.RegistryHandler!.GetProperties("Generic");

        Assert.That(result, Is.InstanceOf<JsonResult>());
    }

    [Test]
    public void RoutableContent_RouteContentType_ReturnsPage()
    {
        IRoutableContent routable = _model;
        Assert.That(routable.RouteContentType, Is.EqualTo("Page"));
    }

    [Test]
    public async Task RoutableContent_GetRoutesAsync_ReturnsRoutes()
    {
        var nodeId = Guid.NewGuid();
        _cmsRouteService.GetByOwningContentAsync(nodeId, Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { new() { Pattern = "/test" } });

        IRoutableContent routable = _model;
        var routes = await routable.GetRoutesAsync(nodeId, CancellationToken.None);

        Assert.That(routes, Has.Count.EqualTo(1));
        Assert.That(routes[0].Pattern, Is.EqualTo("/test"));
    }

    [Test]
    public async Task GetPageIndexAsync_BuildTree_PageWithoutRoute_IsIncludedWithDerivedPath()
    {
        var pageWithRoute = Page(title: "HasRoute");
        var pageWithoutRoute = Page(title: "NoRoute");

        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { pageWithRoute, pageWithoutRoute });
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            RouteFor(pageWithRoute.Version.Node.Id, "/hasroute"),
        });

        var vm = await _model.GetPageIndexAsync();

        // A draft page (no route yet) still appears in the tree, using a path derived from its slug,
        // so the admin can find and publish it.
        Assert.That(vm.Pages, Has.Count.EqualTo(2));
        var noRouteNode = vm.Pages.Single(p => p.Title == "NoRoute");
        Assert.That(noRouteNode.Path, Is.EqualTo("/noroute"));
    }

    [Test]
    public async Task GetPageIndexAsync_BuildTree_HomeSlugWithoutRoute_DerivesRootPath()
    {
        var homePage = Page(title: "Home");
        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { homePage });
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());

        var vm = await _model.GetPageIndexAsync();

        Assert.That(vm.Pages.Single().Path, Is.EqualTo("/"));
    }

    [Test]
    public async Task GetPageIndexAsync_BuildTree_DifferentContentTypeRoutesAreIgnored()
    {
        var page = Page(title: "T");
        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { page });
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            new()
            {
                Pattern = "/test",
                OwningContentNodeId = page.Version.Node.Id,
                OwningContentType = "Widget"
            }
        });

        var vm = await _model.GetPageIndexAsync();

        // The non-Page route is ignored; the page falls back to its slug-derived path.
        Assert.That(vm.Pages, Has.Count.EqualTo(1));
        Assert.That(vm.Pages[0].Path, Is.EqualTo("/t"));
    }

    [Test]
    public async Task GetPageIndexAsync_BuildTree_IntermediateNodeWithoutPage_NullPageNodeId()
    {
        var deepPage = Page(title: "Deep");
        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { deepPage });
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            RouteFor(deepPage.Version.Node.Id, "/a/b"),
        });

        var vm = await _model.GetPageIndexAsync();
        var a = vm.Pages.Single(p => p.Path == "/a");

        Assert.That(a.PageNodeId, Is.Null);
    }

    [Test]
    public async Task GetPageIndexAsync_UnpublishedChild_NestsUnderParent()
    {
        var parentNodeId = Guid.NewGuid();
        var parent = Page(title: "Docs", nodeId: parentNodeId);
        var child = Page(title: "Child");
        child.Version.Relations.Add(new ContentRelation { Name = PageModel.ParentRelation, TargetNodeId = parentNodeId, Kind = RelationKind.Containment });

        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { parent, child });
        _store.GetPublishedNodeIdsAsync(Arg.Any<CancellationToken>()).Returns(new HashSet<Guid>());
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            RouteFor(parentNodeId, "/docs"),
        });

        var vm = await _model.GetPageIndexAsync();

        var docs = vm.Pages.Single(p => p.Path == "/docs");
        Assert.That(docs.Children.Single().Path, Is.EqualTo("/docs/child"));
    }

    [Test]
    public async Task GetPageIndexAsync_DescendantCount_CountsChildPagesAtEveryDepth_ByTheParentRelation()
    {
        var docs = Page(title: "Docs");
        var guide = Page(title: "Guide");
        var step = Page(title: "Step");
        var other = Page(title: "Other");
        guide.Version.Relations.Add(new ContentRelation { Name = PageModel.ParentRelation, TargetNodeId = docs.Version.Node.Id, Kind = RelationKind.Containment });
        step.Version.Relations.Add(new ContentRelation { Name = PageModel.ParentRelation, TargetNodeId = guide.Version.Node.Id, Kind = RelationKind.Containment });

        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { docs, guide, step, other });
        _store.GetPublishedNodeIdsAsync(Arg.Any<CancellationToken>()).Returns(new HashSet<Guid>());
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            RouteFor(docs.Version.Node.Id, "/docs"),
            // An explicit URL elsewhere: the count follows the relation, not the path.
            RouteFor(step.Version.Node.Id, "/elsewhere/step"),
        });

        var vm = await _model.GetPageIndexAsync();

        var nodes = Flatten(vm.Pages).Where(n => n.PageNodeId.HasValue).ToDictionary(n => n.PageNodeId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(nodes[docs.Version.Node.Id].DescendantCount, Is.EqualTo(2));
            Assert.That(nodes[guide.Version.Node.Id].DescendantCount, Is.EqualTo(1));
            Assert.That(nodes[step.Version.Node.Id].DescendantCount, Is.EqualTo(0));
            Assert.That(nodes[other.Version.Node.Id].DescendantCount, Is.EqualTo(0));
            Assert.That(Flatten(vm.Pages).Single(n => n.Path == "/elsewhere").DescendantCount, Is.EqualTo(0), "a path-only node has none");
        });
    }

    private static IEnumerable<PageTreeNode> Flatten(IEnumerable<PageTreeNode> nodes)
        => nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));

    [Test]
    public async Task GetPageIndexAsync_UnpublishedChildUnderRoot_ResolvesUnderSlash()
    {
        var rootNodeId = Guid.NewGuid();
        var root = Page(title: "Home", nodeId: rootNodeId);
        var child = Page(title: "Child");
        child.Version.Relations.Add(new ContentRelation { Name = PageModel.ParentRelation, TargetNodeId = rootNodeId, Kind = RelationKind.Containment });

        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { root, child });
        _store.GetPublishedNodeIdsAsync(Arg.Any<CancellationToken>()).Returns(new HashSet<Guid>());
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            RouteFor(rootNodeId, "/"),
        });

        var vm = await _model.GetPageIndexAsync();

        Assert.That(vm.Pages.Any(p => p.Path == "/child"), Is.True);
    }

    [Test]
    public async Task GetPageIndexAsync_PublishedWithNewerDraft_ReportsPublished()
    {
        var nodeId = Guid.NewGuid();
        var page = Page(title: "T", state: ContentVersionState.Draft, nodeId: nodeId);

        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { page });
        _store.GetPublishedNodeIdsAsync(Arg.Any<CancellationToken>()).Returns(new HashSet<Guid> { nodeId });
        // The page history service decides pending by content; here the newer draft differs from the live page.
        _pageHistory.GetPagesWithUnpublishedChangesAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid> { nodeId });
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            RouteFor(nodeId, "/t"),
        });

        var vm = await _model.GetPageIndexAsync();
        var node = vm.Pages.Single(p => p.Path == "/t");

        Assert.Multiple(() =>
        {
            Assert.That(node.IsPublished, Is.True);
            Assert.That(node.HasPendingChanges, Is.True);
        });
    }

    [Test]
    public async Task GetPageIndexAsync_PublishedWithoutPendingChanges_HasNoPendingChanges()
    {
        var nodeId = Guid.NewGuid();
        var page = Page(title: "T", state: ContentVersionState.Published, nodeId: nodeId);

        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { page });
        _store.GetPublishedNodeIdsAsync(Arg.Any<CancellationToken>()).Returns(new HashSet<Guid> { nodeId });
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            RouteFor(nodeId, "/t"),
        });

        var vm = await _model.GetPageIndexAsync();
        var node = vm.Pages.Single(p => p.Path == "/t");

        Assert.Multiple(() =>
        {
            Assert.That(node.IsPublished, Is.True);
            Assert.That(node.HasPendingChanges, Is.False);
        });
    }

    [Test]
    public async Task GetPageIndexAsync_NeverPublished_ReportsNotPublished()
    {
        var nodeId = Guid.NewGuid();
        var page = Page(title: "T", state: ContentVersionState.Draft, nodeId: nodeId);

        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { page });
        _store.GetPublishedNodeIdsAsync(Arg.Any<CancellationToken>()).Returns(new HashSet<Guid>());
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());

        var vm = await _model.GetPageIndexAsync();
        var node = vm.Pages.Single();

        Assert.Multiple(() =>
        {
            Assert.That(node.IsPublished, Is.False);
            Assert.That(node.HasPendingChanges, Is.False);
        });
    }

    // --- PageRegistryHandler GetForm ---

    [Test]
    public void RegistryHandler_GetForm_EmptyName_ReturnsBadRequest()
    {
        Assert.That(_model.RegistryHandler!.GetForm("  ", null), Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void RegistryHandler_GetForm_NotFound_ReturnsNotFound()
    {
        _registry.GetByName("X").Returns((PageControllerInfo?)null);

        Assert.That(_model.RegistryHandler!.GetForm("X", null), Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public void RegistryHandler_GetForm_NoConfigType_ReturnsPartialViewWithNullModel()
    {
        _registry.GetByName("Plain").Returns(new PageControllerInfo
        {
            Name = "Plain",
            ConfigurationType = null
        });

        var result = _model.RegistryHandler!.GetForm("Plain", null);

        Assert.That(result, Is.InstanceOf<PartialViewResult>());
        Assert.That(((PartialViewResult)result).ViewData!.Model, Is.Null);
    }

    [Test]
    public void RegistryHandler_GetForm_WithConfigType_ReturnsPartialViewWithInstance()
    {
        _registry.GetByName("Typed").Returns(new PageControllerInfo
        {
            Name = "Typed",
            ConfigurationType = typeof(SampleSaveConfig)
        });

        var result = _model.RegistryHandler!.GetForm("Typed", null);

        Assert.That(result, Is.InstanceOf<PartialViewResult>());
        Assert.That(((PartialViewResult)result).ViewData!.Model, Is.TypeOf<SampleSaveConfig>());
    }

    // --- Parent-node resolution for the "Add child" admin link (#2) ---

    [Test]
    public async Task GetUpsertViewModelAsync_ParentRoute_PublishedParent_ResolvesParentNodeIdFromRoutes()
    {
        var parentNodeId = Guid.NewGuid();
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { RouteFor(parentNodeId, "/docs") });

        var vm = (PageUpsertViewModel)(await _model.GetUpsertViewModelAsync(null, Query(("parentRoute", "/docs"))))!;

        Assert.That(vm.ParentNodeId, Is.EqualTo(parentNodeId));
    }

    [Test]
    public async Task GetUpsertViewModelAsync_ParentRoute_UnpublishedParent_ResolvesParentNodeIdByWalkingDrafts()
    {
        var parent = Page(title: "Docs");
        var parentNodeId = parent.Version.Node.Id;

        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());
        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { parent });

        var vm = (PageUpsertViewModel)(await _model.GetUpsertViewModelAsync(null, Query(("parentRoute", "/docs"))))!;

        Assert.That(vm.ParentNodeId, Is.EqualTo(parentNodeId));
    }

    [Test]
    public async Task GetUpsertViewModelAsync_ParentRoute_NoMatchAnywhere_LeavesParentNodeIdNull()
    {
        var other = Page(title: "Other");

        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());
        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { other });

        var vm = (PageUpsertViewModel)(await _model.GetUpsertViewModelAsync(null, Query(("parentRoute", "/docs"))))!;

        Assert.That(vm.ParentNodeId, Is.Null);
    }

    private static ContentRelation ParentLink(PageDTO parent)
        => new() { Name = PageModel.ParentRelation, TargetNodeId = parent.Version.Node.Id, Kind = RelationKind.Containment };

    private static string PickerItem(PageDTO page, string path)
        => new { id = page.Version.Node.Id, title = page.Version.Title, path }.ToString()!;

    [Test]
    public async Task GetApiListAsync_ListsPagesAtEveryDepth_ParentFirstWithPaths()
    {
        var docs = Page(title: "Docs");
        var guide = Page(title: "Guide");
        var step = Page(title: "Step");
        guide.Version.Relations.Add(ParentLink(docs));
        step.Version.Relations.Add(ParentLink(guide));

        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { step, guide, docs });
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            RouteFor(docs.Version.Node.Id, "/docs"),
        });

        var items = (await _model.GetApiListAsync()).Select(i => i.ToString()).ToList();

        Assert.That(items, Is.EqualTo(new[]
        {
            PickerItem(docs, "/docs"),
            PickerItem(guide, "/docs/guide"),
            PickerItem(step, "/docs/guide/step"),
        }));
        await _pageHistory.DidNotReceiveWithAnyArgs().GetPagesWithUnpublishedChangesAsync(default!, default);
    }

    [Test]
    public async Task GetApiListAsync_PagesSharingAPath_AreBothListed()
    {
        var first = Page(title: "About");
        var second = Page(title: "About");

        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { first, second });
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());

        var items = (await _model.GetApiListAsync()).Select(i => i.ToString()).ToList();

        Assert.That(items, Is.EquivalentTo(new[] { PickerItem(first, "/about"), PickerItem(second, "/about") }));
    }

    [Test]
    public async Task GetApiListExcludingSubtreeAsync_LeavesOutThePageAndItsDescendants()
    {
        var docs = Page(title: "Docs");
        var guide = Page(title: "Guide");
        var step = Page(title: "Step");
        var blog = Page(title: "Blog");
        guide.Version.Relations.Add(ParentLink(docs));
        step.Version.Relations.Add(ParentLink(guide));

        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { docs, guide, step, blog });
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());

        var items = (await _model.GetApiListExcludingSubtreeAsync(guide.Version.Node.Id)).Select(i => i.ToString()).ToList();

        Assert.That(items, Is.EqualTo(new[] { PickerItem(blog, "/blog"), PickerItem(docs, "/docs") }));
    }

    [Test]
    public async Task GetApiListAsync_ParentCycleAndMissingParent_ListsEveryPageOnce()
    {
        var a = Page(title: "A");
        var b = Page(title: "B");
        var orphan = Page(title: "C");
        a.Version.Relations.Add(ParentLink(b));
        b.Version.Relations.Add(ParentLink(a));
        orphan.Version.Relations.Add(ParentLink(Page(title: "Gone")));

        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { a, b, orphan });
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());

        var items = (await _model.GetApiListAsync()).Select(i => i.ToString()).ToList();
        var excluded = (await _model.GetApiListExcludingSubtreeAsync(a.Version.Node.Id)).Select(i => i.ToString()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(items, Is.EqualTo(new[] { PickerItem(orphan, "/c"), PickerItem(a, "/a"), PickerItem(b, "/b") }));
            Assert.That(excluded, Is.EqualTo(new[] { PickerItem(orphan, "/c") }));
        });
    }

    [Test]
    public async Task GetPageIndexAsync_BuildTree_CircularParent_DoesNotInfiniteLoop()
    {
        var a = Page(title: "A");
        var b = Page(title: "B");
        a.Version.Relations.Add(new ContentRelation { Name = PageModel.ParentRelation, TargetNodeId = b.Version.Node.Id, Kind = RelationKind.Containment });
        b.Version.Relations.Add(new ContentRelation { Name = PageModel.ParentRelation, TargetNodeId = a.Version.Node.Id, Kind = RelationKind.Containment });

        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { a, b });
        _store.GetPublishedNodeIdsAsync(Arg.Any<CancellationToken>()).Returns(new HashSet<Guid>());
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());

        var vm = await _model.GetPageIndexAsync();

        Assert.Multiple(() =>
        {
            Assert.That(vm.Pages, Has.Count.EqualTo(2));
            // A parent cycle counts each page once.
            Assert.That(vm.Pages.Select(p => p.DescendantCount), Is.EqualTo(new[] { 1, 1 }));
        });
    }

    // --- Sibling-scoped slug query + encoded-slug round-trip (#10 / #7) ---

    [Test]
    public async Task IsSlugAvailableAsync_QueriesOnlyDirectSiblings_NotAllPages()
    {
        var parentNodeId = Guid.NewGuid();
        _store.FindCurrentDraftsRelatedAsync(PageModel.ParentRelation, parentNodeId, Arg.Any<CancellationToken>()).Returns(new List<PageDTO>());
        _cmsRouteService.GetByOwningContentAsync(parentNodeId, Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _store.SaveDraftAsync(Arg.Any<PageDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));

        var result = await _model.SaveUpsertAsync(new PageUpsertViewModel
        {
            Title = "Child",
            Slug = "child",
            ParentNodeId = parentNodeId,
            ControllerName = "C"
        });

        Assert.That(result.Success, Is.True);
        await _store.Received(1).FindCurrentDraftsRelatedAsync(PageModel.ParentRelation, parentNodeId, Arg.Any<CancellationToken>());
        await _store.DidNotReceive().GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SaveUpsertAsync_ReSubmittedAlreadyEncodedSlug_ViaViewModel_StillDetectsCollisionCorrectly()
    {
        var parentNodeId = Guid.NewGuid();
        var sibling = Page(title: "Hello World");
        sibling.Version.Slug = Uri.EscapeDataString(sibling.Version.Slug);
        sibling.Version.Relations.Add(new ContentRelation { Name = PageModel.ParentRelation, TargetNodeId = parentNodeId, Kind = RelationKind.Containment });

        var decodedVm = _mapper.Map<PageUpsertViewModel>(sibling);
        Assert.That(decodedVm.Slug, Is.EqualTo("hello world"));

        _store.FindCurrentDraftsRelatedAsync(PageModel.ParentRelation, parentNodeId, Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { sibling });
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await _model.SaveUpsertAsync(new PageUpsertViewModel
        {
            Title = "Hello World",
            Slug = decodedVm.Slug,
            ParentNodeId = parentNodeId,
            ControllerName = "C"
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorField, Is.EqualTo("Slug"));
        });
    }

    // ─── zone drafts: pending flag + publish cascade ──────────────────────────

    [Test]
    public async Task GetPageIndexAsync_PublishedPageWithPendingZoneDrafts_HasPendingChanges()
    {
        var nodeId = Guid.NewGuid();
        var page = Page(title: "T", state: ContentVersionState.Published, nodeId: nodeId);

        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { page });
        _store.GetPublishedNodeIdsAsync(Arg.Any<CancellationToken>()).Returns(new HashSet<Guid> { nodeId });
        _pageHistory.GetPagesWithUnpublishedChangesAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid> { nodeId });
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO> { RouteFor(nodeId, "/t") });

        var vm = await _model.GetPageIndexAsync();

        Assert.That(vm.Pages.Single(p => p.Path == "/t").HasPendingChanges, Is.True);
    }

    private Guid ArrangeSuccessfulPagePublish()
    {
        var nodeId = Guid.NewGuid();
        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(Page(title: "Test", nodeId: nodeId));
        _store.PublishAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));
        _cmsRouteService.GetByOwningContentAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _routeRegistration.RegisterContentRoutesAsync(Arg.Any<IRoutableContent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns((true, null));
        return nodeId;
    }

    private static ContentZoneItemDTO ZoneItem(string component, bool active = true)
        => new()
        {
            ComponentName = component,
            IsActive = active,
            Version = new ContentVersion { Node = new ContentNode { Id = Guid.NewGuid() } }
        };

    [Test]
    public async Task PublishPageAsync_PublishesZoneDraftsAndRegistersTheirRoutes()
    {
        var nodeId = ArrangeSuccessfulPagePublish();
        var active = ZoneItem("Faq");
        var removed = ZoneItem("Faq", active: false);
        _contentZoneService.PublishPageZonesAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new PageZonePublishResult([active, removed], []));
        _routeRegistration.TryRegisterWidgetRoutesAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((true, null));

        var result = await _model.PublishPageAsync(nodeId);

        Assert.That(result.Success, Is.True);
        await _routeRegistration.Received(1).TryRegisterWidgetRoutesAsync("Faq", active.Version.Node.Id, nodeId, true, active.MetadataJson, Arg.Any<CancellationToken>());
        await _routeRegistration.Received(1).TryRegisterWidgetRoutesAsync("Faq", removed.Version.Node.Id, nodeId, false, removed.MetadataJson, Arg.Any<CancellationToken>());
        _changeSetScope.Received(1).Begin(ChangeSetKind.Publish, nodeId, null);
    }

    [Test]
    public async Task PublishPageAsync_ZoneFailures_ReportedAfterThePagePublishes()
    {
        var nodeId = ArrangeSuccessfulPagePublish();
        var withMessage = ZoneItem("A");
        var withoutMessage = ZoneItem("B");
        _contentZoneService.PublishPageZonesAsync(nodeId, Arg.Any<CancellationToken>())
            .Returns(new PageZonePublishResult([withMessage, withoutMessage], ["item failed."]));
        _routeRegistration.TryRegisterWidgetRoutesAsync("A", Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((false, "route taken."));
        _routeRegistration.TryRegisterWidgetRoutesAsync("B", Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((false, null));

        var (success, error, _) = await _model.PublishPageAsync(nodeId);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(error, Does.StartWith("Page published; some zone changes failed:"));
            Assert.That(error, Does.Contain("item failed."));
            Assert.That(error, Does.Contain("route taken."));
            Assert.That(error, Does.Contain("Failed to register routes for widget 'B'."));
        });
    }

    // ─── page-set history ─────────────────────────────────────────────────────

    [Test]
    public async Task SavePageUpsertAsync_RecordsAPageHistoryEntry_CreatedOrEdited()
    {
        var created = Guid.NewGuid();
        var edited = Guid.NewGuid();
        _store.SaveDraftAsync(Arg.Any<PageDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true, NodeId: created), new ContentWriteResult(true, NodeId: edited));

        await _model.SavePageUpsertAsync(new PageUpsertViewModel { Title = "New", ControllerName = "GenericPage" });
        await _model.SavePageUpsertAsync(new PageUpsertViewModel { NodeId = edited, Title = "Old", ControllerName = "GenericPage" });

        await _pageHistory.Received(1).RecordDraftAsync(created, "Page created", Arg.Any<CancellationToken>());
        await _pageHistory.Received(1).RecordDraftAsync(edited, "Page edited", Arg.Any<CancellationToken>());
        _changeSetScope.Received(1).UseChangeNote("Page created");
        _changeSetScope.Received(1).UseChangeNote("Page edited");
    }

    [Test]
    public async Task SavePageUpsertAsync_FailedSave_RecordsNothing()
    {
        _store.SaveDraftAsync(Arg.Any<PageDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(false, "stale"));

        var result = await _model.SavePageUpsertAsync(new PageUpsertViewModel { NodeId = Guid.NewGuid(), Title = "T", ControllerName = "GenericPage" });

        Assert.That(result, Is.EqualTo((false, (string?)"stale")));
        await _pageHistory.DidNotReceive().RecordDraftAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RestoreVersionAsync_RecordsAnEntryNamingTheRestoredVersion_InARestoreChangeSet()
    {
        var nodeId = Guid.NewGuid();
        var newVersionId = Guid.NewGuid();
        var restored = Page(nodeId: nodeId);
        restored.Version.ChangeNote = "Restored from version 2";
        _store.RestoreAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true, VersionId: newVersionId, NodeId: nodeId));
        _store.GetVersionAsync(newVersionId, Arg.Any<CancellationToken>()).Returns(restored);

        var result = await _model.RestoreVersionAsync(Guid.NewGuid());

        Assert.That(result.Success, Is.True);
        _changeSetScope.Received(1).Begin(ChangeSetKind.Restore, null, null);
        await _pageHistory.Received(1).RecordDraftAsync(nodeId, "Page: Restored from version 2", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RestoreVersionAsync_Failure_RecordsNothing()
    {
        _store.RestoreAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(false, "Version not found."));

        var result = await _model.RestoreVersionAsync(Guid.NewGuid());

        Assert.That(result, Is.EqualTo(new AdminSaveResult(false, "Version not found.")));
        await _pageHistory.DidNotReceive().RecordDraftAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void Constructor_NullPageHistory_Throws()
    {
        Assert.That(() => new PageModel(_store, _mapper, _registry, _viewDiscovery, _routeRegistration, _cmsRouteService, _contentZoneService, null!, _routeStore, _transaction, _routeRegistry, _changeSetScope, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes()),
            Throws.ArgumentNullException);
    }

    [Test]
    public async Task PublishPageAsync_Success_RecordsPageHistory()
    {
        var nodeId = ArrangeSuccessfulPagePublish();

        var result = await _model.PublishPageAsync(nodeId);

        Assert.That(result.Success, Is.True);
        await _pageHistory.Received(1).CaptureAsync(nodeId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishPageAsync_ZoneFailures_StillRecordsWhatWentLive()
    {
        var nodeId = ArrangeSuccessfulPagePublish();
        _contentZoneService.PublishPageZonesAsync(nodeId, Arg.Any<CancellationToken>())
            .Returns(new PageZonePublishResult([], ["item failed."]));

        var result = await _model.PublishPageAsync(nodeId);

        Assert.That(result.Success, Is.False);
        await _pageHistory.Received(1).CaptureAsync(nodeId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishPageAsync_PageFails_RecordsNoHistory()
    {
        var nodeId = ArrangeSuccessfulPagePublish();
        _store.PublishAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(false, "nope"));

        await _model.PublishPageAsync(nodeId);

        await _pageHistory.DidNotReceive().CaptureAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishZoneItemsAsync_RecordsPageHistory()
    {
        var pageNodeId = Guid.NewGuid();
        IReadOnlyCollection<Guid> ids = [Guid.NewGuid()];
        _contentZoneService.PublishPageZoneItemsAsync(pageNodeId, ids, Arg.Any<CancellationToken>())
            .Returns(new PageZonePublishResult([], []));

        await _model.PublishZoneItemsAsync(pageNodeId, ids);

        await _pageHistory.Received(1).CaptureAsync(pageNodeId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryDeleteVersionAsync_VersionInPageHistory_IsRefusedWithTheEntry()
    {
        var versionId = Guid.NewGuid();
        var entry = new PageSetSnapshot { Id = Guid.NewGuid(), CreatedUtc = new DateTime(2026, 10, 1, 9, 30, 0, DateTimeKind.Utc) };
        _pageHistory.GetSnapshotsReferencingVersionAsync(versionId, Arg.Any<CancellationToken>()).Returns([entry]);

        var (success, error) = await _model.TryDeleteVersionAsync(versionId);
        var deleted = await _model.DeleteVersionAsync(versionId);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(error, Is.EqualTo(PageHistoryMessages.VersionInHistory(entry)));
            Assert.That(error, Does.Contain("2026-10-01 09:30"));
            Assert.That(deleted, Is.False);
        });
        await _store.DidNotReceive().DeleteVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryDeleteVersionAsync_NotInHistory_DeletesOrExplains()
    {
        _store.DeleteVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true, false);

        var deleted = await _model.TryDeleteVersionAsync(Guid.NewGuid());
        var refused = await _model.TryDeleteVersionAsync(Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.EqualTo((true, (string?)null)));
            Assert.That(refused, Is.EqualTo((false, IAdminCrudHandler.VersionNotDeletedMessage)));
        });
    }

    [Test]
    public async Task PublishPageAsync_PageFails_LeavesZoneDraftsAlone()
    {
        var nodeId = ArrangeSuccessfulPagePublish();
        _store.PublishAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(false, "nope"));

        var result = await _model.PublishPageAsync(nodeId);

        Assert.That(result.Success, Is.False);
        await _contentZoneService.DidNotReceive().PublishPageZonesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishZoneItemsAsync_PublishesOnlyTheListedItemsAndRegistersTheirRoutes()
    {
        var pageNodeId = Guid.NewGuid();
        var listed = ZoneItem("A");
        var routeless = ZoneItem("B");
        var failed = Guid.NewGuid();
        IReadOnlyCollection<Guid> ids = [listed.Version.Node.Id, routeless.Version.Node.Id, failed];
        _contentZoneService.PublishPageZoneItemsAsync(pageNodeId, ids, Arg.Any<CancellationToken>())
            .Returns(new PageZonePublishResult([listed, routeless], ["item failed."]) { FailedItemNodeIds = [failed] });
        _routeRegistration.TryRegisterWidgetRoutesAsync("A", Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((true, null));
        _routeRegistration.TryRegisterWidgetRoutesAsync("B", Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((false, "route taken."));

        var result = await _model.PublishZoneItemsAsync(pageNodeId, ids);

        Assert.Multiple(() =>
        {
            Assert.That(result.Published, Is.EqualTo(new[] { listed, routeless }));
            Assert.That(result.Errors, Is.EqualTo(new[] { "item failed.", "route taken." }));
            Assert.That(result.FailedItemNodeIds, Is.EqualTo(new[] { failed }));
        });
        await _routeRegistration.Received(1).TryRegisterWidgetRoutesAsync("A", listed.Version.Node.Id, pageNodeId, true, listed.MetadataJson, Arg.Any<CancellationToken>());
        // The page itself is left alone.
        await _store.DidNotReceive().PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _contentZoneService.DidNotReceive().PublishPageZonesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _changeSetScope.Received(1).Begin(ChangeSetKind.Publish, pageNodeId, null);
    }

    [Test]
    public async Task PublishPageAsync_RouteRegistrationFails_PreviousRouteRejected_ReportsIt()
    {
        var nodeId = Guid.NewGuid();
        var page = Page(title: "Test", nodeId: nodeId);
        var withMessage = RouteFor(nodeId, "/blog/{page}");
        var withoutMessage = RouteFor(nodeId, "/other");
        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _store.PublishAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));
        _cmsRouteService.GetByOwningContentAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO> { withMessage, withoutMessage });
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _cmsRouteService.UpsertAsync(withMessage, Arg.Any<CancellationToken>()).Returns((false, "reserved name.", (CMSRouteDTO?)null));
        _cmsRouteService.UpsertAsync(withoutMessage, Arg.Any<CancellationToken>()).Returns((false, (string?)null, (CMSRouteDTO?)null));
        _routeRegistration.RegisterContentRoutesAsync(Arg.Any<IRoutableContent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns((false, (string?)null));

        var (success, error, _) = await _model.PublishPageAsync(nodeId);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(error, Does.StartWith("Failed to register the page route. The previous route could not be restored:"));
            Assert.That(error, Does.Contain("reserved name."));
            Assert.That(error, Does.Contain("Route '/other' could not be restored."));
        });
    }
}

public class SampleSaveConfig
{
    public int Value { get; set; }
}
