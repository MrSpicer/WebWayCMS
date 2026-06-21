using Bunit;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Presentation.Components.Admin;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class AdminZoneItemsPageTests
{
	private static ContentZoneItemDTO Item(string component, int ordinal, bool active = true, string json = "{}") => new()
	{
		ContentId = Guid.NewGuid(),
		Ordinal = ordinal,
		ComponentName = component,
		ComponentPropertiesJson = json,
		IsActive = active,
	};

	private static ContentZoneDTO Zone(string description, params ContentZoneItemDTO[] items) => new()
	{
		ContentId = Guid.NewGuid(),
		Name = "Main",
		Description = description,
		Items = items.ToList(),
	};

	private static (BunitContext Ctx, IAdminCrudChildHandler Child) Build(ContentZoneDTO? zone)
	{
		var ctx = new BunitContext();
		var child = Substitute.For<IAdminCrudChildHandler>();
		child.GetChildIndexViewModelAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((object?)zone);
		var handler = Substitute.For<IAdminCrudHandler>();
		handler.ChildHandler.Returns(child);
		var registry = Substitute.For<IAdminHandlerRegistry>();
		registry.GetHandler("contentzones").Returns(handler);
		ctx.Services.AddSingleton(registry);
		return (ctx, child);
	}

	private static IRenderedComponent<AdminZoneItemsPage> Render(BunitContext ctx, Guid? zoneId = null)
		=> ctx.Render<AdminZoneItemsPage>(p => p.Add(c => c.ZoneId, zoneId ?? Guid.NewGuid()));

	[Test]
	public void ZoneNotFound_ShowsWarning()
	{
		var (ctx, _) = Build(null);
		using (ctx)
		{
			Assert.That(Render(ctx).Markup, Does.Contain("Content zone not found"));
		}
	}

	[Test]
	public void NoItems_ShowsEmptyState_NoDescription()
	{
		var (ctx, _) = Build(Zone(description: string.Empty)); // empty description -> no subtitle
		using (ctx)
		{
			var cut = Render(ctx);
			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Contain("No items in this zone yet"));
				Assert.That(cut.Markup, Does.Contain("Items in: Main")); // header renders; empty description -> no subtitle line
			});
		}
	}

	[Test]
	public void WithItems_RendersColumns_ActiveFlags_TruncatedJson_AndDescription()
	{
		var longJson = "{\"key\":\"" + new string('v', 60) + "\"}";
		var zoneId = Guid.NewGuid();
		var active = Item("ContentBlock", 1, active: true, json: "{}");
		var inactive = Item("Article", 2, active: false, json: longJson);
		var (ctx, _) = Build(Zone("Primary area", active, inactive));
		using (ctx)
		{
			var cut = Render(ctx, zoneId);
			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Contain("Primary area")); // description subtitle
				Assert.That(cut.Markup, Does.Contain("ContentBlock"));
				Assert.That(cut.Markup, Does.Contain("Article"));
				Assert.That(cut.Markup, Does.Contain($"/admin/zones/{zoneId}/items/edit/{active.ContentId}"));
				Assert.That(cut.Markup, Does.Contain("is-success")); // active
				Assert.That(cut.Markup, Does.Contain("is-warning")); // inactive
				Assert.That(cut.Markup, Does.Contain("...")); // long json truncated
			});
		}
	}

	[Test]
	public void ConfirmDelete_CallsHandler_AndRemovesRow()
	{
		var item = Item("ContentBlock", 1);
		var (ctx, child) = Build(Zone("d", item));
		child.DeleteChildAsync(item.ContentId, Arg.Any<CancellationToken>()).Returns(true);
		using (ctx)
		{
			var cut = Render(ctx);
			cut.Find("tbody button.is-danger").Click();
			cut.Find("button.confirm-delete").Click();

			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Contain("No items in this zone yet"));
				Assert.That(cut.Markup, Does.Not.Contain("ContentBlock"));
			});
			child.Received(1).DeleteChildAsync(item.ContentId, Arg.Any<CancellationToken>());
		}
	}

	[Test]
	public void CancelDelete_ClosesModal_KeepsRow()
	{
		var (ctx, _) = Build(Zone("d", Item("ContentBlock", 1)));
		using (ctx)
		{
			var cut = Render(ctx);
			cut.Find("tbody button.is-danger").Click();
			cut.Find("button.cancel-delete").Click();

			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Not.Contain("modal is-active"));
				Assert.That(cut.Markup, Does.Contain("ContentBlock"));
			});
		}
	}
}
