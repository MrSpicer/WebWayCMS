using System.Text.Json;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Forms;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Models.Shared;
using WebWayCMS.Services;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class ContentZoneConfigRelationTests
{
    private sealed class WidgetConfig
    {
        [ContentRelation("images")]
        public Guid? ImageId { get; set; }

        [FormProperty(EditorType = EditorType.Text)]
        public string? Other { get; set; }
    }

    private static readonly string ConfigTypeName = typeof(WidgetConfig).AssemblyQualifiedName!;

    private IContentZoneService _service = null!;
    private IContentStore<ContentZoneDTO> _zoneStore = null!;
    private IContentStore<ContentZoneItemDTO> _itemStore = null!;
    private IWidgetRegistry _registry = null!;
    private IViewDiscoveryService _viewDiscovery = null!;
    private ICMSRouteService _routeService = null!;
    private IRouteRegistrationService _routeRegistration = null!;
    private IChangeSetScope _changeSetScope = null!;
    private IPageHistoryService _pageHistory = null!;
    private IWidgetSlotDiscovery _slotDiscovery = null!;
    private IRelationBinder _binder = null!;
    private ContentZoneModel _model = null!;

    /// <summary>The zone item handler over this fixture's model (the old zone child handler's home).</summary>
    private ContentZoneItemModel Items => new(_model);

    private static readonly Microsoft.AspNetCore.Http.IQueryCollection NoQuery = new Microsoft.AspNetCore.Http.QueryCollection();

    /// <summary>Creates an item in <paramref name="zoneId"/> (stubbed as an existing zone) through the item handler.</summary>
    private Task<AdminSaveResult> CreateInZoneAsync(Guid zoneId, ContentZoneItemUpsertViewModel vm)
    {
        _zoneStore.GetCurrentDraftAsync(zoneId, Arg.Any<CancellationToken>())
            .Returns(new ContentZoneDTO { Version = new ContentVersion { Node = new ContentNode { Id = zoneId } } });
        RelationProperties.Write(vm, [new RelationValue(nameof(ContentZoneItemUpsertViewModel.ContentZoneNodeId), zoneId, 0, string.Empty)]);
        return Items.SaveUpsertAsync(vm);
    }

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
        _pageHistory = Substitute.For<IPageHistoryService>();
        _slotDiscovery = Substitute.For<IWidgetSlotDiscovery>();
        _binder = Substitute.For<IRelationBinder>();
        _binder.ValidateAsync(Arg.Any<object>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((AdminSaveResult?)null);

        _pageHistory.GetSnapshotsReferencingVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<PageSetSnapshot>());
        _service.GetOwnedSlotsWithItemsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        _service.GetDescendantItemNodeIdsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<Guid>());
        _registry.GetByName("Widget").Returns(new WidgetRegistrationInfo
        {
            Name = "Widget",
            DisplayName = "Widget",
            ConfigurationTypeName = ConfigTypeName
        });

        _model = new ContentZoneModel(_service, _zoneStore, _itemStore, _registry, _viewDiscovery,
            _routeService, _routeRegistration, _pageHistory, _slotDiscovery, _changeSetScope, _binder,
            TestSupport.PassThroughDeletes());
    }

    private static ContentZoneItemDTO Item(Guid nodeId, string json, Guid? imageId = null)
    {
        var item = new ContentZoneItemDTO
        {
            VersionId = Guid.NewGuid(),
            ComponentName = "Widget",
            ComponentPropertiesJson = json,
            IsActive = true,
            Version = new ContentVersion
            {
                Id = Guid.NewGuid(),
                Node = new ContentNode { Id = nodeId },
                VersionNumber = 1,
                State = ContentVersionState.Draft,
                IsCurrentDraft = true
            }
        }.In(Guid.NewGuid());
        if (imageId.HasValue)
        {
            item.Version.RelationWrites = new Dictionary<string, IReadOnlyList<RelationWrite>>
            {
                ["config.ImageId"] = [new RelationWrite(imageId.Value, 0, string.Empty, RelationKind.Reference)]
            };
        }
        return item;
    }

    private static ContentZoneItemUpsertViewModel Upsert(string json, Guid? nodeId = null, int? expected = null) => new()
    {
        NodeId = nodeId,
        ContentZoneNodeId = Guid.NewGuid(),
        ExpectedVersionNumber = expected,
        ComponentName = "Widget",
        ComponentPropertiesJson = json,
        IsActive = true
    };

    [Test]
    public async Task GetChildUpsertViewModelAsync_HydratesConfigRelations()
    {
        var nodeId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        _itemStore.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>())
            .Returns(Item(nodeId, "{\"other\":\"kept\"}", imageId));

        var vm = (ContentZoneItemUpsertViewModel)(await Items.GetUpsertViewModelAsync(nodeId, NoQuery))!;

        using var json = JsonDocument.Parse(vm.ComponentPropertiesJson);
        Assert.Multiple(() =>
        {
            Assert.That(json.RootElement.GetProperty("ImageId").GetString(), Is.EqualTo(imageId.ToString()));
            Assert.That(json.RootElement.GetProperty("other").GetString(), Is.EqualTo("kept"));
        });
    }

    [Test]
    public async Task SaveChildUpsertAsync_Create_LiftsRelationsOutOfTheConfigJson()
    {
        var imageId = Guid.NewGuid();
        ContentZoneItemDTO? added = null;
        _service.AddItemAsync(Arg.Any<Guid>(), Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                added = ci.Arg<ContentZoneItemDTO>();
                added.Version.Node = new ContentNode { Id = Guid.NewGuid() };
                return added;
            });
        var zoneId = Guid.NewGuid();

        var result = await CreateInZoneAsync(zoneId, Upsert($"{{\"ImageId\":\"{imageId}\",\"other\":\"kept\"}}"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(added, Is.Not.Null);
            Assert.That(added!.ComponentPropertiesJson, Does.Not.Contain("ImageId"));
            Assert.That(added.ComponentPropertiesJson, Does.Contain("kept"));
            Assert.That(added.Version.RelationWrites["config.ImageId"].Single().TargetNodeId, Is.EqualTo(imageId));
        });
    }

    [Test]
    public async Task SaveChildUpsertAsync_Update_LiftsRelationsOutOfTheConfigJson()
    {
        var nodeId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var existing = Item(nodeId, "{}");
        _itemStore.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(existing);
        ContentZoneItemDTO? updated = null;
        _service.UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns(ci =>
            {
                updated = ci.Arg<ContentZoneItemDTO>();
                return true;
            });

        var result = await Items.SaveUpsertAsync(Upsert($"{{\"ImageId\":\"{imageId}\"}}", nodeId, existing.Version.VersionNumber));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(updated, Is.Not.Null);
            Assert.That(updated!.ComponentPropertiesJson, Does.Not.Contain("ImageId"));
            Assert.That(updated.Version.RelationWrites["config.ImageId"].Single().TargetNodeId, Is.EqualTo(imageId));
        });
    }

    [Test]
    public async Task SaveChildUpsertAsync_InvalidRelation_ReturnsTheBinderFieldError()
    {
        _binder.ValidateAsync(Arg.Any<object>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AdminSaveResult(false, "Image is required.", "ImageId"));

        var result = await CreateInZoneAsync(Guid.NewGuid(), Upsert("{}"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorField, Is.EqualTo("ImageId"));
        });
        await _service.DidNotReceive().AddItemAsync(Arg.Any<Guid>(), Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SaveChildUpsertAsync_UnregisteredComponent_DoesNotTouchRelations()
    {
        _service.AddItemAsync(Arg.Any<Guid>(), Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var item = ci.Arg<ContentZoneItemDTO>();
                item.Version.Node = new ContentNode { Id = Guid.NewGuid() };
                return item;
            });
        var vm = new ContentZoneItemUpsertViewModel
        {
            ContentZoneNodeId = Guid.NewGuid(),
            ComponentName = "Unknown",
            ComponentPropertiesJson = "{\"anything\":1}",
            IsActive = true
        };

        var result = await CreateInZoneAsync(Guid.NewGuid(), vm);

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task SaveChildUpsertAsync_ConfigConstraintViolation_IsRejectedWithTheConfigFieldError()
    {
        _registry.ValidateConfiguration("Widget", Arg.Any<object>(), Arg.Any<WidgetDisplayMode?>())
            .Returns(new[] { "Count must be at most 10." });

        var result = await CreateInZoneAsync(Guid.NewGuid(), Upsert("{\"count\":99}"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorField, Is.EqualTo("ComponentPropertiesJson"));
            Assert.That(result.ErrorMessage, Is.EqualTo("Count must be at most 10."));
        });
        await _service.DidNotReceive().AddItemAsync(Arg.Any<Guid>(), Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SaveChildUpsertAsync_Update_ConfigConstraintViolation_IsRejectedWithTheConfigFieldError()
    {
        var nodeId = Guid.NewGuid();
        var existing = Item(nodeId, "{}");
        _itemStore.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(existing);
        _registry.ValidateConfiguration("Widget", Arg.Any<object>(), Arg.Any<WidgetDisplayMode?>())
            .Returns(new[] { "Label has an invalid format." });

        var result = await Items.SaveUpsertAsync(Upsert("{\"other\":\"x\"}", nodeId, existing.Version.VersionNumber));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorField, Is.EqualTo("ComponentPropertiesJson"));
        });
        await _service.DidNotReceive().UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>(), Arg.Any<int?>());
    }

    [Test]
    public async Task SaveChildUpsertAsync_EmptyConfigJson_IsValidatedAsAnEmptyObject()
    {
        // An empty payload is no way around a required config field.
        _registry.ValidateConfiguration("Widget", "{}", Arg.Any<WidgetDisplayMode?>())
            .Returns(new[] { "Other is required." });
        var vm = new ContentZoneItemUpsertViewModel
        {
            ComponentName = "Widget",
            ComponentPropertiesJson = null,
            IsActive = true
        };

        var result = await CreateInZoneAsync(Guid.NewGuid(), vm);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("Other is required."));
        });
        await _service.DidNotReceive().AddItemAsync(Arg.Any<Guid>(), Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SaveChildUpsertAsync_ConfigIsValidatedWithItsRelationIds()
    {
        // A required relation field (ContentBlock's ContentBlockID) is still set in what is validated:
        // the ids are stripped only afterwards.
        var imageId = Guid.NewGuid();
        var json = "{\"imageId\":\"" + imageId + "\",\"other\":\"x\"}";
        _service.AddItemAsync(Arg.Any<Guid>(), Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var item = ci.Arg<ContentZoneItemDTO>();
                item.Version.Node = new ContentNode { Id = Guid.NewGuid() };
                return item;
            });

        var result = await CreateInZoneAsync(Guid.NewGuid(), Upsert(json));

        Assert.That(result.Success, Is.True, result.ErrorMessage);
        _registry.Received(1).ValidateConfiguration("Widget", json, WidgetDisplayMode.Single);
    }

    [Test]
    public async Task SaveChildUpsertAsync_ListItem_ValidatesTheListFields()
    {
        _registry.GetByName("Widget").Returns(new WidgetRegistrationInfo
        {
            Name = "Widget",
            DisplayName = "Widget",
            ConfigurationTypeName = ConfigTypeName,
            SupportsList = true
        });
        var nodeId = Guid.NewGuid();
        var existing = Item(nodeId, "{}");
        existing.MetadataJson = "{\"DisplayMode\":\"List\"}";
        _itemStore.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(existing);
        _service.UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>(), Arg.Any<int?>()).Returns(true);

        // No metadata supplied: the stored List decides which fields are checked.
        await Items.SaveUpsertAsync(Upsert("{\"other\":\"x\"}", nodeId, existing.Version.VersionNumber));

        _registry.Received(1).ValidateConfiguration("Widget", "{\"other\":\"x\"}", WidgetDisplayMode.List);
    }

    [Test]
    public async Task SaveChildUpsertAsync_Create_ConfigOverColumnLength_IsRejectedBeforeTheStore()
    {
        var longJson = "{\"other\":\"" + new string('x', WidgetConfigurationJson.MaxJsonLength) + "\"}";

        var result = await CreateInZoneAsync(Guid.NewGuid(), Upsert(longJson));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorField, Is.EqualTo("ComponentPropertiesJson"));
            Assert.That(result.ErrorMessage, Does.Contain("exceeds the 4000 character limit"));
        });
        await _service.DidNotReceive().AddItemAsync(Arg.Any<Guid>(), Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SaveChildUpsertAsync_Update_ConfigOverColumnLength_IsRejectedBeforeTheStore()
    {
        var nodeId = Guid.NewGuid();
        var existing = Item(nodeId, "{}");
        _itemStore.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(existing);
        var longJson = "{\"other\":\"" + new string('x', WidgetConfigurationJson.MaxJsonLength) + "\"}";

        var result = await Items.SaveUpsertAsync(Upsert(longJson, nodeId, existing.Version.VersionNumber));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorField, Is.EqualTo("ComponentPropertiesJson"));
        });
        await _service.DidNotReceive().UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>(), Arg.Any<int?>());
    }

    [Test]
    public async Task ValidateConfigRelations_UsesTheComponentDisplayName_OrFallsBack()
    {
        var registration = new WidgetRegistrationInfo
        {
            Name = "Widget",
            DisplayName = null!,
            ConfigurationTypeName = ConfigTypeName
        };
        _registry.GetByName("Widget").Returns(registration, registration, (WidgetRegistrationInfo?)null);
        _service.AddItemAsync(Arg.Any<Guid>(), Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var item = ci.Arg<ContentZoneItemDTO>();
                item.Version.Node = new ContentNode { Id = Guid.NewGuid() };
                return item;
            });

        await CreateInZoneAsync(Guid.NewGuid(), Upsert("{}"));

        await _binder.Received(1).ValidateAsync(Arg.Any<object>(), "Widget", Arg.Any<CancellationToken>());
    }
}
