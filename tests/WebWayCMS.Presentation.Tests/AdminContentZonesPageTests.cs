using Bunit;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Presentation.Components.Admin;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class AdminContentZonesPageTests
{
	private static ContentZoneDTO Zone(string name, bool published = true, Guid? contentId = null, Guid? masterId = null) => new()
	{
		ContentId = contentId ?? Guid.NewGuid(),
		Name = name,
		Description = name + " description",
		ContentMeta = new ContentDTO
		{
			MasterId = masterId ?? Guid.NewGuid(),
			IsPublished = published,
			Version = 1,
			ModificationDate = new DateTime(2026, 1, 2, 3, 4, 0),
		},
		Items = new(),
	};

	private static (BunitContext Ctx, IAdminCrudHandler Handler) Build(ContentZoneIndexViewModel vm)
	{
		var ctx = new BunitContext();
		var handler = Substitute.For<IAdminCrudHandler>();
		handler.GetIndexViewModelAsync(Arg.Any<IQueryCollection>(), Arg.Any<CancellationToken>()).Returns((object)vm);
		var registry = Substitute.For<IAdminHandlerRegistry>();
		registry.GetHandler("contentzones").Returns(handler);
		ctx.Services.AddSingleton(registry);
		return (ctx, handler);
	}

	private static IRenderedComponent<AdminContentZonesPage> Render(BunitContext ctx, string? uri = null)
	{
		if (uri is not null)
			ctx.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo(uri);
		return ctx.Render<AdminContentZonesPage>();
	}

	[Test]
	public void EmptyList_ShowsEmptyState()
	{
		var (ctx, _) = Build(new ContentZoneIndexViewModel());
		using (ctx)
		{
			var cut = Render(ctx);
			Assert.That(cut.Markup, Does.Contain("No content zones defined"));
		}
	}

	[Test]
	public void WithZones_RendersColumns_PublishedAndGlobalFlags()
	{
		var global = Zone("Header", published: true);
		var assigned = Zone("Sidebar", published: false);
		var vm = new ContentZoneIndexViewModel
		{
			Zones = new() { global, assigned },
			AssignmentCountsByMasterId = new() { [assigned.ContentMeta.MasterId] = 3 }, // global has none -> 0
		};
		var (ctx, _) = Build(vm);
		using (ctx)
		{
			var cut = Render(ctx, "http://localhost/admin/zones?pageId=" + Guid.NewGuid() + "&zoneId=" + Guid.NewGuid());
			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Contain("Header"));
				Assert.That(cut.Markup, Does.Contain("Sidebar"));
				Assert.That(cut.Markup, Does.Contain($"/admin/zones/edit/{global.ContentId}"));
				Assert.That(cut.Markup, Does.Contain($"/admin/zone-editor/{global.ContentId}"));
				Assert.That(cut.Markup, Does.Contain($"/admin/zones/{global.ContentId}/items"));
				Assert.That(cut.Markup, Does.Contain("is-warning")); // unpublished tag
				Assert.That(cut.Markup, Does.Not.Contain("Child Zones"));
			});
		}
	}

	[Test]
	public void ChildZones_LinkShown_OnlyForZonesWithChildren()
	{
		var withChildren = Zone("Parent");
		var leaf = Zone("Leaf");
		var vm = new ContentZoneIndexViewModel
		{
			Zones = new() { withChildren, leaf },
			ZoneIdsWithChildren = new() { withChildren.ContentId },
		};
		var (ctx, _) = Build(vm);
		using (ctx)
		{
			var cut = Render(ctx);
			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Contain("Child Zones"));
				Assert.That(cut.Markup, Does.Contain($"/admin/zones?zoneId={withChildren.ContentId}"));
				Assert.That(System.Text.RegularExpressions.Regex.Matches(cut.Markup, "Child Zones"), Has.Count.EqualTo(1)); // only the parent
			});
		}
	}

	[Test]
	public void NoFilter_ShowsNoBanner()
	{
		var (ctx, _) = Build(new ContentZoneIndexViewModel { Zones = new() { Zone("Z") } });
		using (ctx)
		{
			var cut = Render(ctx);
			Assert.That(cut.Markup, Does.Not.Contain("Filtered by"));
		}
	}

	[Test]
	public void PageFilter_ShowsBanner_RouteWhenPresent_ElseId()
	{
		var pageId = Guid.NewGuid();
		var withRoute = new ContentZoneIndexViewModel { FilterPageId = pageId, FilterPageRoute = "/home", Zones = new() { Zone("Z") } };
		var (ctx1, _) = Build(withRoute);
		using (ctx1)
		{
			Assert.That(Render(ctx1).Markup, Does.Contain("page: /home"));
		}

		var withoutRoute = new ContentZoneIndexViewModel { FilterPageId = pageId, Zones = new() { Zone("Z") } };
		var (ctx2, _) = Build(withoutRoute);
		using (ctx2)
		{
			Assert.That(Render(ctx2).Markup, Does.Contain($"page: {pageId}"));
		}
	}

	[Test]
	public void ZoneFilter_ShowsBanner_NameWhenPresent_ElseId()
	{
		var zoneId = Guid.NewGuid();
		var withName = new ContentZoneIndexViewModel { FilterParentZoneId = zoneId, FilterParentZoneName = "Main", Zones = new() { Zone("Z") } };
		var (ctx1, _) = Build(withName);
		using (ctx1)
		{
			Assert.That(Render(ctx1).Markup, Does.Contain("zone: Main"));
		}

		var withoutName = new ContentZoneIndexViewModel { FilterParentZoneId = zoneId, Zones = new() { Zone("Z") } };
		var (ctx2, _) = Build(withoutName);
		using (ctx2)
		{
			Assert.That(Render(ctx2).Markup, Does.Contain($"zone: {zoneId}"));
		}
	}

	[Test]
	public void ConfirmDelete_CallsHandler_AndRemovesRow()
	{
		var zone = Zone("Header");
		var (ctx, handler) = Build(new ContentZoneIndexViewModel { Zones = new() { zone } });
		handler.DeleteAsync(zone.ContentId, Arg.Any<CancellationToken>()).Returns(true);
		using (ctx)
		{
			var cut = Render(ctx);
			cut.Find("tbody button.is-danger").Click();
			cut.Find("button.confirm-delete").Click();

			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Not.Contain("Header"));
				Assert.That(cut.Markup, Does.Contain("No content zones defined"));
			});
			handler.Received(1).DeleteAsync(zone.ContentId, Arg.Any<CancellationToken>());
		}
	}

	[Test]
	public void CancelDelete_ClosesModal_KeepsRow()
	{
		var (ctx, _) = Build(new ContentZoneIndexViewModel { Zones = new() { Zone("Header") } });
		using (ctx)
		{
			var cut = Render(ctx);
			cut.Find("tbody button.is-danger").Click();
			cut.Find("button.cancel-delete").Click();

			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Not.Contain("modal is-active"));
				Assert.That(cut.Markup, Does.Contain("Header"));
			});
		}
	}
}
