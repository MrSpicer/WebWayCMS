using Microsoft.AspNetCore.Mvc;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Controllers.Api;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Services;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class ContentZoneApiControllerTests
{
    private IContentZoneService _service = null!;
    private IContentZoneModel _model = null!;
    private IRouteRegistrationService _routeRegistration = null!;
    private IWidgetRegistry _widgetRegistry = null!;
    private IRelationBinder _relationBinder = null!;
    private ContentZoneApiController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _service = Substitute.For<IContentZoneService>();
        _routeRegistration = Substitute.For<IRouteRegistrationService>();
        _widgetRegistry = Substitute.For<IWidgetRegistry>();
        _widgetRegistry.GetByName("Lister").Returns(new WidgetRegistrationInfo { Name = "Lister", SupportsList = true });
        _relationBinder = Substitute.For<IRelationBinder>();
        // Item updates go through the model (which may move a container's nested content along); it
        // forwards to the service here so the update scenarios below exercise the controller's handling.
        _model = Substitute.For<IContentZoneModel>();
        _model.UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns(ci => _service.UpdateItemAsync(ci.ArgAt<ContentZoneItemDTO>(0), ci.ArgAt<CancellationToken>(1), ci.ArgAt<int?>(2)));
        _controller = new ContentZoneApiController(_service, _model, _routeRegistration, _widgetRegistry, _relationBinder);
        new MvcHarness().Configure(_controller, new[] { "Admin" });
        _routeRegistration.TryRegisterWidgetRoutesAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((true, null));
    }

    private static ContentZoneItemDTO Item(Guid itemNodeId, Guid zoneNodeId, bool active = true)
        => new ContentZoneItemDTO
        {
            ComponentName = "X",
            IsActive = active,
            Version = new ContentVersion { Node = new ContentNode { Id = itemNodeId } }
        }.In(zoneNodeId);

    private static object? ValueOf(object? obj, string prop)
        => obj?.GetType().GetProperty(prop)?.GetValue(obj);

    [Test]
    public void Constructor_Null_Throws()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new ContentZoneApiController(null!, _model, _routeRegistration, _widgetRegistry, _relationBinder), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneApiController(_service, null!, _routeRegistration, _widgetRegistry, _relationBinder), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneApiController(_service, _model, null!, _widgetRegistry, _relationBinder), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneApiController(_service, _model, _routeRegistration, null!, _relationBinder), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneApiController(_service, _model, _routeRegistration, _widgetRegistry, null!), Throws.ArgumentNullException);
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
        _service.GetOrCreateByPageSlotAsync(pageNodeId, "Main", Arg.Any<CancellationToken>()).Returns((zone, new OwnedZone()));
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

    /// <summary>A widget configuration with one config relation.</summary>
    public sealed class PickerConfig
    {
        [WebWayCMS.Attributes.ContentRelation("images")]
        public Guid? ImageNodeId { get; set; }
    }

    [Test]
    public async Task SaveItem_UpdateItem_ConfigRelationsRideOnTheItemPassedToTheUpdate()
    {
        var zoneId = Guid.NewGuid();
        var itemNodeId = Guid.NewGuid();
        var image = Guid.NewGuid();
        _widgetRegistry.GetByName("Picker").Returns(new WidgetRegistrationInfo
            { Name = "Picker", ConfigurationTypeName = typeof(PickerConfig).AssemblyQualifiedName });
        _service.GetItemByNodeIdAsync(itemNodeId, Arg.Any<CancellationToken>()).Returns(Item(itemNodeId, zoneId));
        ContentZoneItemDTO? passed = null;
        _service.UpdateItemAsync(Arg.Do<ContentZoneItemDTO>(i => passed = i), Arg.Any<CancellationToken>(), Arg.Any<int?>()).Returns(true);

        var result = await _controller.SaveItem(new SaveItemRequest
        {
            ZoneId = zoneId,
            ItemId = itemNodeId,
            ComponentName = "Picker",
            ZoneName = "Z",
            ComponentPropertiesJson = $"{{\"ImageNodeId\":\"{image}\"}}"
        }, default);

        // The service merges the passed item's relation writes onto the version it reloads; writes left
        // on another copy would be dropped and the picker change lost.
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            Assert.That(passed!.Version.TargetOf("config.ImageNodeId"), Is.EqualTo(image));
            Assert.That(passed.ComponentPropertiesJson, Does.Not.Contain("ImageNodeId"));
        });
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
    public async Task SaveItem_Create_InvalidConfiguration_ReturnsBadRequestWithoutSaving()
    {
        var zoneId = Guid.NewGuid();
        _widgetRegistry.ValidateConfiguration("Lister", "{\"maxItems\":-5}", WidgetDisplayMode.List)
            .Returns(new[] { "Max Items must be at least 1." });

        var result = await _controller.SaveItem(new SaveItemRequest
        {
            ComponentName = "Lister", ZoneName = "Z", ZoneId = zoneId,
            ComponentPropertiesJson = "{\"maxItems\":-5}", MetadataJson = "{\"DisplayMode\":\"List\"}"
        }, default);

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
            Assert.That(ValueOf(((BadRequestObjectResult)result).Value, "error"), Is.EqualTo("Max Items must be at least 1."));
            await _service.DidNotReceive().AddItemAsync(Arg.Any<Guid>(), Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task SaveItem_Update_InvalidConfiguration_ValidatesForTheStoredDisplayModeWithoutSaving()
    {
        var zoneId = Guid.NewGuid();
        var itemNodeId = Guid.NewGuid();
        var existing = Item(itemNodeId, zoneId);
        existing.ComponentName = "Lister";
        existing.MetadataJson = "{\"DisplayMode\":\"List\"}";
        _service.GetItemByNodeIdAsync(itemNodeId, Arg.Any<CancellationToken>()).Returns(existing);
        _widgetRegistry.ValidateConfiguration("Lister", "{}", WidgetDisplayMode.List)
            .Returns(new[] { "Heading is required." });

        var result = await _controller.SaveItem(new SaveItemRequest
        {
            ComponentName = "Lister", ZoneName = "Z", ZoneId = zoneId, ItemId = itemNodeId, ComponentPropertiesJson = null
        }, default);

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
            await _model.DidNotReceive().UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>(), Arg.Any<int?>());
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
        _service.GetOrCreateByPageSlotAsync(pageNodeId, "Main", Arg.Any<CancellationToken>()).Returns((zone, new OwnedZone()));

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
            ComponentName = "C"
        }.In(Guid.NewGuid());
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
        _routeRegistration.TryRegisterWidgetRoutesAsync("X", itemNodeId, null, true, null, Arg.Any<CancellationToken>())
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
        _routeRegistration.TryRegisterWidgetRoutesAsync("X", itemNodeId, null, true, null, Arg.Any<CancellationToken>())
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
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
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
    public async Task ItemUpdates_GoThroughTheModel_SoAContainerViewChangeMovesItsContent()
    {
        var zoneId = Guid.NewGuid();
        var itemNodeId = Guid.NewGuid();
        var existing = Item(itemNodeId, zoneId);
        existing.Version = new ContentVersion { VersionNumber = 5, Node = new ContentNode { Id = itemNodeId } };
        _service.GetItemByNodeIdAsync(itemNodeId, Arg.Any<CancellationToken>()).Returns(existing);
        _service.GetParentPageNodeForZoneAsync(zoneId, Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());
        _model.UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>(), Arg.Any<int?>()).Returns(true);

        await _controller.SaveItem(
            new SaveItemRequest { ZoneId = zoneId, ItemId = itemNodeId, ComponentName = "X", ZoneName = "Z", ExpectedVersionNumber = 5 }, default);
        await _controller.UpdateItemMetadata(itemNodeId, new UpdateItemMetadataRequest { MetadataJson = "{}", ExpectedVersionNumber = 5 }, default);

        await _model.Received(2).UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>(), Arg.Is<int?>(v => v == 5));
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
        _routeRegistration.TryRegisterWidgetRoutesAsync("X", itemNodeId, null, false, null, Arg.Any<CancellationToken>())
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
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
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
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }
}
