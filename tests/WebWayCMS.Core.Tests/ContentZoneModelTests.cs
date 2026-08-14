using System.Reflection;
using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Forms;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Services;

namespace WebWayCMS.Core.Tests;

public sealed class SampleZoneConfig
{
    public int X { get; set; }
}

[TestFixture]
public class ContentZoneModelTests
{
    private IContentZoneService _service = null!;
    private IContentStore<ContentZoneDTO> _zoneStore = null!;
    private IContentStore<ContentZoneItemDTO> _itemStore = null!;
    private IWidgetRegistry _registry = null!;
    private IViewDiscoveryService _viewDiscovery = null!;
    private ICMSRouteService _routeService = null!;
    private IRouteRegistrationService _routeRegistration = null!;
    private IChangeSetScope _changeSetScope = null!;
    private ContentZoneModel _model = null!;

    [SetUp]
    public void SetUp()
    {
        _service = Substitute.For<IContentZoneService>();
        _zoneStore = Substitute.For<IContentStore<ContentZoneDTO>>();
        _itemStore = Substitute.For<IContentStore<ContentZoneItemDTO>>();
        _routeService = Substitute.For<ICMSRouteService>();
        _routeRegistration = Substitute.For<IRouteRegistrationService>();
        _registry = Substitute.For<IWidgetRegistry>();
        _viewDiscovery = Substitute.For<IViewDiscoveryService>();
        _changeSetScope = Substitute.For<IChangeSetScope>();
        _model = new ContentZoneModel(_service, _zoneStore, _itemStore, _registry, _viewDiscovery, _routeService, _routeRegistration, _changeSetScope);

        _service.GetItemsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new List<ContentZoneItemDTO>());
    }

    private static ContentZoneDTO Zone(Guid? nodeId = null, string name = "Zone")
    {
        var nid = nodeId ?? Guid.NewGuid();
        return new ContentZoneDTO
        {
            VersionId = Guid.NewGuid(),
            Name = name,
            Version = new ContentVersion
            {
                Node = new ContentNode { Id = nid, CreatedUtc = DateTime.UtcNow },
                Title = name,
                Slug = "z",
                VersionNumber = 0,
                State = ContentVersionState.Draft
            }
        };
    }

    private static ContentZoneItemDTO Item(string component, string json, int ordinal = 0)
    {
        return new ContentZoneItemDTO
        {
            VersionId = Guid.NewGuid(),
            ContentZoneNodeId = Guid.NewGuid(),
            ComponentName = component,
            ComponentPropertiesJson = json,
            Ordinal = ordinal,
            IsActive = true,
            Version = new ContentVersion
            {
                Node = new ContentNode { Id = Guid.NewGuid(), CreatedUtc = DateTime.UtcNow },
                Title = component,
                VersionNumber = 0,
                State = ContentVersionState.Draft
            }
        };
    }

    private static ViewDataDictionary NewViewData() => new(new EmptyModelMetadataProvider(), new ModelStateDictionary());

    [Test]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new ContentZoneModel(null!, _zoneStore, _itemStore, _registry, _viewDiscovery, _routeService, _routeRegistration, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneModel(_service, null!, _itemStore, _registry, _viewDiscovery, _routeService, _routeRegistration, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneModel(_service, _zoneStore, null!, _registry, _viewDiscovery, _routeService, _routeRegistration, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneModel(_service, _zoneStore, _itemStore, null!, _viewDiscovery, _routeService, _routeRegistration, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneModel(_service, _zoneStore, _itemStore, _registry, null!, _routeService, _routeRegistration, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneModel(_service, _zoneStore, _itemStore, _registry, _viewDiscovery, null!, _routeRegistration, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneModel(_service, _zoneStore, _itemStore, _registry, _viewDiscovery, _routeService, null!, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneModel(_service, _zoneStore, _itemStore, _registry, _viewDiscovery, _routeService, _routeRegistration, null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void Metadata()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_model.ContentType, Is.EqualTo("contentzones"));
            Assert.That(_model.DisplayName, Is.EqualTo("Content Zone"));
            Assert.That(_model.IndexViewPath, Does.Contain("ContentZones.cshtml"));
            Assert.That(_model.UpsertViewPath, Does.Contain("ContentZoneUpsert.cshtml"));
            Assert.That(_model.WriteRoles, Is.Null);
            Assert.That(_model.HasSecondaryApiList, Is.False);
            Assert.That(_model.RegistryHandler, Is.Not.Null);
            Assert.That(_model.ChildHandler, Is.Not.Null);
        });
    }

    [Test]
    public async Task GetViewModelAsync_WhitespaceNullExistingAndMissing()
    {
        Assert.That(await _model.GetViewModelAsync(" "), Is.Null);

        _service.GetZoneByNameAsync("Missing", Arg.Any<CancellationToken>()).Returns((ContentZoneDTO?)null);
        Assert.That((await _model.GetViewModelAsync("Missing"))!.Name, Is.EqualTo("Missing"));

        _service.GetZoneByNameAsync("Found", Arg.Any<CancellationToken>()).Returns(Zone(name: "Found"));
        Assert.That(await _model.GetViewModelAsync("Found"), Is.Not.Null);
    }

    [Test]
    public async Task DeserializeProperties_AllBranches()
    {
        var zoneId = Guid.NewGuid();
        var zone = Zone(zoneId, "Z");
        var items = new List<ContentZoneItemDTO>
        {
            Item("WithDefault", "{}", 1),
            Item("NoDefault", "", 2),
            Item("Typed", "{\"x\":5}", 3),
            Item("Untyped", "{\"a\":1}", 4),
            Item("Bad", "{bad", 5),
            Item("TypedNull", "null", 6)
        };
        _service.GetZoneByNodeAsync(zoneId, Arg.Any<CancellationToken>()).Returns(zone);
        _service.GetItemsAsync(zoneId, Arg.Any<CancellationToken>()).Returns(items);

        _registry.CreateDefaultConfiguration("WithDefault").Returns(new SampleZoneConfig());
        _registry.CreateDefaultConfiguration("NoDefault").Returns((object?)null);
        _registry.GetByName("Typed").Returns(new WidgetRegistrationInfo { Name = "Typed", ConfigurationTypeName = typeof(SampleZoneConfig).FullName });
        _registry.GetByName("Untyped").Returns((WidgetRegistrationInfo?)null);
        _registry.GetByName("Bad").Returns((WidgetRegistrationInfo?)null);
        _registry.GetByName("TypedNull").Returns(new WidgetRegistrationInfo { Name = "TypedNull", ConfigurationTypeName = typeof(SampleZoneConfig).FullName });

        var vm = await _model.GetViewModelByIdAsync(zoneId);

        Assert.That(vm!.ZoneObjects, Has.Count.EqualTo(6));
    }

    [Test]
    public async Task GetViewModelByIdAsync_NotFound_ReturnsNull()
    {
        _service.GetZoneByNodeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ContentZoneDTO?)null);
        Assert.That(await _model.GetViewModelByIdAsync(Guid.NewGuid()), Is.Null);
    }

    [Test]
    public async Task GetOrCreateAndSlotViewModels()
    {
        var zone = Zone();
        _service.GetOrCreateByNameAsync("G", Arg.Any<CancellationToken>()).Returns(zone);
        _service.GetOrCreateByPageSlotAsync(Arg.Any<Guid>(), "S", Arg.Any<CancellationToken>()).Returns((zone, new ContentZoneAssignmentDTO()));
        _service.GetOrCreateByZoneSlotAsync(Arg.Any<Guid>(), "S", Arg.Any<CancellationToken>()).Returns((zone, new ContentZoneAssignmentDTO()));

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetOrCreateViewModelAsync("G"), Is.Not.Null);
            Assert.That(await _model.GetOrCreateViewModelByPageSlotAsync(Guid.NewGuid(), "S"), Is.Not.Null);
            Assert.That(await _model.GetOrCreateViewModelByZoneSlotAsync(Guid.NewGuid(), "S"), Is.Not.Null);
        });
    }

    [Test]
    public async Task MapToViewModel_EmptyNodeId_ReturnsEmptyViewModel()
    {
        var zone = new ContentZoneDTO { Version = new ContentVersion { Node = new ContentNode { Id = Guid.Empty } } };
        _service.GetOrCreateByNameAsync("Empty", Arg.Any<CancellationToken>()).Returns(zone);

        var vm = await _model.GetOrCreateViewModelAsync("Empty");

        Assert.Multiple(() =>
        {
            Assert.That(vm.Id, Is.EqualTo(Guid.Empty));
            Assert.That(vm.ZoneObjects, Is.Empty);
        });
        await _service.DidNotReceive().GetItemsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetViewModelByPageSlot_NullAssignment_NullZone_AndFound()
    {
        var pageNodeId = Guid.NewGuid();
        _service.GetByPageSlotAsync(pageNodeId, "none", Arg.Any<CancellationToken>()).Returns((ContentZoneAssignmentDTO?)null);
        Assert.That(await _model.GetViewModelByPageSlotAsync(pageNodeId, "none"), Is.Null);

        var assignment = new ContentZoneAssignmentDTO { ContentZoneNodeId = Guid.NewGuid() };
        _service.GetByPageSlotAsync(pageNodeId, "slot", Arg.Any<CancellationToken>()).Returns(assignment);
        _service.GetZoneByNodeAsync(assignment.ContentZoneNodeId, Arg.Any<CancellationToken>()).Returns((ContentZoneDTO?)null, Zone());
        Assert.That(await _model.GetViewModelByPageSlotAsync(pageNodeId, "slot"), Is.Null);
        Assert.That(await _model.GetViewModelByPageSlotAsync(pageNodeId, "slot"), Is.Not.Null);
    }

    [Test]
    public async Task GetViewModelByZoneSlot_NullAssignment_AndFound()
    {
        var parent = Guid.NewGuid();
        _service.GetByZoneSlotAsync(parent, "none", Arg.Any<CancellationToken>()).Returns((ContentZoneAssignmentDTO?)null);
        Assert.That(await _model.GetViewModelByZoneSlotAsync(parent, "none"), Is.Null);

        var assignment = new ContentZoneAssignmentDTO { ContentZoneNodeId = Guid.NewGuid() };
        _service.GetByZoneSlotAsync(parent, "slot", Arg.Any<CancellationToken>()).Returns(assignment);
        _service.GetZoneByNodeAsync(assignment.ContentZoneNodeId, Arg.Any<CancellationToken>()).Returns(Zone());
        Assert.That(await _model.GetViewModelByZoneSlotAsync(parent, "slot"), Is.Not.Null);
    }

    [Test]
    public async Task PassthroughCrudAndItemOperations()
    {
        var zone = Zone();
        var item = Item("C", "{}");
        _zoneStore.GetCurrentDraftAsync(zone.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(zone);
        _service.AddItemAsync(zone.Version.Node.Id, Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>()).Returns(item);
        _service.UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>()).Returns(true);
        _service.RemoveItemAsync(item.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(true);
        _itemStore.GetCurrentDraftAsync(item.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(item);
        _itemStore.GetVersionAsync(item.VersionId, Arg.Any<CancellationToken>()).Returns(item);
        _itemStore.DeleteVersionAsync(item.VersionId, Arg.Any<CancellationToken>()).Returns(true);
        _service.ReorderItemsAsync(zone.Version.Node.Id, Arg.Any<List<Guid>>(), Arg.Any<CancellationToken>()).Returns(true);
        _itemStore.GetAllVersionsAsync(item.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(new List<ContentZoneItemDTO> { item });
        _service.GetItemsAsync(zone.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(new List<ContentZoneItemDTO> { item });
        _service.DeleteZoneAsync(zone.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(true);

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetByIdAsync(zone.Version.Node.Id), Is.SameAs(zone));
            Assert.That(await _model.AddItemAsync(zone.Version.Node.Id, item), Is.SameAs(item));
            Assert.That(await _model.UpdateItemAsync(item), Is.True);
            Assert.That(await _model.RemoveItemAsync(item.Version.Node.Id), Is.True);
            Assert.That(await _model.GetItemByNodeIdAsync(item.Version.Node.Id), Is.SameAs(item));
            Assert.That(await _model.GetItemVersionAsync(item.VersionId), Is.SameAs(item));
            Assert.That(await _model.DeleteItemVersionAsync(item.VersionId), Is.True);
            Assert.That(await _model.ReorderItemsAsync(zone.Version.Node.Id, new List<Guid>()), Is.True);
            Assert.That(await _model.GetAllItemVersionsAsync(item.Version.Node.Id), Has.Count.EqualTo(1));
            Assert.That(await _model.GetItemsAsync(zone.Version.Node.Id), Has.Count.EqualTo(1));
            Assert.That(await _model.DeleteAsync(zone.Version.Node.Id), Is.True);
        });
    }

    [Test]
    public async Task GetIndexViewModelAsync_Parameterless()
    {
        _zoneStore.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<ContentZoneDTO> { Zone() });
        _service.GetZoneNodeIdsWithChildrenAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>()).Returns(new HashSet<Guid>());
        _service.GetAssignmentCountsByNodeIdAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, int>());

        Assert.That(await _model.GetIndexViewModelAsync(), Is.InstanceOf<ContentZoneIndexViewModel>());
    }

    [Test]
    public async Task GetIndexViewModelAsync_Query_PageZoneAndDefault()
    {
        _service.GetZoneNodeIdsWithChildrenAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>()).Returns(new HashSet<Guid>());
        _service.GetAssignmentCountsByNodeIdAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, int>());
        _zoneStore.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<ContentZoneDTO>());
        var pageId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();
        _service.GetAllByPageAsync(pageId, Arg.Any<CancellationToken>()).Returns(new List<ContentZoneDTO>());
        _routeService.GetByOwningContentAsync(pageId, Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO> { new() { Pattern = "/r" } });
        _service.GetAllByParentZoneAsync(zoneId, Arg.Any<CancellationToken>()).Returns(new List<ContentZoneDTO>());
        _service.GetZoneByNodeAsync(zoneId, Arg.Any<CancellationToken>()).Returns(new ContentZoneDTO { Name = "Parent" });

        var handler = (IAdminCrudHandler)_model;
        var pageQuery = new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["pageId"] = pageId.ToString() });
        var zoneQuery = new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["zoneId"] = zoneId.ToString() });
        var emptyQuery = new QueryCollection();

        Assert.Multiple(async () =>
        {
            Assert.That(((ContentZoneIndexViewModel)await handler.GetIndexViewModelAsync(pageQuery, default)).FilterPageRoute, Is.EqualTo("/r"));
            Assert.That(((ContentZoneIndexViewModel)await handler.GetIndexViewModelAsync(zoneQuery, default)).FilterParentZoneName, Is.EqualTo("Parent"));
            Assert.That(await handler.GetIndexViewModelAsync(emptyQuery, default), Is.InstanceOf<ContentZoneIndexViewModel>());
        });
    }

    [Test]
    public async Task GetUpsertViewModelAsync_NullFoundAndMissing()
    {
        var zone = Zone();
        _zoneStore.GetCurrentDraftAsync(zone.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(zone);
        _zoneStore.GetCurrentDraftAsync(Arg.Is<Guid>(g => g != zone.Version.Node.Id), Arg.Any<CancellationToken>()).Returns((ContentZoneDTO?)null);
        var q = new QueryCollection();

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetUpsertViewModelAsync(null, q), Is.InstanceOf<ContentZoneUpsertViewModel>());
            Assert.That(await _model.GetUpsertViewModelAsync(zone.Version.Node.Id, q), Is.Not.Null);
            Assert.That(await _model.GetUpsertViewModelAsync(Guid.NewGuid(), q), Is.Null);
        });
    }

    [Test]
    public async Task SaveUpsertAsync_CreateEditUpdateFailAndNotFound()
    {
        var zone = Zone();
        _zoneStore.GetCurrentDraftAsync(zone.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(zone);
        _zoneStore.SaveDraftAsync(Arg.Any<ContentZoneDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true), new ContentWriteResult(true), new ContentWriteResult(false, "err"));

        Assert.Multiple(async () =>
        {
            Assert.That((await _model.SaveUpsertAsync(new ContentZoneUpsertViewModel { NodeId = null, Name = "N", Title = "T" })).Success, Is.True);
            Assert.That((await _model.SaveUpsertAsync(new ContentZoneUpsertViewModel { NodeId = zone.Version.Node.Id, Name = "N", Title = "T" })).Success, Is.True);
            Assert.That((await _model.SaveUpsertAsync(new ContentZoneUpsertViewModel { NodeId = zone.Version.Node.Id, Name = "N", Title = "T" })).Success, Is.False);
        });

        _zoneStore.GetCurrentDraftAsync(Arg.Is<Guid>(g => g != zone.Version.Node.Id), Arg.Any<CancellationToken>()).Returns((ContentZoneDTO?)null);
        Assert.That((await _model.SaveUpsertAsync(new ContentZoneUpsertViewModel { NodeId = Guid.NewGuid(), Name = "N", Title = "T" })).Success, Is.False);
    }

    [Test]
    public async Task ApiListSecondaryAndCreateEmpty()
    {
        _zoneStore.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<ContentZoneDTO>
        {
            Zone(name: ""),
            new ContentZoneDTO { Name = "n", Version = new ContentVersion { Node = new ContentNode { Id = Guid.NewGuid() }, Title = "HasTitle" } }
        });

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetApiListAsync(), Is.Not.Empty);
            Assert.That(await _model.GetSecondaryApiListAsync("x"), Is.Empty);
            Assert.That(_model.CreateEmptyUpsertViewModel(), Is.InstanceOf<ContentZoneUpsertViewModel>());
        });
    }

    [Test]
    public async Task VersionHistory_BuildAndDeleteVersion()
    {
        var nodeId = Guid.NewGuid();
        _zoneStore.GetAllVersionsAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new List<ContentZoneDTO> { Zone(nodeId) });
        _zoneStore.DeleteVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetVersionHistoryViewModelAsync(nodeId), Is.Not.Null);
            Assert.That(await _model.DeleteVersionAsync(Guid.NewGuid()), Is.True);
        });
    }

    [Test]
    public async Task RestoreVersion_HistoricalFoundAndMissing()
    {
        _zoneStore.GetVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ContentZoneDTO?)null);
        Assert.That(await _model.GetRestoreVersionViewModelAsync(Guid.NewGuid()), Is.Null);

        var zone = Zone();
        _zoneStore.GetVersionAsync(zone.VersionId, Arg.Any<CancellationToken>()).Returns(zone);
        _zoneStore.GetCurrentDraftAsync(zone.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(zone);
        Assert.That(await _model.GetRestoreVersionViewModelAsync(zone.VersionId), Is.InstanceOf<ContentZoneUpsertViewModel>());
    }

    [Test]
    public async Task PublishAsync_DelegatesToStore()
    {
        _zoneStore.PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));

        Assert.That((await _model.PublishAsync(Guid.NewGuid())).Success, Is.True);
    }

    // --- Child handler ---

    [Test]
    public async Task ChildHandler_Metadata()
    {
        var child = _model.ChildHandler!;

        Assert.Multiple(() =>
        {
            Assert.That(child.ChildType, Is.EqualTo("items"));
            Assert.That(child.ChildDisplayName, Is.EqualTo("Content Zone Item"));
            Assert.That(child.WriteRoles, Is.Null);
            Assert.That(child.ChildIndexViewPath, Does.Contain("ContentZoneItems.cshtml"));
            Assert.That(child.ChildUpsertViewPath, Does.Contain("ContentZoneItemUpsert.cshtml"));
            Assert.That(child.SupportsReorder, Is.False);
            Assert.That(child.SupportsVersionHistory, Is.True);
            Assert.That(child.CreateEmptyChildUpsertViewModel(), Is.InstanceOf<ContentZoneItemUpsertViewModel>());
        });
    }

    [Test]
    public async Task ChildHandler_GetChildIndex_Variants()
    {
        var child = _model.ChildHandler!;
        var zone = Zone();
        var item = Item("C", "{}");
        _zoneStore.GetCurrentDraftAsync(zone.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(zone);
        _service.GetItemsAsync(zone.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(new List<ContentZoneItemDTO> { item });

        Assert.Multiple(async () =>
        {
            Assert.That(await child.GetChildIndexViewModelAsync("not-a-guid"), Is.Null);

            var vm = await child.GetChildIndexViewModelAsync(zone.Version.Node.Id.ToString());
            Assert.That(vm, Is.InstanceOf<ContentZoneItemsIndexViewModel>());
        });
    }

    [Test]
    public async Task ChildHandler_GetChildUpsert_Variants()
    {
        var child = _model.ChildHandler!;
        var item = Item("C", "{}");
        _itemStore.GetCurrentDraftAsync(item.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(item);

        Assert.Multiple(async () =>
        {
            Assert.That(await child.GetChildUpsertViewModelAsync("k", null), Is.Null);
            Assert.That(await child.GetChildUpsertViewModelAsync("k", item.Version.Node.Id), Is.Not.Null);
        });
    }

    [Test]
    public async Task ChildHandler_GetChildUpsert_MissingItem_ReturnsNull()
    {
        _itemStore.GetCurrentDraftAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ContentZoneItemDTO?)null);

        Assert.That(await _model.ChildHandler!.GetChildUpsertViewModelAsync("k", Guid.NewGuid()), Is.Null);
    }

    [Test]
    public async Task ChildHandler_SetViewData_Variants()
    {
        var child = _model.ChildHandler!;
        var zone = Zone();
        _zoneStore.GetCurrentDraftAsync(zone.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(zone);
        var viewData = NewViewData();

        await child.SetChildUpsertViewDataAsync(viewData, "not-a-guid");
        await child.SetChildUpsertViewDataAsync(viewData, zone.Version.Node.Id.ToString());

        Assert.That(viewData["ZoneId"], Is.EqualTo(zone.Version.Node.Id.ToString()));
    }

    [Test]
    public async Task ChildHandler_SaveChildUpsert_Variants()
    {
        var child = _model.ChildHandler!;
        var item = Item("C", "{}");
        _itemStore.GetCurrentDraftAsync(item.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(item);
        _service.UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>()).Returns(true, false);

        Assert.Multiple(async () =>
        {
            // no id and invalid parent key -> error
            Assert.That((await child.SaveChildUpsertAsync("not-a-guid", new ContentZoneItemUpsertViewModel { NodeId = null })).Success, Is.False);
            // id but item missing -> error
            Assert.That((await child.SaveChildUpsertAsync("k", new ContentZoneItemUpsertViewModel { NodeId = Guid.NewGuid() })).Success, Is.False);
            // update success then failure
            Assert.That((await child.SaveChildUpsertAsync("k", new ContentZoneItemUpsertViewModel { NodeId = item.Version.Node.Id, ComponentName = "C" })).Success, Is.True);
            Assert.That((await child.SaveChildUpsertAsync("k", new ContentZoneItemUpsertViewModel { NodeId = item.Version.Node.Id, ComponentName = "C" })).Success, Is.False);
        });
    }

    [Test]
    public async Task ChildHandler_SaveChildUpsert_CreatesNewItem_WhenNoId()
    {
        var child = _model.ChildHandler!;
        var zoneId = Guid.NewGuid();
        ContentZoneItemDTO? captured = null;
        _service.AddItemAsync(zoneId, Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>())
            .Returns(c =>
            {
                captured = c.Arg<ContentZoneItemDTO>();
                captured.Version.Node = new ContentNode { Id = Guid.NewGuid() };
                return captured;
            });
        _service.GetParentPageNodeForZoneAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var created = await child.SaveChildUpsertAsync(zoneId.ToString(),
            new ContentZoneItemUpsertViewModel { NodeId = null, ComponentName = "ContentBlock", ComponentPropertiesJson = "{\"ContentBlockID\":\"x\"}", IsActive = true });

        Assert.Multiple(() =>
        {
            Assert.That(created.Success, Is.True);
            Assert.That(captured!.ContentZoneNodeId, Is.EqualTo(zoneId));
            Assert.That(captured!.ComponentName, Is.EqualTo("ContentBlock"));
            Assert.That(captured!.ComponentPropertiesJson, Is.EqualTo("{\"ContentBlockID\":\"x\"}"));
            Assert.That(captured!.IsActive, Is.True);
        });

        var createdBlank = await child.SaveChildUpsertAsync(zoneId.ToString(),
            new ContentZoneItemUpsertViewModel { NodeId = null, ComponentName = "ContentBlock", ComponentPropertiesJson = "  " });

        Assert.Multiple(() =>
        {
            Assert.That(createdBlank.Success, Is.True);
            Assert.That(captured!.ComponentPropertiesJson, Is.EqualTo("{}"));
        });
    }

    [Test]
    public async Task ChildHandler_DeleteChild_And_VersionHistory_Restore_DeleteVersion()
    {
        var child = _model.ChildHandler!;
        var item = Item("C", "{}");
        _itemStore.GetCurrentDraftAsync(item.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(item);
        _service.RemoveItemAsync(item.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(true);
        _service.GetParentPageNodeForZoneAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Guid?)null);

        Assert.That(await child.DeleteChildAsync(item.Version.Node.Id), Is.True);

        _itemStore.GetAllVersionsAsync(item.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(new List<ContentZoneItemDTO> { item });
        Assert.That(await child.GetChildVersionHistoryViewModelAsync("k", item.Version.Node.Id), Is.Not.Null);

        _itemStore.GetVersionAsync(item.VersionId, Arg.Any<CancellationToken>()).Returns(item);
        Assert.That(await child.GetChildRestoreVersionViewModelAsync("k", item.VersionId), Is.Not.Null);

        _itemStore.DeleteVersionAsync(item.VersionId, Arg.Any<CancellationToken>()).Returns(true);
        Assert.That(await child.DeleteChildVersionAsync(item.VersionId), Is.True);

        Assert.That(await child.ReorderAsync("k", new List<Guid>()), Is.False);
    }

    // --- Registry handler ---

    [Test]
    public void RegistryHandler_GetAll_Json()
    {
        _registry.GetAllComponents().Returns(new List<WidgetRegistrationInfo>
        {
            new() { Name = "C", DisplayName = "C", Description = "d", Category = "General" }
        });

        Assert.That(_model.RegistryHandler!.GetAll(), Is.InstanceOf<JsonResult>());
    }

    [Test]
    public void RegistryHandler_GetProperties_EmptyNameAndNotFound()
    {
        _registry.GetByName("X").Returns((WidgetRegistrationInfo?)null);

        Assert.Multiple(() =>
        {
            Assert.That(_model.RegistryHandler!.GetProperties(" "), Is.InstanceOf<BadRequestObjectResult>());
            Assert.That(_model.RegistryHandler!.GetProperties("X"), Is.InstanceOf<NotFoundObjectResult>());
        });
    }

    [Test]
    public void RegistryHandler_GetProperties_ViewPickerWithAndWithoutViews_AndPlainProperty()
    {
        _registry.GetByName("C").Returns(new WidgetRegistrationInfo
        {
            Name = "C",
            DisplayName = "C",
            Category = "General",
            Properties = new List<FormPropertyInfo>
            {
                new() { Name = "WithViews", EditorType = EditorType.ViewPicker, ViewComponentName = "Has", Order = 1 },
                new() { Name = "NoViews", EditorType = EditorType.ViewPicker, ViewComponentName = "Empty", Order = 2 },
                new() { Name = "Plain", EditorType = EditorType.Text, Order = 3 },
            }
        });
        _viewDiscovery.GetAvailableViews("Has").Returns(new[] { "Default" });
        _viewDiscovery.GetAvailableViews("Empty").Returns(Array.Empty<string>());

        Assert.That(_model.RegistryHandler!.GetProperties("C"), Is.InstanceOf<JsonResult>());
    }

    [Test]
    public void RegistryHandler_GetForm_EmptyName_ReturnsBadRequest()
    {
        Assert.That(_model.RegistryHandler!.GetForm("  ", null), Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void RegistryHandler_GetForm_NotFound_ReturnsNotFound()
    {
        _registry.GetByName("X").Returns((WidgetRegistrationInfo?)null);

        Assert.That(_model.RegistryHandler!.GetForm("X", null), Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public void RegistryHandler_GetForm_NoConfigTypeName_ReturnsPartialViewWithNullModel()
    {
        _registry.GetByName("Plain").Returns(new WidgetRegistrationInfo
        {
            Name = "Plain",
            ConfigurationTypeName = null
        });

        var result = _model.RegistryHandler!.GetForm("Plain", null);

        Assert.That(result, Is.InstanceOf<PartialViewResult>());
        Assert.That(((PartialViewResult)result).ViewData!.Model, Is.Null);
    }

    [Test]
    public void RegistryHandler_GetForm_UnresolvableConfigTypeName_ReturnsPartialViewWithNullModel()
    {
        _registry.GetByName("Bad").Returns(new WidgetRegistrationInfo
        {
            Name = "Bad",
            ConfigurationTypeName = "NonExistent.Type.Name.ForTest"
        });

        var result = _model.RegistryHandler!.GetForm("Bad", null);

        Assert.That(result, Is.InstanceOf<PartialViewResult>());
        Assert.That(((PartialViewResult)result).ViewData!.Model, Is.Null);
    }

    [Test]
    public void RegistryHandler_GetForm_WithConfigTypeName_ReturnsPartialViewWithInstance()
    {
        _registry.GetByName("Typed").Returns(new WidgetRegistrationInfo
        {
            Name = "Typed",
            ConfigurationTypeName = typeof(SampleZoneConfig).FullName
        });

        var result = _model.RegistryHandler!.GetForm("Typed", null);

        Assert.That(result, Is.InstanceOf<PartialViewResult>());
        Assert.That(((PartialViewResult)result).ViewData!.Model, Is.TypeOf<SampleZoneConfig>());
    }

    [Test]
    public void RegistryHandler_GetForm_CoreAssemblyType_ResolvedDirectly()
    {
        _registry.GetByName("Core").Returns(new WidgetRegistrationInfo
        {
            Name = "Core",
            ConfigurationTypeName = typeof(WebWayCMS.Models.Page.PageTreeNode).FullName
        });

        var result = _model.RegistryHandler!.GetForm("Core", null);

        Assert.That(result, Is.InstanceOf<PartialViewResult>());
    }

    // ResolveType private method coverage via reflection

    [Test]
    public void ResolveType_NullOrWhitespace_ReturnsNull()
    {
        var method = typeof(ContentZoneModel).GetMethod("ResolveType",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.That(method, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(method.Invoke(null, new object?[] { null }), Is.Null);
            Assert.That(method.Invoke(null, new object?[] { "  " }), Is.Null);
            Assert.That(method.Invoke(null, new object?[] { "" }), Is.Null);
        });
    }

    [Test]
    public void ResolveType_TypeFoundByGetType_ReturnsType()
    {
        var method = typeof(ContentZoneModel).GetMethod("ResolveType",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var typeName = typeof(WebWayCMS.Models.Page.PageModel).FullName;
        Assert.That(typeName, Is.Not.Null);

        var result = method.Invoke(null, new object?[] { typeName });

        Assert.That(result, Is.EqualTo(typeof(WebWayCMS.Models.Page.PageModel)));
    }

    [Test]
    public void ResolveType_TypeNotFoundAnywhere_ReturnsNull()
    {
        var method = typeof(ContentZoneModel).GetMethod("ResolveType",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = method.Invoke(null, new object?[] { "NonExistent.Type.Name.ForTest" });

        Assert.That(result, Is.Null);
    }
}
