using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Interfaces;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Page;
using WebWayCMS.Pages;
using WebWayCMS.Services;

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
        _changeSetScope = Substitute.For<IChangeSetScope>();
        _model = new PageModel(_store, _mapper, _registry, _viewDiscovery, _routeRegistration, _cmsRouteService, _contentZoneService, _changeSetScope);
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
            Assert.That(() => new PageModel(null!, _mapper, _registry, _viewDiscovery, _routeRegistration, _cmsRouteService, _contentZoneService, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_store, null!, _registry, _viewDiscovery, _routeRegistration, _cmsRouteService, _contentZoneService, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_store, _mapper, null!, _viewDiscovery, _routeRegistration, _cmsRouteService, _contentZoneService, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_store, _mapper, _registry, null!, _routeRegistration, _cmsRouteService, _contentZoneService, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_store, _mapper, _registry, _viewDiscovery, null!, _cmsRouteService, _contentZoneService, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_store, _mapper, _registry, _viewDiscovery, _routeRegistration, null!, _contentZoneService, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_store, _mapper, _registry, _viewDiscovery, _routeRegistration, _cmsRouteService, null!, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_store, _mapper, _registry, _viewDiscovery, _routeRegistration, _cmsRouteService, _contentZoneService, null!), Throws.ArgumentNullException);
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
    public async Task DeletePageAsync_DelegatesAndUnregistersRoute()
    {
        var page = Page();
        _store.GetCurrentDraftAsync(page.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(page);
        _store.DeleteAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>()).Returns(true);

        Assert.That(await _model.DeletePageAsync(page.Version.Node.Id), Is.True);
        await _routeRegistration.Received(1).UnregisterContentRoutesAsync(page.Version.Node.Id, Arg.Any<CancellationToken>());
        await _contentZoneService.Received(1).DeletePageZonesAsync(page.Version.Node.Id, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeletePageAsync_NotFound_StillDeletesWithoutUnregistering()
    {
        _store.GetCurrentDraftAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PageDTO?)null);
        _store.DeleteAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>()).Returns(true);

        Assert.That(await _model.DeletePageAsync(Guid.NewGuid()), Is.True);
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
        historical.Version.Node.ParentNodeId = parentNodeId;

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
        page.Version.Node.ParentNodeId = parentNodeId;

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
        sibling.Version.Node.ParentNodeId = parentNodeId;

        _store.GetCurrentDraftChildrenAsync(parentNodeId, Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { sibling });
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

        _store.GetCurrentDraftChildrenAsync(null, Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { sibling });

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
        page.Version.Node.ParentNodeId = parentNodeId;

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
        var page = Page(title: "Test", nodeId: nodeId);
        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _store.PublishAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));
        _cmsRouteService.GetByOwningContentAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _routeRegistration.RegisterContentRoutesAsync(Arg.Any<IRoutableContent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns((false, "collision"));

        var result = await _model.PublishPageAsync(nodeId);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("collision"));
        });
        await _store.Received(1).UnpublishAsync(nodeId, Arg.Any<CancellationToken>());
        await _cmsRouteService.DidNotReceive().UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishPageAsync_RouteRegistrationFails_Republish_RestoresPreviousRoute()
    {
        var nodeId = Guid.NewGuid();
        var page = Page(title: "Test", nodeId: nodeId);
        var existingRoute = RouteFor(nodeId, "/old-slug");
        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _store.PublishAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));
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
        await _store.Received(1).UnpublishAsync(nodeId, Arg.Any<CancellationToken>());
        await _cmsRouteService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(r => r.Pattern == "/old-slug"), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishPageAsync_DuplicateSiblingSlug_FailsLoudly()
    {
        var nodeId = Guid.NewGuid();
        var parentNodeId = Guid.NewGuid();
        var page = Page(title: "Child", nodeId: nodeId);
        page.Version.Node.ParentNodeId = parentNodeId;

        var sibling = Page(title: "Sibling");
        sibling.Version.Node.ParentNodeId = parentNodeId;
        sibling.Version.Slug = "child";

        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _store.GetCurrentDraftChildrenAsync(parentNodeId, Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { page, sibling });

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
        child.Version.Node.ParentNodeId = parentNodeId;

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
    public async Task GetPageIndexAsync_UnpublishedChildUnderRoot_ResolvesUnderSlash()
    {
        var rootNodeId = Guid.NewGuid();
        var root = Page(title: "Home", nodeId: rootNodeId);
        var child = Page(title: "Child");
        child.Version.Node.ParentNodeId = rootNodeId;

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

    [Test]
    public async Task GetPageIndexAsync_BuildTree_CircularParent_DoesNotInfiniteLoop()
    {
        var a = Page(title: "A");
        var b = Page(title: "B");
        a.Version.Node.ParentNodeId = b.Version.Node.Id;
        b.Version.Node.ParentNodeId = a.Version.Node.Id;

        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { a, b });
        _store.GetPublishedNodeIdsAsync(Arg.Any<CancellationToken>()).Returns(new HashSet<Guid>());
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());

        var vm = await _model.GetPageIndexAsync();

        Assert.That(vm.Pages, Has.Count.EqualTo(2));
    }

    // --- Sibling-scoped slug query + encoded-slug round-trip (#10 / #7) ---

    [Test]
    public async Task IsSlugAvailableAsync_QueriesOnlyDirectSiblings_NotAllPages()
    {
        var parentNodeId = Guid.NewGuid();
        _store.GetCurrentDraftChildrenAsync(parentNodeId, Arg.Any<CancellationToken>()).Returns(new List<PageDTO>());
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
        await _store.Received(1).GetCurrentDraftChildrenAsync(parentNodeId, Arg.Any<CancellationToken>());
        await _store.DidNotReceive().GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SaveUpsertAsync_ReSubmittedAlreadyEncodedSlug_ViaViewModel_StillDetectsCollisionCorrectly()
    {
        var parentNodeId = Guid.NewGuid();
        var sibling = Page(title: "Hello World");
        sibling.Version.Slug = Uri.EscapeDataString(sibling.Version.Slug);
        sibling.Version.Node.ParentNodeId = parentNodeId;

        var decodedVm = _mapper.Map<PageUpsertViewModel>(sibling);
        Assert.That(decodedVm.Slug, Is.EqualTo("hello world"));

        _store.GetCurrentDraftChildrenAsync(parentNodeId, Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { sibling });
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
}

public class SampleSaveConfig
{
    public int Value { get; set; }
}
