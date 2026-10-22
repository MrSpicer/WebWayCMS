using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.ContentZones;
using WebWayCMS.Controllers;
using WebWayCMS.Controllers.Admin;
using WebWayCMS.Controllers.Api;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Services;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class AdminContentZoneControllerTests
{
    [Test]
    public void Constructor_NullArguments_Throw()
    {
        var model = Substitute.For<IContentZoneModel>();
        var registry = Substitute.For<IWidgetRegistry>();
        var pageStore = Substitute.For<IContentStore<PageDTO>>();
        var zoneService = Substitute.For<IContentZoneService>();

        Assert.Multiple(() =>
        {
            Assert.That(() => new AdminContentZoneController(null!, registry, pageStore, zoneService), Throws.ArgumentNullException);
            Assert.That(() => new AdminContentZoneController(model, null!, pageStore, zoneService), Throws.ArgumentNullException);
            Assert.That(() => new AdminContentZoneController(model, registry, null!, zoneService), Throws.ArgumentNullException);
            Assert.That(() => new AdminContentZoneController(model, registry, pageStore, null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public async Task ZoneEdit_NotFoundAndView()
    {
        var model = Substitute.For<IContentZoneModel>();
        var controller = new AdminContentZoneController(model, Substitute.For<IWidgetRegistry>(),
            Substitute.For<IContentStore<PageDTO>>(), Substitute.For<IContentZoneService>());
        new MvcHarness().Configure(controller, new[] { "Admin" });

        model.GetViewModelByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ContentZoneViewModel?)null, new ContentZoneViewModel());

        Assert.Multiple(async () =>
        {
            Assert.That(await controller.ZoneEdit(Guid.NewGuid(), default), Is.InstanceOf<NotFoundResult>());
            Assert.That(await controller.ZoneEdit(Guid.NewGuid(), default), Is.InstanceOf<ViewResult>());
        });
    }

    [Test]
    public async Task ZoneEdit_MarksRequestAsUncachedDraftRead()
    {
        var model = Substitute.For<IContentZoneModel>();
        var controller = new AdminContentZoneController(model, Substitute.For<IWidgetRegistry>(),
            Substitute.For<IContentStore<PageDTO>>(), Substitute.For<IContentZoneService>());
        new MvcHarness().Configure(controller, new[] { "Admin" });
        model.GetViewModelByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new ContentZoneViewModel());

        await controller.ZoneEdit(Guid.NewGuid(), default);

        Assert.Multiple(() =>
        {
            Assert.That(controller.HttpContext.Items[WebWayCMS.PreviewConstants.DraftRequestItemKey], Is.EqualTo(true));
            Assert.That(controller.Response.Headers.CacheControl.ToString(), Is.EqualTo(WebWayCMS.PreviewConstants.DraftCacheControl));
        });
    }
}

[TestFixture]
public class AdminContentZoneControllerPageZonesTests
{
    private IContentZoneModel _model = null!;
    private IContentStore<PageDTO> _pageStore = null!;
    private IContentZoneService _zoneService = null!;
    private AdminContentZoneController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _model = Substitute.For<IContentZoneModel>();
        _pageStore = Substitute.For<IContentStore<PageDTO>>();
        _zoneService = Substitute.For<IContentZoneService>();
        _controller = new AdminContentZoneController(_model, Substitute.For<IWidgetRegistry>(), _pageStore, _zoneService);
        new MvcHarness().Configure(_controller, new[] { "Admin" });
    }

    private static PageDTO Page(Guid nodeId, ContentVersionState state, string title = "About")
        => new()
        {
            Version = new ContentVersion { Node = new ContentNode { Id = nodeId }, Title = title, State = state }
        };

    [Test]
    public async Task PageZones_MissingPage_ReturnsNotFound()
    {
        _pageStore.GetCurrentDraftAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PageDTO?)null);

        Assert.That(await _controller.PageZones(Guid.NewGuid(), default), Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public async Task PageZones_PublishedPageWithZoneDrafts_ReadsDraftsAndReportsPending()
    {
        var pageId = Guid.NewGuid();
        var page = Page(pageId, ContentVersionState.Published);
        var slots = new List<PageZoneSlot> { new("Main", Guid.NewGuid()) };
        _pageStore.GetCurrentDraftAsync(pageId, Arg.Any<CancellationToken>()).Returns(page);
        _pageStore.GetAllVersionsAsync(pageId, Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { page });
        _zoneService.GetPagesWithPendingZoneChangesAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid> { pageId });
        _model.GetPageZoneSlotsAsync(pageId, Arg.Any<CancellationToken>()).Returns(slots);

        var view = (ViewResult)await _controller.PageZones(pageId, default);
        var vm = (PageZonesEditViewModel)view.Model!;

        Assert.Multiple(() =>
        {
            Assert.That(view.ViewName, Is.EqualTo("~/Views/AdminContentZone/PageZonesEdit.cshtml"));
            Assert.That(_controller.HttpContext.Items[WebWayCMS.PreviewConstants.DraftRequestItemKey], Is.EqualTo(true));
            Assert.That(vm.PageNodeId, Is.EqualTo(pageId));
            Assert.That(vm.PageTitle, Is.EqualTo("About"));
            Assert.That(vm.IsPublished, Is.True);
            Assert.That(vm.HasPendingChanges, Is.True);
            Assert.That(vm.HasPendingZoneChanges, Is.True);
            Assert.That(vm.Slots, Is.SameAs(slots));
        });
    }

    [Test]
    public async Task PageZones_UpToDateAndNeverPublished_Variants()
    {
        var liveId = Guid.NewGuid();
        var live = Page(liveId, ContentVersionState.Published);
        var newId = Guid.NewGuid();
        var draft = Page(newId, ContentVersionState.Draft);
        _pageStore.GetCurrentDraftAsync(liveId, Arg.Any<CancellationToken>()).Returns(live);
        _pageStore.GetAllVersionsAsync(liveId, Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { live });
        _pageStore.GetCurrentDraftAsync(newId, Arg.Any<CancellationToken>()).Returns(draft);
        _pageStore.GetAllVersionsAsync(newId, Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { draft });
        _zoneService.GetPagesWithPendingZoneChangesAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid>());

        var upToDate = (PageZonesEditViewModel)((ViewResult)await _controller.PageZones(liveId, default)).Model!;
        var neverPublished = (PageZonesEditViewModel)((ViewResult)await _controller.PageZones(newId, default)).Model!;

        Assert.Multiple(() =>
        {
            Assert.That(upToDate.IsPublished, Is.True);
            Assert.That(upToDate.HasPendingChanges, Is.False);
            Assert.That(neverPublished.IsPublished, Is.False);
            Assert.That(neverPublished.HasPendingChanges, Is.True);
            Assert.That(neverPublished.HasPendingZoneChanges, Is.False);
        });
    }

    [Test]
    public async Task DiscardPageZones_RedirectsBackToTheEditor_ReportingFailures()
    {
        var pageId = Guid.NewGuid();
        _zoneService.DiscardPageZoneDraftsAsync(pageId, Arg.Any<CancellationToken>()).Returns((true, (string?)null), (false, "boom"));

        var ok = (RedirectToActionResult)await _controller.DiscardPageZones(pageId, default);
        Assert.That(_controller.TempData.ContainsKey("Error"), Is.False);

        var failed = (RedirectToActionResult)await _controller.DiscardPageZones(pageId, default);

        Assert.Multiple(() =>
        {
            Assert.That(ok.ActionName, Is.EqualTo(nameof(AdminContentZoneController.PageZones)));
            Assert.That(ok.RouteValues!["pageNodeId"], Is.EqualTo(pageId));
            Assert.That(failed.ActionName, Is.EqualTo(nameof(AdminContentZoneController.PageZones)));
            Assert.That(_controller.TempData["Error"], Is.EqualTo("boom"));
        });
    }
}

