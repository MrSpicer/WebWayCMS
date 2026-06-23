using Bunit;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.ContentZones;
using WebWayCMS.Data.Models;
using WebWayCMS.Forms;
using WebWayCMS.Models.ContentBlock;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Presentation.Components.Admin;
using WebWayCMS.Presentation.Components.Widgets;
using WebWayCMS.Presentation.Rendering;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class ContentZoneEditorTests
{
	private static ContentZoneObject Obj(string name) =>
		new() { Id = Guid.NewGuid(), ComponentName = name, ComponentProperties = new object() };

	private static (IContentZoneModel Model, Guid ZoneId, ContentZoneViewModel Vm) Setup(params ContentZoneObject[] items)
	{
		var zoneId = Guid.NewGuid();
		var vm = new ContentZoneViewModel { Id = zoneId, RawZoneName = "Main", ZoneObjects = items.ToList() };
		var model = Substitute.For<IContentZoneModel>();
		model.GetViewModelByIdAsync(zoneId, Arg.Any<CancellationToken>()).Returns(vm);
		model.RemoveItemAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
		model.ReorderItemsAsync(Arg.Any<Guid>(), Arg.Any<List<Guid>>(), Arg.Any<CancellationToken>()).Returns(true);
		return (model, zoneId, vm);
	}

	private static IRenderedComponent<ContentZoneEditor> Render(BunitContext ctx, IContentZoneModel model, Guid zoneId)
	{
		ctx.Services.AddSingleton(model);
		// Empty widget registry -> no item previews rendered (covers the "no preview" branch).
		ctx.Services.AddSingleton<IContentZoneWidgetRegistry>(new ContentZoneWidgetRegistry(new Dictionary<string, Type>()));
		// The add/edit form (ContentZoneItemForm) injects a form-options provider and the view registry.
		var options = Substitute.For<IFormOptionsProvider>();
		options.GetOptionsAsync(Arg.Any<FormPropertyInfo>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<FormOption>>(Array.Empty<FormOption>()));
		ctx.Services.AddSingleton(options);
		var views = Substitute.For<IContentZoneViewRegistry>();
		views.GetComponentViews(Arg.Any<string>()).Returns(Array.Empty<string>());
		ctx.Services.AddSingleton(views);
		return ctx.Render<ContentZoneEditor>(p => p.Add(c => c.ZoneId, zoneId));
	}

	[Test]
	public void Items_WithMappedWidget_RenderPreview()
	{
		using var ctx = new BunitContext();
		var blockId = Guid.NewGuid();
		var item = new ContentZoneObject
		{
			Id = Guid.NewGuid(),
			ComponentName = "ContentBlock",
			ComponentProperties = new ContentBlockContentZoneConfiguration { ContentBlockID = blockId },
		};
		var zoneId = Guid.NewGuid();
		var vm = new ContentZoneViewModel { Id = zoneId, RawZoneName = "Main", ZoneObjects = new() { item } };
		var model = Substitute.For<IContentZoneModel>();
		model.GetViewModelByIdAsync(zoneId, Arg.Any<CancellationToken>()).Returns(vm);
		var blockModel = Substitute.For<IContentBlockModel>();
		blockModel.GetViewModelByMasterIdAsync(blockId, Arg.Any<CancellationToken>())
			.Returns(new ContentBlockViewModel { Content = "<p>preview here</p>" });

		ctx.Services.AddSingleton(model);
		ctx.Services.AddSingleton(blockModel);
		ctx.Services.AddSingleton<IContentZoneWidgetRegistry>(
			new ContentZoneWidgetRegistry(new Dictionary<string, Type> { ["ContentBlock"] = typeof(ContentBlockWidget) }));

		var cut = ctx.Render<ContentZoneEditor>(p => p.Add(c => c.ZoneId, zoneId));

		Assert.That(cut.Markup, Does.Contain("<p>preview here</p>"));
	}

	[Test]
	public void ZoneNotFound_ShowsWarning()
	{
		using var ctx = new BunitContext();
		var model = Substitute.For<IContentZoneModel>();
		model.GetViewModelByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ContentZoneViewModel?)null);

		var cut = Render(ctx, model, Guid.NewGuid());

		Assert.That(cut.Markup, Does.Contain("Content zone not found"));
	}

	[Test]
	public void EmptyZone_ShowsEmptyMessage()
	{
		using var ctx = new BunitContext();
		var (model, zoneId, _) = Setup();

		var cut = Render(ctx, model, zoneId);

		Assert.That(cut.Markup, Does.Contain("No widgets in this zone yet"));
	}

	[Test]
	public void WithItems_RendersLabels()
	{
		using var ctx = new BunitContext();
		var (model, zoneId, _) = Setup(Obj("Alpha"), Obj("Beta"));

		var cut = Render(ctx, model, zoneId);

		Assert.Multiple(() =>
		{
			Assert.That(cut.Markup, Does.Contain("Alpha"));
			Assert.That(cut.Markup, Does.Contain("Beta"));
			Assert.That(cut.Markup, Does.Not.Contain("modal is-active"));
		});
	}

	[Test]
	public void ClickDelete_ShowsConfirmModal()
	{
		using var ctx = new BunitContext();
		var (model, zoneId, _) = Setup(Obj("Alpha"));

		var cut = Render(ctx, model, zoneId);
		cut.Find(".zone-delete").Click();

		Assert.That(cut.Markup, Does.Contain("modal is-active"));
	}

	[Test]
	public void ConfirmDelete_RemovesItem_AndCallsModel()
	{
		using var ctx = new BunitContext();
		var item = Obj("Alpha");
		var (model, zoneId, _) = Setup(item);

		var cut = Render(ctx, model, zoneId);
		cut.Find(".zone-delete").Click();
		cut.Find(".confirm-delete").Click();

		Assert.That(cut.Markup, Does.Contain("No widgets in this zone yet"));
		model.Received(1).RemoveItemAsync(item.Id, Arg.Any<CancellationToken>());
	}

	[Test]
	public void CancelDelete_KeepsItem()
	{
		using var ctx = new BunitContext();
		var (model, zoneId, _) = Setup(Obj("Alpha"));

		var cut = Render(ctx, model, zoneId);
		cut.Find(".zone-delete").Click();
		cut.Find(".cancel-delete").Click();

		Assert.Multiple(() =>
		{
			Assert.That(cut.Markup, Does.Not.Contain("modal is-active"));
			Assert.That(cut.Markup, Does.Contain("Alpha"));
		});
	}

	[Test]
	public void MoveUp_FirstItem_IsNoOp()
	{
		using var ctx = new BunitContext();
		var (model, zoneId, _) = Setup(Obj("Alpha"), Obj("Beta"));

		var cut = Render(ctx, model, zoneId);
		cut.FindAll(".move-up")[0].Click(); // first item cannot move up

		model.DidNotReceive().ReorderItemsAsync(Arg.Any<Guid>(), Arg.Any<List<Guid>>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public void MoveUp_SecondItem_ReordersAndPersists()
	{
		using var ctx = new BunitContext();
		var alpha = Obj("Alpha");
		var beta = Obj("Beta");
		var (model, zoneId, vm) = Setup(alpha, beta);

		var cut = Render(ctx, model, zoneId);
		cut.FindAll(".move-up")[1].Click(); // Beta moves above Alpha

		Assert.That(vm.ZoneObjects[0], Is.SameAs(beta));
		model.Received(1).ReorderItemsAsync(zoneId, Arg.Is<List<Guid>>(ids => ids[0] == beta.Id && ids[1] == alpha.Id), Arg.Any<CancellationToken>());
	}

	[Test]
	public void MoveDown_LastItem_IsNoOp()
	{
		using var ctx = new BunitContext();
		var (model, zoneId, _) = Setup(Obj("Alpha"), Obj("Beta"));

		var cut = Render(ctx, model, zoneId);
		cut.FindAll(".move-down")[1].Click(); // last item cannot move down

		model.DidNotReceive().ReorderItemsAsync(Arg.Any<Guid>(), Arg.Any<List<Guid>>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public void MoveDown_FirstItem_ReordersAndPersists()
	{
		using var ctx = new BunitContext();
		var alpha = Obj("Alpha");
		var beta = Obj("Beta");
		var (model, zoneId, vm) = Setup(alpha, beta);

		var cut = Render(ctx, model, zoneId);
		cut.FindAll(".move-down")[0].Click(); // Alpha moves below Beta

		Assert.That(vm.ZoneObjects[1], Is.SameAs(alpha));
		model.Received(1).ReorderItemsAsync(zoneId, Arg.Is<List<Guid>>(ids => ids[0] == beta.Id && ids[1] == alpha.Id), Arg.Any<CancellationToken>());
	}

	// ─── Add / Edit widget wiring (3c-2) ────────────────────────────────────────

	private static IContentZoneComponentRegistry ItemRegistry()
	{
		var bare = new ContentZoneComponentInfo { Name = "Bare", DisplayName = "Bare", Category = "General", ConfigurationType = null };
		var reg = Substitute.For<IContentZoneComponentRegistry>();
		reg.GetComponentsByCategory().Returns(new Dictionary<string, IReadOnlyList<ContentZoneComponentInfo>> { ["General"] = new[] { bare } });
		reg.GetByName("Bare").Returns(bare);
		reg.CreateDefaultConfiguration("Bare").Returns((object?)null);
		return reg;
	}

	[Test]
	public void AddWidget_OpensForm()
	{
		using var ctx = new BunitContext();
		ctx.Services.AddSingleton(ItemRegistry());
		var (model, zoneId, _) = Setup();

		var cut = Render(ctx, model, zoneId);
		cut.Find(".add-widget").Click();

		Assert.Multiple(() =>
		{
			Assert.That(cut.Markup, Does.Contain("cz-item-form")); // the add/edit modal
			// Add mode: component selector must be enabled (guards the Razor literal-vs-expression binding).
			Assert.That(cut.Find(".component-selector").HasAttribute("disabled"), Is.False);
			Assert.That(cut.Markup, Does.Contain("Add Widget"));
		});
	}

	[Test]
	public void AddWidget_Save_AddsItemAndReloads()
	{
		using var ctx = new BunitContext();
		ctx.Services.AddSingleton(ItemRegistry());
		var (model, zoneId, _) = Setup();
		model.AddItemAsync(Arg.Any<Guid>(), Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>()).Returns(c => c.Arg<ContentZoneItemDTO>());

		var cut = Render(ctx, model, zoneId);
		cut.Find(".add-widget").Click();
		cut.Find(".component-selector").Change("Bare");
		cut.Find(".save-form").Click();

		model.Received(1).AddItemAsync(zoneId, Arg.Is<ContentZoneItemDTO>(d => d.ComponentName == "Bare"), Arg.Any<CancellationToken>());
		model.Received(2).GetViewModelByIdAsync(zoneId, Arg.Any<CancellationToken>()); // initial + reload
	}

	[Test]
	public void EditItem_NotFound_DoesNotOpenForm()
	{
		using var ctx = new BunitContext();
		ctx.Services.AddSingleton(ItemRegistry());
		var (model, zoneId, _) = Setup(Obj("Bare"));
		model.GetItemByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ContentZoneItemDTO?)null);

		var cut = Render(ctx, model, zoneId);
		cut.Find(".edit-item").Click();

		Assert.That(cut.Markup, Does.Not.Contain("Edit Widget"));
	}

	[Test]
	public void EditItem_Save_UpdatesItem()
	{
		using var ctx = new BunitContext();
		ctx.Services.AddSingleton(ItemRegistry());
		var item = Obj("Bare");
		var (model, zoneId, _) = Setup(item);
		model.GetItemByIdAsync(item.Id, Arg.Any<CancellationToken>())
			.Returns(new ContentZoneItemDTO { ContentId = item.Id, ComponentName = "Bare", ComponentPropertiesJson = "{}" });
		model.UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>()).Returns(true);

		var cut = Render(ctx, model, zoneId);
		cut.Find(".edit-item").Click();
		Assert.That(cut.Markup, Does.Contain("Edit Widget"));

		cut.Find(".save-form").Click();
		model.Received(1).UpdateItemAsync(Arg.Is<ContentZoneItemDTO>(d => d.ContentId == item.Id), Arg.Any<CancellationToken>());
	}

	[Test]
	public void AddWidget_Cancel_ClosesForm()
	{
		using var ctx = new BunitContext();
		ctx.Services.AddSingleton(ItemRegistry());
		var (model, zoneId, _) = Setup();

		var cut = Render(ctx, model, zoneId);
		cut.Find(".add-widget").Click();
		cut.Find(".cancel-form").Click();

		Assert.That(cut.Markup, Does.Not.Contain("cz-item-form"));
	}
}
