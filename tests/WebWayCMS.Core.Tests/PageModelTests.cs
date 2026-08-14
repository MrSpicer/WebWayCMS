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
    private IChangeSetScope _changeSetScope = null!;
    private PageModel _model = null!;

    [SetUp]
    public void SetUp()
    {
        _store = Substitute.For<IContentStore<PageDTO>>();
        _mapper = TestSupport.CreateMapper();
        _registry = Substitute.For<IPageControllerRegistry>();
        _viewDiscovery = Substitute.For<IViewDiscoveryService>();
        _routeRegistration = Substitute.For<IRouteRegistrationService>();
        _cmsRouteService = Substitute.For<ICMSRouteService>();
        _changeSetScope = Substitute.For<IChangeSetScope>();
        _model = new PageModel(_store, _mapper, _registry, _viewDiscovery, _routeRegistration, _cmsRouteService, _changeSetScope);
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
            Assert.That(() => new PageModel(null!, _mapper, _registry, _viewDiscovery, _routeRegistration, _cmsRouteService, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_store, null!, _registry, _viewDiscovery, _routeRegistration, _cmsRouteService, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_store, _mapper, null!, _viewDiscovery, _routeRegistration, _cmsRouteService, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_store, _mapper, _registry, null!, _routeRegistration, _cmsRouteService, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_store, _mapper, _registry, _viewDiscovery, null!, _cmsRouteService, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_store, _mapper, _registry, _viewDiscovery, _routeRegistration, null!, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_store, _mapper, _registry, _viewDiscovery, _routeRegistration, _cmsRouteService, null!), Throws.ArgumentNullException);
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
    public async Task GetPageUpsertAsync_NullId_FoundAndNotFound()
    {
        var page = Page();
        _store.GetCurrentDraftAsync(page.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(page);
        _store.GetCurrentDraftAsync(Arg.Is<Guid>(g => g != page.Version.Node.Id), Arg.Any<CancellationToken>()).Returns((PageDTO?)null);

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetPageUpsertAsync(null), Is.Not.Null);
            Assert.That(await _model.GetPageUpsertAsync(Guid.Empty), Is.Not.Null);
            Assert.That(await _model.GetPageUpsertAsync(page.Version.Node.Id), Is.Not.Null);
            Assert.That(await _model.GetPageUpsertAsync(Guid.NewGuid()), Is.Null);
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
    }

    [Test]
    public async Task DeletePageAsync_NotFound_StillDeletesWithoutUnregistering()
    {
        _store.GetCurrentDraftAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PageDTO?)null);
        _store.DeleteAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>()).Returns(true);

        Assert.That(await _model.DeletePageAsync(Guid.NewGuid()), Is.True);
        await _routeRegistration.DidNotReceive().UnregisterContentRoutesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task VersionHistory_RestoreAndDeleteVersion()
    {
        var nodeId = Guid.NewGuid();
        _store.GetAllVersionsAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { Page(nodeId: nodeId) });
        Assert.That(await _model.GetVersionHistoryAsync(nodeId), Is.Not.Null);

        var historical = Page();
        _store.GetVersionAsync(historical.VersionId, Arg.Any<CancellationToken>()).Returns(historical);
        Assert.That(((PageUpsertViewModel)(await _model.GetRestoreVersionViewModelAsync(historical.VersionId))!).NodeId, Is.EqualTo(historical.Version.Node.Id));

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
        await _cmsRouteService.Received(1).IsPatternAvailableAsync("/About", null, null, Arg.Any<CancellationToken>());
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
        _store.PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(false, "err"));

        var result = await _model.PublishPageAsync(Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("err"));
        });
    }

    [Test]
    public async Task PublishPageAsync_PageMissingAfterPublish_ReturnsError()
    {
        _store.PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));
        _store.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PageDTO?)null);

        var result = await _model.PublishPageAsync(Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("Failed to read published page."));
        });
    }

    [Test]
    public async Task PublishPageAsync_Success_RegistersRouteFromSlug()
    {
        var nodeId = Guid.NewGuid();
        var page = Page(title: "Test", nodeId: nodeId);
        _store.PublishAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));
        _store.GetAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _cmsRouteService.GetByOwningContentAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());

        var result = await _model.PublishPageAsync(nodeId);

        Assert.That(result.Success, Is.True);
        await _routeRegistration.Received(1).RegisterContentRoutesAsync(
            _model, "/test", "GenericPage", nodeId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishPageAsync_HomeSlug_DerivesRoutePatternToRoot()
    {
        var nodeId = Guid.NewGuid();
        var page = Page(title: "Home", nodeId: nodeId);
        _store.PublishAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));
        _store.GetAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _cmsRouteService.GetByOwningContentAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());

        var result = await _model.PublishPageAsync(nodeId);

        Assert.That(result.Success, Is.True);
        await _routeRegistration.Received(1).RegisterContentRoutesAsync(
            _model, "/", "GenericPage", nodeId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishPageAsync_ExistingRoutePreservesPrefix()
    {
        var nodeId = Guid.NewGuid();
        var page = Page(title: "New", nodeId: nodeId);
        _store.PublishAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));
        _store.GetAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _cmsRouteService.GetByOwningContentAsync(nodeId, Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { new() { Pattern = "/blog/old-slug" } });

        var result = await _model.PublishPageAsync(nodeId);

        Assert.That(result.Success, Is.True);
        await _routeRegistration.Received(1).RegisterContentRoutesAsync(
            _model, "/blog/new", "GenericPage", nodeId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishPageAsync_ExistingRootRoute_NoPrefix()
    {
        var nodeId = Guid.NewGuid();
        var page = Page(title: "New", nodeId: nodeId);
        _store.PublishAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));
        _store.GetAsync(nodeId, Arg.Any<CancellationToken>()).Returns(page);
        _cmsRouteService.GetByOwningContentAsync(nodeId, Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { new() { Pattern = "/old-slug" } });

        var result = await _model.PublishPageAsync(nodeId);

        Assert.That(result.Success, Is.True);
        await _routeRegistration.Received(1).RegisterContentRoutesAsync(
            _model, "/new", "GenericPage", nodeId, Arg.Any<CancellationToken>());
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
        _store.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Page(title: "T"));
        _cmsRouteService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());

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
}

public class SampleSaveConfig
{
    public int Value { get; set; }
}