[TestFixture]
public class ContentZoneApiControllerTests
{
    private IContentZoneService _service = null!;
    private IRouteRegistrationService _routeRegistration = null!;
    private IWidgetRegistry _widgetRegistry = null!;
    private ContentZoneApiController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _service = Substitute.For<IContentZoneService>();
        _routeRegistration = Substitute.For<IRouteRegistrationService>();
        _widgetRegistry = Substitute.For<IWidgetRegistry>();
        _widgetRegistry.GetByName("Lister").Returns(new WidgetRegistrationInfo { Name = "Lister", SupportsList = true });
        _controller = new ContentZoneApiController(_service, _routeRegistration, _widgetRegistry);
        new MvcHarness().Configure(_controller, new[] { "Admin" });
        _routeRegistration.TryRegisterWidgetRoutesAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns((true, null));
    }

    private static ContentZoneItemDTO Item(Guid itemNodeId, Guid zoneNodeId, bool active = true)
        => new()
        {
            ContentZoneNodeId = zoneNodeId,
            ComponentName = "X",
            IsActive = active,
            Version = new ContentVersion { Node = new ContentNode { Id = itemNodeId } }
        };

    private static object? ValueOf(object? obj, string prop)
        => obj?.GetType().GetProperty(prop)?.GetValue(obj);

    [Test]
    public void Constructor_Null_Throws()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new ContentZoneApiController(null!, _routeRegistration, _widgetRegistry), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneApiController(_service, null!, _widgetRegistry), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneApiController(_service, _routeRegistration, null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public async Task SaveItem_ValidationErrors()
    {
        Assert.Multiple(async () =>
        {
            Assert.That(await _controller.SaveItem(null!, default), Is.InstanceOf<BadRequestObjectResult>());
            Assert.That(await _controller.SaveItem(new SaveItemRequest { ComponentName = "", ZoneName = "Z" }, default), Is.InstanceOf<BadRequestObjectResult>());
            Assert.That(await _controller.SaveItem(new SaveItemRequest { ComponentName = "C", ZoneName = "" }, default), Is.InstanceOf<BadRequestObjectResult>());
        });
    }

    [Test]
    public async Task SaveItem_ByZoneId_CreatesItem()
    {
        var zoneId = Guid.NewGuid();
        _service.AddItemAsync(zoneId, Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>())
            .Returns(c =>
            {
                var item = c.Arg<ContentZoneItemDTO>();
                item.Version.Node = new ContentNode { Id = Guid.NewGuid() };
                return item;
            });
        _service.GetParentPageNodeForZoneAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var result = await _controller.SaveItem(new SaveItemRequest { ComponentName = "C", ZoneName = "Z", ZoneId = zoneId }, default);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public async Task SaveItem_Create_WithListMetadata_StoresNormalizedMetadata()
    {
        var zoneId = Guid.NewGuid();
        ContentZoneItemDTO? added = null;
        _service.AddItemAsync(zoneId, Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>())
            .Returns(c =>
            {
                added = c.Arg<ContentZoneItemDTO>();
                added.Version.Node = new ContentNode { Id = Guid.NewGuid() };
                return added;
            });

        var result = await _controller.SaveItem(new SaveItemRequest
        {
            ComponentName = "Lister", ZoneName = "Z", ZoneId = zoneId, MetadataJson = "{\"DisplayMode\":\"List\"}"
        }, default);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            Assert.That(added!.MetadataJson, Is.EqualTo("{\"DisplayMode\":\"List\"}"));
        });
    }

    [Test]
    public async Task SaveItem_Create_ListMetadataForWidgetWithoutListSupport_ReturnsBadRequest()
    {
        var result = await _controller.SaveItem(new SaveItemRequest
        {
            ComponentName = "C", ZoneName = "Z", ZoneId = Guid.NewGuid(), MetadataJson = "{\"DisplayMode\":\"List\"}"
        }, default);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        await _service.DidNotReceiveWithAnyArgs().AddItemAsync(default, default!, default);
    }

    [Test]
    public async Task SaveItem_ByPageSlot_UpdatesItem_SuccessAndNotFound()
    {
        var pageNodeId = Guid.NewGuid();
        var zone = new ContentZoneDTO { Version = new ContentVersion { Node = new ContentNode { Id = Guid.NewGuid() } } };
        _service.GetOrCreateByPageSlotAsync(pageNodeId, "Main", Arg.Any<CancellationToken>()).Returns((zone, new ContentZoneAssignmentDTO()));
        _service.UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>()).Returns(true, false);
        _service.GetItemByNodeIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new ContentZoneItemDTO { Version = new ContentVersion { Node = new ContentNode { Id = Guid.NewGuid() } } });

        var req = new SaveItemRequest { ComponentName = "C", ZoneName = "Z", ParentPageNodeId = pageNodeId, SlotName = "Main", ItemId = Guid.NewGuid() };

        Assert.Multiple(async () =>
        {
            Assert.That(await _controller.SaveItem(req, default), Is.InstanceOf<OkObjectResult>());
            Assert.That(await _controller.SaveItem(req, default), Is.InstanceOf<NotFoundObjectResult>());
        });
    }

    [Test]
    public async Task SaveItem_UpdateItem_StaleVersion_ReturnsConflictWithoutSaving()
    {
        var zoneId = Guid.NewGuid();
        var itemNodeId = Guid.NewGuid();
        var existing = Item(itemNodeId, zoneId);
        existing.Version = new ContentVersion { VersionNumber = 5, Node = new ContentNode { Id = itemNodeId } };
        _service.GetItemByNodeIdAsync(itemNodeId, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _controller.SaveItem(
            new SaveItemRequest { ZoneId = zoneId, ItemId = itemNodeId, ComponentName = "X", ZoneName = "Z", ExpectedVersionNumber = 4 },
            default);

        var objectResult = (ObjectResult)result;
        Assert.Multiple(() =>
        {
            Assert.That(objectResult.StatusCode, Is.EqualTo(409));
            Assert.That(ValueOf(objectResult.Value, "error"), Is.EqualTo("This item was changed by someone else since you opened it."));
        });
        await _service.DidNotReceive().UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>(), Arg.Any<int?>());
    }

    [Test]
    public async Task SaveItem_UpdateItem_PassesExpectedVersionToService()
    {
        var zoneId = Guid.NewGuid();
        var itemNodeId = Guid.NewGuid();
        var existing = Item(itemNodeId, zoneId);
        existing.Version = new ContentVersion { VersionNumber = 5, Node = new ContentNode { Id = itemNodeId } };
        _service.GetItemByNodeIdAsync(itemNodeId, Arg.Any<CancellationToken>()).Returns(existing);
        _service.UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>(), Arg.Is<int?>(v => v == 5)).Returns(true);
        _service.GetParentPageNodeForZoneAsync(zoneId, Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());

        var result = await _controller.SaveItem(
            new SaveItemRequest { ZoneId = zoneId, ItemId = itemNodeId, ComponentName = "X", ZoneName = "Z", ExpectedVersionNumber = 5 },
            default);

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            await _service.Received(1).UpdateItemAsync(
                Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>(), Arg.Is<int?>(v => v == 5));
        });
    }

    [Test]
    public async Task SaveItem_ByName_ExistingZone()
    {
        _service.GetZoneByNameAsync("Z", Arg.Any<CancellationToken>()).Returns(new ContentZoneDTO
        {
            Version = new ContentVersion { Node = new ContentNode { Id = Guid.NewGuid() } }
        });
        _service.AddItemAsync(Arg.Any<Guid>(), Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>())
            .Returns(c =>
            {
                var item = c.Arg<ContentZoneItemDTO>();
                item.Version.Node = new ContentNode { Id = Guid.NewGuid() };
                return item;
            });
        _service.GetParentPageNodeForZoneAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var result = await _controller.SaveItem(new SaveItemRequest { ComponentName = "C", ZoneName = "Z" }, default);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public async Task SaveItem_ByName_CreatesZone()
    {
        _service.GetZoneByNameAsync("Z", Arg.Any<CancellationToken>()).Returns((ContentZoneDTO?)null);
        _service.GetOrCreateByNameAsync("Z", Arg.Any<CancellationToken>()).Returns(new ContentZoneDTO
        {
            Version = new ContentVersion { Node = new ContentNode { Id = Guid.NewGuid() } }
        });
        _service.AddItemAsync(Arg.Any<Guid>(), Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>())
            .Returns(c =>
            {
                var item = c.Arg<ContentZoneItemDTO>();
                item.Version.Node = new ContentNode { Id = Guid.NewGuid() };
                return item;
            });
        _service.GetParentPageNodeForZoneAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var result = await _controller.SaveItem(new SaveItemRequest { ComponentName = "C", ZoneName = "Z" }, default);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public async Task SaveItem_ExceptionReturns500()
    {
        _service.GetZoneByNameAsync("Z", Arg.Any<CancellationToken>()).Returns<ContentZoneDTO?>(_ => throw new InvalidOperationException("boom"));

        var result = await _controller.SaveItem(new SaveItemRequest { ComponentName = "C", ZoneName = "Z" }, default) as ObjectResult;

        Assert.That(result!.StatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task SaveItem_UnresolvedZone_ReturnsBadRequest()
    {
        var pageNodeId = Guid.NewGuid();
        var zone = new ContentZoneDTO { Version = new ContentVersion { Node = new ContentNode { Id = Guid.Empty } } };
        _service.GetOrCreateByPageSlotAsync(pageNodeId, "Main", Arg.Any<CancellationToken>()).Returns((zone, new ContentZoneAssignmentDTO()));

        var result = await _controller.SaveItem(new SaveItemRequest { ComponentName = "C", ZoneName = "Z", ParentPageNodeId = pageNodeId, SlotName = "Main" }, default);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task DeleteItem_Success_NotFound_Exception()
    {
        _service.RemoveItemAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true, false);
        Assert.That(await _controller.DeleteItem(Guid.NewGuid(), default), Is.InstanceOf<OkObjectResult>());
        Assert.That(await _controller.DeleteItem(Guid.NewGuid(), default), Is.InstanceOf<NotFoundObjectResult>());

        _service.RemoveItemAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns<bool>(_ => throw new InvalidOperationException());
        Assert.That(((ObjectResult)await _controller.DeleteItem(Guid.NewGuid(), default)).StatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task MoveItem_ValidatesRequest()
    {
        Assert.Multiple(async () =>
        {
            Assert.That(await _controller.MoveItem(Guid.NewGuid(), null!, default), Is.InstanceOf<BadRequestObjectResult>());
            Assert.That(await _controller.MoveItem(Guid.NewGuid(), new MoveItemRequest(), default), Is.InstanceOf<BadRequestObjectResult>());
        });
        await _service.DidNotReceive().MoveItemAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MoveItem_ItemNotFound_Returns404()
    {
        _service.MoveItemAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((false, IContentZoneService.ItemNotFoundMessage));

        var result = await _controller.MoveItem(Guid.NewGuid(), new MoveItemRequest { TargetZoneId = Guid.NewGuid() }, default);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task MoveItem_PassesExpectedVersionToService_AndSurfacesStaleMessage()
    {
        var itemNodeId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();
        const string staleMessage = "This item was changed by someone else since you opened it.";

        _service.MoveItemAsync(itemNodeId, zoneId, 3, Arg.Any<CancellationToken>(), Arg.Is<int?>(v => v == 7))
            .Returns((false, staleMessage), (true, (string?)null));

        var request = new MoveItemRequest { TargetZoneId = zoneId, Position = 3, ExpectedVersionNumber = 7 };
        var stale = await _controller.MoveItem(itemNodeId, request, default);
        var ok = await _controller.MoveItem(itemNodeId, request, default);

        Assert.Multiple(() =>
        {
            var staleResult = (ObjectResult)stale;
            Assert.That(staleResult.StatusCode, Is.EqualTo(409));
            Assert.That(ValueOf(staleResult.Value, "error"), Is.EqualTo(staleMessage));
            Assert.That(ok, Is.InstanceOf<OkObjectResult>());
        });
        await _service.Received(2).MoveItemAsync(itemNodeId, zoneId, 3, Arg.Any<CancellationToken>(), Arg.Is<int?>(v => v == 7));
    }

    [Test]
    public async Task MoveItem_Success_Rejected_Exception()
    {
        var itemNodeId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();
        var request = new MoveItemRequest { TargetZoneId = zoneId, Position = 3 };
        _service.MoveItemAsync(itemNodeId, zoneId, 3, Arg.Any<CancellationToken>())
            .Returns((true, (string?)null), (false, "nope"), (false, (string?)null));

        var ok = await _controller.MoveItem(itemNodeId, request, default);
        var rejected = (BadRequestObjectResult)await _controller.MoveItem(itemNodeId, request, default);
        var rejectedNoMessage = (BadRequestObjectResult)await _controller.MoveItem(itemNodeId, request, default);

        _service.MoveItemAsync(itemNodeId, zoneId, 3, Arg.Any<CancellationToken>())
            .Returns<(bool, string?)>(_ => throw new InvalidOperationException());
        var failed = (ObjectResult)await _controller.MoveItem(itemNodeId, request, default);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.InstanceOf<OkObjectResult>());
            Assert.That(ValueOf(rejected.Value, "error"), Is.EqualTo("nope"));
            Assert.That(ValueOf(rejectedNoMessage.Value, "error"), Is.EqualTo("Failed to move item."));
            Assert.That(failed.StatusCode, Is.EqualTo(500));
        });
    }

    [Test]
    public async Task GetItem_FoundAndNotFound()
    {
        var nodeId = Guid.NewGuid();
        var item = new ContentZoneItemDTO
        {
            Version = new ContentVersion { Node = new ContentNode { Id = nodeId } },
            ContentZoneNodeId = Guid.NewGuid(),
            ComponentName = "C"
        };
        _service.GetItemByNodeIdAsync(nodeId, Arg.Any<CancellationToken>()).Returns(item);
        _service.GetItemByNodeIdAsync(Arg.Is<Guid>(g => g != nodeId), Arg.Any<CancellationToken>()).Returns((ContentZoneItemDTO?)null);

        Assert.Multiple(async () =>
        {
            Assert.That(await _controller.GetItem(nodeId, default), Is.InstanceOf<OkObjectResult>());
            Assert.That(await _controller.GetItem(Guid.NewGuid(), default), Is.InstanceOf<NotFoundObjectResult>());
        });
    }

    [Test]
    public async Task SaveItem_NewItem_GlobalZone_RouteRegistrationFails_Returns500()
    {
        var zoneId = Guid.NewGuid();
        var itemNodeId = Guid.NewGuid();

        _service.AddItemAsync(zoneId, Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>())
            .Returns(Item(itemNodeId, zoneId));
        _service.GetParentPageNodeForZoneAsync(zoneId, Arg.Any<CancellationToken>()).Returns((Guid?)null);
        _routeRegistration.TryRegisterWidgetRoutesAsync("X", itemNodeId, null, true, Arg.Any<CancellationToken>())
            .Returns((false, "boom"));

        var result = await _controller.SaveItem(new SaveItemRequest { ZoneId = zoneId, ComponentName = "X", ZoneName = "Z" }, default);

        var objectResult = (ObjectResult)result;
        Assert.Multiple(() =>
        {
            Assert.That(objectResult.StatusCode, Is.EqualTo(500));
            Assert.That(ValueOf(objectResult.Value, "error"), Is.EqualTo("boom"));
        });
    }

    [Test]
    public async Task SaveItem_UpdateItem_GlobalZone_RouteRegistrationFails_Returns500WithDefaultMessage()
    {
        var zoneId = Guid.NewGuid();
        var itemNodeId = Guid.NewGuid();

        _service.UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>()).Returns(true);
        _service.GetItemByNodeIdAsync(itemNodeId, Arg.Any<CancellationToken>()).Returns(Item(itemNodeId, zoneId));
        _service.GetParentPageNodeForZoneAsync(zoneId, Arg.Any<CancellationToken>()).Returns((Guid?)null);
        _routeRegistration.TryRegisterWidgetRoutesAsync("X", itemNodeId, null, true, Arg.Any<CancellationToken>())
            .Returns((false, null));

        var result = await _controller.SaveItem(new SaveItemRequest { ZoneId = zoneId, ItemId = itemNodeId, ComponentName = "X", ZoneName = "Z" }, default);

        var objectResult = (ObjectResult)result;
        Assert.Multiple(() =>
        {
            Assert.That(objectResult.StatusCode, Is.EqualTo(500));
            Assert.That(ValueOf(objectResult.Value, "error"), Is.EqualTo("Failed to register widget route."));
        });
    }

    [Test]
    public async Task SaveItem_PageZone_NewAndUpdate_DefersRouteRegistrationToPublish()
    {
        var zoneId = Guid.NewGuid();
        var itemNodeId = Guid.NewGuid();
        var pageId = Guid.NewGuid();

        _service.AddItemAsync(zoneId, Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>())
            .Returns(Item(itemNodeId, zoneId));
        _service.UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>()).Returns(true);
        _service.GetItemByNodeIdAsync(itemNodeId, Arg.Any<CancellationToken>()).Returns(Item(itemNodeId, zoneId));
        _service.GetParentPageNodeForZoneAsync(zoneId, Arg.Any<CancellationToken>()).Returns(pageId);

        // Page known from the request (new item) and resolved from the zone tree (update).
        var created = await _controller.SaveItem(new SaveItemRequest { ZoneId = zoneId, ParentPageNodeId = pageId, ComponentName = "X", ZoneName = "Z" }, default);
        var updated = await _controller.SaveItem(new SaveItemRequest { ZoneId = zoneId, ItemId = itemNodeId, ComponentName = "X", ZoneName = "Z" }, default);

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.InstanceOf<OkObjectResult>());
            Assert.That(updated, Is.InstanceOf<OkObjectResult>());
        });
        await _routeRegistration.DidNotReceive().TryRegisterWidgetRoutesAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateItemMetadata_NotFound_Returns404()
    {
        var result = await _controller.UpdateItemMetadata(Guid.NewGuid(), new UpdateItemMetadataRequest(), default);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task UpdateItemMetadata_StaleVersion_ReturnsConflictWithoutSaving()
    {
        var itemNodeId = Guid.NewGuid();
        var existing = Item(itemNodeId, Guid.NewGuid());
        existing.Version = new ContentVersion { VersionNumber = 5, Node = new ContentNode { Id = itemNodeId } };
        _service.GetItemByNodeIdAsync(itemNodeId, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _controller.UpdateItemMetadata(itemNodeId, new UpdateItemMetadataRequest
        {
            MetadataJson = "{}",
            ExpectedVersionNumber = 4
        }, default);

        var objectResult = (ObjectResult)result;
        Assert.Multiple(() =>
        {
            Assert.That(objectResult.StatusCode, Is.EqualTo(409));
            Assert.That(ValueOf(objectResult.Value, "error"), Is.EqualTo("This item was changed by someone else since you opened it."));
        });
        await _service.DidNotReceive().UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>(), Arg.Any<int?>());
    }

    [Test]
    public async Task UpdateItemMetadata_SavesWithExpectedVersion()
    {
        var itemNodeId = Guid.NewGuid();
        var existing = Item(itemNodeId, Guid.NewGuid());
        existing.Version = new ContentVersion { VersionNumber = 5, Node = new ContentNode { Id = itemNodeId } };
        _service.GetItemByNodeIdAsync(itemNodeId, Arg.Any<CancellationToken>()).Returns(existing);
        _service.UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>(), Arg.Is<int?>(v => v == 5)).Returns(true);

        var result = await _controller.UpdateItemMetadata(itemNodeId, new UpdateItemMetadataRequest
        {
            MetadataJson = "{}",
            ExpectedVersionNumber = 5
        }, default);

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            await _service.Received(1).UpdateItemAsync(
                Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>(), Arg.Is<int?>(v => v == 5));
        });
    }

    [Test]
    public async Task UpdateItemMetadata_BlankBody_KeepsStoredListForWidgetWithoutListSupport()
    {
        // A widget that lost list support hides the display-mode field, so its dialog can save an
        // empty document; that must not downgrade the grandfathered List to Single.
        var itemNodeId = Guid.NewGuid();
        var existing = Item(itemNodeId, Guid.NewGuid()) with { MetadataJson = "{\"DisplayMode\":\"List\"}" };
        _service.GetItemByNodeIdAsync(itemNodeId, Arg.Any<CancellationToken>()).Returns(existing);
        ContentZoneItemDTO? saved = null;
        _service.UpdateItemAsync(Arg.Do<ContentZoneItemDTO>(i => saved = i), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns(true);

        var result = await _controller.UpdateItemMetadata(itemNodeId, new UpdateItemMetadataRequest { MetadataJson = " " }, default);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            Assert.That(WidgetMetadataJson.StoresList(saved!.MetadataJson), Is.True);
        });
    }

    [Test]
    public async Task UpdateItemMetadata_InvalidJson_ReturnsBadRequestWithoutSaving()
    {
        var itemNodeId = Guid.NewGuid();
        _service.GetItemByNodeIdAsync(itemNodeId, Arg.Any<CancellationToken>()).Returns(Item(itemNodeId, Guid.NewGuid()));

        var result = await _controller.UpdateItemMetadata(itemNodeId, new UpdateItemMetadataRequest { MetadataJson = "[1]" }, default);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        await _service.DidNotReceive().UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>(), Arg.Any<int?>());
    }

    [Test]
    public async Task UpdateItemMetadata_ConcurrentWriteAfterPreCheck_ReturnsConflict()
    {
        // Both requests pass the pre-check at version 5; the store's own check rejects the loser.
        var itemNodeId = Guid.NewGuid();
        var existing = Item(itemNodeId, Guid.NewGuid());
        existing.Version = new ContentVersion { VersionNumber = 5, Node = new ContentNode { Id = itemNodeId } };
        _service.GetItemByNodeIdAsync(itemNodeId, Arg.Any<CancellationToken>()).Returns(existing);
        _service.UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>(), Arg.Any<int?>()).Returns(false);

        var result = (ObjectResult)await _controller.UpdateItemMetadata(itemNodeId, new UpdateItemMetadataRequest
        {
            MetadataJson = "{}",
            ExpectedVersionNumber = 5
        }, default);

        Assert.Multiple(() =>
        {
            Assert.That(result.StatusCode, Is.EqualTo(409));
            Assert.That(ValueOf(result.Value, "error"), Is.EqualTo("This item was changed by someone else since you opened it."));
        });
    }

    [Test]
    public async Task UpdateItemMetadata_UpdateFailsWithoutExpectedVersion_ReturnsNotFound()
    {
        var itemNodeId = Guid.NewGuid();
        _service.GetItemByNodeIdAsync(itemNodeId, Arg.Any<CancellationToken>()).Returns(Item(itemNodeId, Guid.NewGuid()));
        _service.UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>(), Arg.Any<int?>()).Returns(false);

        var result = await _controller.UpdateItemMetadata(itemNodeId, new UpdateItemMetadataRequest { MetadataJson = "{}" }, default);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task SaveItem_UpdateItem_ConcurrentWriteAfterPreCheck_ReturnsConflict()
    {
        var zoneId = Guid.NewGuid();
        var itemNodeId = Guid.NewGuid();
        var existing = Item(itemNodeId, zoneId);
        existing.Version = new ContentVersion { VersionNumber = 5, Node = new ContentNode { Id = itemNodeId } };
        _service.GetItemByNodeIdAsync(itemNodeId, Arg.Any<CancellationToken>()).Returns(existing);
        _service.UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>(), Arg.Any<int?>()).Returns(false);

        var result = (ObjectResult)await _controller.SaveItem(
            new SaveItemRequest { ZoneId = zoneId, ItemId = itemNodeId, ComponentName = "X", ZoneName = "Z", ExpectedVersionNumber = 5 },
            default);

        Assert.Multiple(() =>
        {
            Assert.That(result.StatusCode, Is.EqualTo(409));
            Assert.That(ValueOf(result.Value, "error"), Is.EqualTo("This item was changed by someone else since you opened it."));
        });
    }

    [Test]
    public async Task DeleteItem_GlobalZone_RouteRegistrationFails_Returns500()
    {
        var itemNodeId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();

        _service.GetItemByNodeIdAsync(itemNodeId, Arg.Any<CancellationToken>()).Returns(Item(itemNodeId, zoneId));
        _service.GetParentPageNodeForZoneAsync(zoneId, Arg.Any<CancellationToken>()).Returns((Guid?)null);
        _routeRegistration.TryRegisterWidgetRoutesAsync("X", itemNodeId, null, false, Arg.Any<CancellationToken>())
            .Returns((false, "boom"));

        var result = await _controller.DeleteItem(itemNodeId, default);

        var objectResult = (ObjectResult)result;
        Assert.Multiple(() =>
        {
            Assert.That(objectResult.StatusCode, Is.EqualTo(500));
            Assert.That(ValueOf(objectResult.Value, "error"), Is.EqualTo("boom"));
        });
        await _service.DidNotReceive().RemoveItemAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteItem_PageZone_DefersRouteRegistrationToPublish()
    {
        var itemNodeId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();

        _service.GetItemByNodeIdAsync(itemNodeId, Arg.Any<CancellationToken>()).Returns(Item(itemNodeId, zoneId));
        _service.GetParentPageNodeForZoneAsync(zoneId, Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());
        _service.RemoveItemAsync(itemNodeId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _controller.DeleteItem(itemNodeId, default);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        await _routeRegistration.DidNotReceive().TryRegisterWidgetRoutesAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SaveItem_RouteRegistrationSucceeds_ReturnsOkWithIds()
    {
        var zoneId = Guid.NewGuid();
        var itemNodeId = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        _service.AddItemAsync(zoneId, Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>())
            .Returns(Item(itemNodeId, zoneId));

        var result = await _controller.SaveItem(new SaveItemRequest
        {
            ZoneId = zoneId,
            ParentPageNodeId = parentId,
            ComponentName = "X",
            ZoneName = "Z"
        }, default);

        var ok = (OkObjectResult)result;
        Assert.Multiple(() =>
        {
            Assert.That(ValueOf(ok.Value, "itemId"), Is.EqualTo(itemNodeId));
            Assert.That(ValueOf(ok.Value, "zoneId"), Is.EqualTo(zoneId));
        });
    }

    [Test]
    public async Task DeleteItem_ItemMissing_SkipsRouteCall_ReturnsNotFound()
    {
        var itemNodeId = Guid.NewGuid();

        _service.GetItemByNodeIdAsync(itemNodeId, Arg.Any<CancellationToken>()).Returns((ContentZoneItemDTO?)null);
        _service.RemoveItemAsync(itemNodeId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _controller.DeleteItem(itemNodeId, default);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
        await _routeRegistration.DidNotReceive().TryRegisterWidgetRoutesAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }
}

[TestFixture]
public class GenericPageControllerTests
{
    private static T Build<T>() where T : Controller, new()
    {
        var controller = new T();
        var http = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private static PageDTO Page(string? viewName, string title = "T")
        => new()
        {
            ViewName = viewName,
            Version = new ContentVersion
            {
                Node = new ContentNode { Id = Guid.NewGuid() },
                Title = title
            }
        };

    [Test]
    public async Task GenericPage_Index_DefaultView_CustomView_AndConfigFallback()
    {
        var controller = Build<GenericPageController>();
        // No CMS:PageData / CMS:PageConfig -> default view + new config.
        var def = (ViewResult)await controller.Index();
        Assert.That(def.ViewName, Is.Null);
        Assert.That(def.Model, Is.InstanceOf<GenericPageConfiguration>());

        // With a page that specifies a view name and an existing config.
        controller.HttpContext.Items["CMS:PageData"] = Page("Custom");
        controller.HttpContext.Items["CMS:PageConfig"] = new GenericPageConfiguration { Style = "x" };
        var custom = (ViewResult)await controller.Index();
        Assert.That(custom.ViewName, Is.EqualTo("Custom"));

        // A page with the "Default" view name falls back to the default view.
        controller.HttpContext.Items["CMS:PageData"] = Page("Default");
        var defaultNamed = (ViewResult)await controller.Index();
        Assert.That(defaultNamed.ViewName, Is.Null);
    }

    [Test]
    public async Task GenericAdminPage_Index_DefaultAndCustomView()
    {
        var controller = Build<GenericAdminPageController>();
        Assert.That(((ViewResult)await controller.Index()).ViewName, Is.Null);

        controller.HttpContext.Items["CMS:PageData"] = Page("AdminView");
        Assert.That(((ViewResult)await controller.Index()).ViewName, Is.EqualTo("AdminView"));
    }
}

[TestFixture]
public class ErrorControllerTests
{
    private ErrorController Build()
    {
        var controller = new ErrorController();
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    [Test]
    public void Index_NoExceptionFeature_ReturnsErrorView()
    {
        var result = (ViewResult)Build().Index();
        Assert.That(result.ViewName, Is.EqualTo("Error"));
    }

    [Test]
    public void Index_WithExceptionFeature_LogsAndReturnsView()
    {
        var controller = Build();
        controller.HttpContext.Features.Set<IExceptionHandlerPathFeature>(
            new ExceptionHandlerFeature { Error = new InvalidOperationException("x"), Path = "/p" });

        Assert.That(((ViewResult)controller.Index()).ViewName, Is.EqualTo("Error"));
    }

    [Test]
    public void Index_FeatureWithoutError_SkipsLogging()
    {
        var controller = Build();
        controller.HttpContext.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature());

        Assert.That(((ViewResult)controller.Index()).ViewName, Is.EqualTo("Error"));
    }

    [Test]
    public void StatusCodeHandler_ReturnsErrorView()
    {
        Assert.That(((ViewResult)Build().StatusCodeHandler(404)).ViewName, Is.EqualTo("Error"));
    }

    [Test]
    public void Index_WithCurrentActivity_UsesActivityId()
    {
        using var activity = new System.Diagnostics.Activity("test");
        activity.Start();
        try
        {
            Assert.That(((ViewResult)Build().Index()).ViewName, Is.EqualTo("Error"));
            Assert.That(((ViewResult)Build().StatusCodeHandler(500)).ViewName, Is.EqualTo("Error"));
        }
        finally
        {
            activity.Stop();
        }
    }
}
