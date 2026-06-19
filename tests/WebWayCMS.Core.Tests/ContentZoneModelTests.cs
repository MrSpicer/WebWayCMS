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
	private IPageService _pageService = null!;
	private IContentZoneComponentRegistry _registry = null!;
	private IViewDiscoveryService _viewDiscovery = null!;
	private ContentZoneModel _model = null!;

	[SetUp]
	public void SetUp()
	{
		_service = Substitute.For<IContentZoneService>();
		_pageService = Substitute.For<IPageService>();
		_registry = Substitute.For<IContentZoneComponentRegistry>();
		_viewDiscovery = Substitute.For<IViewDiscoveryService>();
		_model = new ContentZoneModel(_service, _pageService, _registry, _viewDiscovery);
	}

	private static ContentZoneDTO Zone(Guid? id = null, string name = "Zone", params ContentZoneItemDTO[] items)
	{
		var cid = id ?? Guid.NewGuid();
		return new ContentZoneDTO
		{
			ContentId = cid,
			Name = name,
			Items = items.ToList(),
			ContentMeta = new ContentDTO { Id = cid, MasterId = cid, Title = name }
		};
	}

	private static ContentZoneItemDTO Item(string component, string json, int ordinal = 0)
	{
		var cid = Guid.NewGuid();
		return new ContentZoneItemDTO
		{
			ContentId = cid,
			ComponentName = component,
			ComponentPropertiesJson = json,
			Ordinal = ordinal,
			IsActive = true,
			ContentMeta = new ContentDTO { Id = cid, MasterId = cid }
		};
	}

	private static ViewDataDictionary NewViewData() => new(new EmptyModelMetadataProvider(), new ModelStateDictionary());

	[Test]
	public void Constructor_NullArguments_Throw()
	{
		Assert.Multiple(() =>
		{
			Assert.That(() => new ContentZoneModel(null!, _pageService, _registry, _viewDiscovery), Throws.ArgumentNullException);
			Assert.That(() => new ContentZoneModel(_service, null!, _registry, _viewDiscovery), Throws.ArgumentNullException);
			Assert.That(() => new ContentZoneModel(_service, _pageService, null!, _viewDiscovery), Throws.ArgumentNullException);
			Assert.That(() => new ContentZoneModel(_service, _pageService, _registry, null!), Throws.ArgumentNullException);
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

		_service.GetByNameAsync("Missing", Arg.Any<CancellationToken>()).Returns((ContentZoneDTO?)null);
		Assert.That((await _model.GetViewModelAsync("Missing"))!.Name, Is.EqualTo("Missing"));

		_service.GetByNameAsync("Found", Arg.Any<CancellationToken>()).Returns(Zone(name: "Found"));
		Assert.That(await _model.GetViewModelAsync("Found"), Is.Not.Null);
	}

	[Test]
	public async Task DeserializeProperties_AllBranches()
	{
		var zoneId = Guid.NewGuid();
		var zone = Zone(zoneId, "Z",
			Item("WithDefault", "{}", 1),
			Item("NoDefault", "", 2),
			Item("Typed", "{\"x\":5}", 3),
			Item("Untyped", "{\"a\":1}", 4),
			Item("Bad", "{bad", 5),
			Item("TypedNull", "null", 6));
		_service.GetByIdAsync(zoneId, Arg.Any<CancellationToken>()).Returns(zone);

		_registry.CreateDefaultConfiguration("WithDefault").Returns(new SampleZoneConfig());
		_registry.CreateDefaultConfiguration("NoDefault").Returns((object?)null);
		_registry.GetByName("Typed").Returns(new ContentZoneComponentInfo { Name = "Typed", ConfigurationType = typeof(SampleZoneConfig) });
		_registry.GetByName("Untyped").Returns((ContentZoneComponentInfo?)null);
		_registry.GetByName("Bad").Returns((ContentZoneComponentInfo?)null);
		_registry.GetByName("TypedNull").Returns(new ContentZoneComponentInfo { Name = "TypedNull", ConfigurationType = typeof(SampleZoneConfig) });

		var vm = await _model.GetViewModelByIdAsync(zoneId);

		Assert.That(vm!.ZoneObjects, Has.Count.EqualTo(6));
	}

	[Test]
	public async Task GetViewModelByIdAsync_NotFound_ReturnsNull()
	{
		_service.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ContentZoneDTO?)null);
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
	public async Task GetViewModelByPageSlot_NullAssignment_NullZone_AndFound()
	{
		var pageMaster = Guid.NewGuid();
		_service.GetByPageSlotAsync(pageMaster, "none", Arg.Any<CancellationToken>()).Returns((ContentZoneAssignmentDTO?)null);
		Assert.That(await _model.GetViewModelByPageSlotAsync(pageMaster, "none"), Is.Null);

		var assignment = new ContentZoneAssignmentDTO { ContentZoneId = Guid.NewGuid() };
		_service.GetByPageSlotAsync(pageMaster, "slot", Arg.Any<CancellationToken>()).Returns(assignment);
		_service.GetByIdAsync(assignment.ContentZoneId, Arg.Any<CancellationToken>()).Returns((ContentZoneDTO?)null, Zone());
		Assert.That(await _model.GetViewModelByPageSlotAsync(pageMaster, "slot"), Is.Null);
		Assert.That(await _model.GetViewModelByPageSlotAsync(pageMaster, "slot"), Is.Not.Null);
	}

	[Test]
	public async Task GetViewModelByZoneSlot_NullAssignment_AndFound()
	{
		var parent = Guid.NewGuid();
		_service.GetByZoneSlotAsync(parent, "none", Arg.Any<CancellationToken>()).Returns((ContentZoneAssignmentDTO?)null);
		Assert.That(await _model.GetViewModelByZoneSlotAsync(parent, "none"), Is.Null);

		var assignment = new ContentZoneAssignmentDTO { ContentZoneId = Guid.NewGuid() };
		_service.GetByZoneSlotAsync(parent, "slot", Arg.Any<CancellationToken>()).Returns(assignment);
		_service.GetByIdAsync(assignment.ContentZoneId, Arg.Any<CancellationToken>()).Returns(Zone());
		Assert.That(await _model.GetViewModelByZoneSlotAsync(parent, "slot"), Is.Not.Null);
	}

	[Test]
	public async Task PassthroughCrudAndItemOperations()
	{
		var zone = Zone();
		_service.GetByIdAsync(zone.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(zone);
		_service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ContentZoneDTO> { zone });
		_service.CreateAsync(zone, Arg.Any<CancellationToken>()).Returns(zone);
		_service.UpdateAsync(zone, Arg.Any<CancellationToken>()).Returns(true);
		_service.DeleteAsync(zone.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(true);
		var item = Item("C", "{}");
		_service.AddItemAsync(zone.ContentMeta.Id, item, Arg.Any<CancellationToken>()).Returns(item);
		_service.UpdateItemAsync(item, Arg.Any<CancellationToken>()).Returns(true);
		_service.RemoveItemAsync(item.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(true);
		_service.GetItemByIdAsync(item.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(item);
		_service.ReorderItemsAsync(zone.ContentMeta.Id, Arg.Any<List<Guid>>(), Arg.Any<CancellationToken>()).Returns(true);
		_service.GetAllVersionsAsync(zone.ContentMeta.MasterId, Arg.Any<CancellationToken>()).Returns(new List<ContentZoneDTO> { zone });
		_service.GetAllItemVersionsAsync(item.ContentMeta.MasterId, Arg.Any<CancellationToken>()).Returns(new List<ContentZoneItemDTO> { item });

		Assert.Multiple(async () =>
		{
			Assert.That(await _model.GetByIdAsync(zone.ContentMeta.Id), Is.SameAs(zone));
			Assert.That(await _model.GetAllAsync(), Has.Count.EqualTo(1));
			Assert.That(await _model.CreateAsync(zone), Is.SameAs(zone));
			Assert.That(await _model.UpdateAsync(zone), Is.True);
			Assert.That(await _model.DeleteAsync(zone.ContentMeta.Id), Is.True);
			Assert.That(await _model.AddItemAsync(zone.ContentMeta.Id, item), Is.SameAs(item));
			Assert.That(await _model.UpdateItemAsync(item), Is.True);
			Assert.That(await _model.RemoveItemAsync(item.ContentMeta.Id), Is.True);
			Assert.That(await _model.GetItemByIdAsync(item.ContentMeta.Id), Is.SameAs(item));
			Assert.That(await _model.ReorderItemsAsync(zone.ContentMeta.Id, new List<Guid>()), Is.True);
			Assert.That(await ((IContentZoneModel)_model).GetAllVersionsAsync(zone.ContentMeta.MasterId), Has.Count.EqualTo(1));
			Assert.That(await _model.GetAllItemVersionsAsync(item.ContentMeta.MasterId), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task GetIndexViewModelAsync_Parameterless()
	{
		_service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ContentZoneDTO> { Zone() });
		_service.GetZoneIdsWithChildrenAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>()).Returns(new HashSet<Guid>());
		_service.GetAssignmentCountsByMasterIdAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, int>());

		Assert.That(await _model.GetIndexViewModelAsync(), Is.InstanceOf<ContentZoneIndexViewModel>());
	}

	[Test]
	public async Task GetIndexViewModelAsync_Query_PageZoneAndDefault()
	{
		_service.GetZoneIdsWithChildrenAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>()).Returns(new HashSet<Guid>());
		_service.GetAssignmentCountsByMasterIdAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, int>());
		_service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ContentZoneDTO>());
		var pageId = Guid.NewGuid();
		var zoneId = Guid.NewGuid();
		_service.GetAllByPageAsync(pageId, Arg.Any<CancellationToken>()).Returns(new List<ContentZoneDTO>());
		_pageService.GetAllVersionsAsync(pageId, Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { new() { Route = "/r" } });
		_service.GetAllByParentZoneAsync(zoneId, Arg.Any<CancellationToken>()).Returns(new List<ContentZoneDTO>());
		_service.GetByIdAsync(zoneId, Arg.Any<CancellationToken>()).Returns(new ContentZoneDTO { Name = "Parent" });

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
		_service.GetByIdAsync(zone.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(zone);
		_service.GetByIdAsync(Arg.Is<Guid>(g => g != zone.ContentMeta.Id), Arg.Any<CancellationToken>()).Returns((ContentZoneDTO?)null);
		var q = new QueryCollection();

		Assert.Multiple(async () =>
		{
			Assert.That(await _model.GetUpsertViewModelAsync(null, q), Is.InstanceOf<ContentZoneUpsertViewModel>());
			Assert.That(await _model.GetUpsertViewModelAsync(zone.ContentMeta.Id, q), Is.Not.Null);
			Assert.That(await _model.GetUpsertViewModelAsync(Guid.NewGuid(), q), Is.Null);
		});
	}

	[Test]
	public async Task SaveUpsertAsync_CreateEditUpdateFailAndNotFound()
	{
		var zone = Zone();
		_service.GetByIdAsync(zone.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(zone);
		_service.UpdateAsync(Arg.Any<ContentZoneDTO>(), Arg.Any<CancellationToken>()).Returns(true, false);
		_service.CreateAsync(Arg.Any<ContentZoneDTO>(), Arg.Any<CancellationToken>()).Returns(c => c.Arg<ContentZoneDTO>());

		Assert.Multiple(async () =>
		{
			Assert.That((await _model.SaveUpsertAsync(new ContentZoneUpsertViewModel { Id = null, Name = "N", Title = "T" })).Success, Is.True);
			Assert.That((await _model.SaveUpsertAsync(new ContentZoneUpsertViewModel { Id = zone.ContentMeta.Id, Name = "N", Title = "T" })).Success, Is.True);
			Assert.That((await _model.SaveUpsertAsync(new ContentZoneUpsertViewModel { Id = zone.ContentMeta.Id, Name = "N", Title = "T" })).Success, Is.False);
		});

		_service.GetByIdAsync(Arg.Is<Guid>(g => g != zone.ContentMeta.Id), Arg.Any<CancellationToken>()).Returns((ContentZoneDTO?)null);
		Assert.That((await _model.SaveUpsertAsync(new ContentZoneUpsertViewModel { Id = Guid.NewGuid(), Name = "N", Title = "T" })).Success, Is.False);
	}

	[Test]
	public async Task ApiListSecondaryAndCreateEmpty()
	{
		_service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ContentZoneDTO> { Zone(name: ""), new() { Name = "n", ContentMeta = new ContentDTO { Id = Guid.NewGuid(), Title = "HasTitle" } } });

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
		var master = Guid.NewGuid();
		_service.GetAllVersionsAsync(master, Arg.Any<CancellationToken>()).Returns(new List<ContentZoneDTO> { Zone(master) });
		_service.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

		Assert.Multiple(async () =>
		{
			Assert.That(await _model.GetVersionHistoryViewModelAsync(master), Is.Not.Null);
			Assert.That(await _model.DeleteVersionAsync(Guid.NewGuid()), Is.True);
		});
	}

	[Test]
	public async Task BaseHandlerDefault_RestoreVersionIsNull()
	{
		// ContentZoneModel does not override the base GetRestoreVersionViewModelAsync.
		Assert.That(await _model.GetRestoreVersionViewModelAsync(Guid.NewGuid()), Is.Null);
	}

	// --- Child handler ---

	[Test]
	public async Task ChildHandler_FullFlow()
	{
		var child = _model.ChildHandler!;
		var zone = Zone();
		var item = Item("C", "{}");
		_service.GetByIdAsync(zone.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(zone);
		_service.GetItemByIdAsync(item.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(item);
		_service.UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>()).Returns(true);
		_service.RemoveItemAsync(item.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(true);
		var viewData = NewViewData();

		Assert.Multiple(async () =>
		{
			Assert.That(child.ChildType, Is.EqualTo("items"));
			Assert.That(child.ChildDisplayName, Is.EqualTo("Content Zone Item"));
			Assert.That(child.WriteRoles, Is.Null);
			Assert.That(child.ChildIndexViewPath, Does.Contain("ContentZoneItems.cshtml"));
			Assert.That(child.ChildUpsertViewPath, Does.Contain("ContentZoneItemUpsert.cshtml"));
			Assert.That(child.SupportsReorder, Is.False);
			Assert.That(child.CreateEmptyChildUpsertViewModel(), Is.InstanceOf<ContentZoneItemUpsertViewModel>());

			Assert.That(await child.GetChildIndexViewModelAsync("not-a-guid"), Is.Null);
			Assert.That(await child.GetChildIndexViewModelAsync(zone.ContentMeta.Id.ToString()), Is.SameAs(zone));

			Assert.That(await child.GetChildUpsertViewModelAsync("k", null), Is.Null);
			Assert.That(await child.GetChildUpsertViewModelAsync("k", item.ContentMeta.Id), Is.Not.Null);

			Assert.That(await child.ReorderAsync("k", new List<Guid>()), Is.False);
			Assert.That(await child.DeleteChildAsync(item.ContentMeta.Id), Is.True);
		});

		// SetChildUpsertViewData: invalid guid returns early; valid sets ViewData.
		await child.SetChildUpsertViewDataAsync(viewData, "not-a-guid");
		await child.SetChildUpsertViewDataAsync(viewData, zone.ContentMeta.Id.ToString());
		Assert.That(viewData["ZoneId"], Is.EqualTo(zone.ContentMeta.Id.ToString()));
	}

	[Test]
	public async Task ChildHandler_GetChildUpsert_MissingItem_ReturnsNull()
	{
		_service.GetItemByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ContentZoneItemDTO?)null);

		Assert.That(await _model.ChildHandler!.GetChildUpsertViewModelAsync("k", Guid.NewGuid()), Is.Null);
	}

	[Test]
	public async Task ChildHandler_SaveChildUpsert_Variants()
	{
		var child = _model.ChildHandler!;
		var item = Item("C", "{}");
		_service.GetItemByIdAsync(item.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(item);
		_service.UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>()).Returns(true, false);

		Assert.Multiple(async () =>
		{
			// no id -> error
			Assert.That((await child.SaveChildUpsertAsync("k", new ContentZoneItemUpsertViewModel { Id = null })).Success, Is.False);
			// id but item missing -> error
			Assert.That((await child.SaveChildUpsertAsync("k", new ContentZoneItemUpsertViewModel { Id = Guid.NewGuid() })).Success, Is.False);
			// update success then failure
			Assert.That((await child.SaveChildUpsertAsync("k", new ContentZoneItemUpsertViewModel { Id = item.ContentMeta.Id, ComponentName = "C" })).Success, Is.True);
			Assert.That((await child.SaveChildUpsertAsync("k", new ContentZoneItemUpsertViewModel { Id = item.ContentMeta.Id, ComponentName = "C" })).Success, Is.False);
		});
	}

	// --- Registry handler ---

	[Test]
	public void RegistryHandler_GetAll_Json()
	{
		_registry.GetAllComponents().Returns(new List<ContentZoneComponentInfo>
		{
			new() { Name = "C", DisplayName = "C", Description = "d", Category = "General" }
		});

		Assert.That(_model.RegistryHandler!.GetAll(), Is.InstanceOf<JsonResult>());
	}

	[Test]
	public void RegistryHandler_GetProperties_EmptyNameAndNotFound()
	{
		_registry.GetByName("X").Returns((ContentZoneComponentInfo?)null);

		Assert.Multiple(() =>
		{
			Assert.That(_model.RegistryHandler!.GetProperties(" "), Is.InstanceOf<BadRequestObjectResult>());
			Assert.That(_model.RegistryHandler!.GetProperties("X"), Is.InstanceOf<NotFoundObjectResult>());
		});
	}

	[Test]
	public void RegistryHandler_GetProperties_ViewPickerWithAndWithoutViews_AndPlainProperty()
	{
		_registry.GetByName("C").Returns(new ContentZoneComponentInfo
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
}
