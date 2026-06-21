using Bunit;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Models.Shared;
using WebWayCMS.Presentation.Components.Admin;
using WebWayCMS.Presentation.Rendering;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class AdminRoutesTests
{
	[TestCase("contentblocks", "/admin/blocks")]
	[TestCase("articles", "/admin/article-lists")]
	[TestCase("contentzones", "/admin/zones")]
	[TestCase("pages", "/admin/site-pages")]
	[TestCase("mystery", "/admin")]
	public void ListUrl_MapsContentTypeToBlazorRoute(string contentType, string expected)
		=> Assert.That(AdminRoutes.ListUrl(contentType), Is.EqualTo(expected));
}

[TestFixture]
public class AdminVersionHistoryPageTests
{
	private static VersionItemViewModel Version(int n, bool latest, bool published = true) => new()
	{
		Id = Guid.NewGuid(),
		Version = n,
		Title = $"v{n}",
		ModificationDate = new DateTime(2026, 1, 1, 0, n, 0),
		IsPublished = published,
		IsLatest = latest,
	};

	private static VersionHistoryViewModel Vm(params VersionItemViewModel[] versions) => new()
	{
		ContentType = "contentblocks",
		ItemTitle = "My Block",
		Versions = versions.ToList(),
	};

	private static (BunitContext Ctx, IAdminCrudHandler Handler, IAdminCrudChildHandler Child) Build(bool registerHandler = true, bool registerChild = true)
	{
		var ctx = new BunitContext();
		var child = Substitute.For<IAdminCrudChildHandler>();
		var handler = Substitute.For<IAdminCrudHandler>();
		handler.ChildHandler.Returns(registerChild ? child : null);
		var registry = Substitute.For<IAdminHandlerRegistry>();
		registry.GetHandler(Arg.Any<string>()).Returns(registerHandler ? handler : null);
		ctx.Services.AddSingleton(registry);
		return (ctx, handler, child);
	}

	private static IRenderedComponent<AdminVersionHistoryPage> RenderTop(BunitContext ctx, Guid? masterId = null)
		=> ctx.Render<AdminVersionHistoryPage>(p => p
			.Add(c => c.ContentType, "contentblocks")
			.Add(c => c.MasterId, masterId ?? Guid.NewGuid()));

	private static IRenderedComponent<AdminVersionHistoryPage> RenderChild(BunitContext ctx, Guid? masterId = null)
		=> ctx.Render<AdminVersionHistoryPage>(p => p
			.Add(c => c.ContentType, "articles")
			.Add(c => c.ParentKey, "news")
			.Add(c => c.ChildType, "articles")
			.Add(c => c.MasterId, masterId ?? Guid.NewGuid()));

	[Test]
	public void TopLevel_HandlerMissing_ShowsNotFound()
	{
		var (ctx, _, _) = Build(registerHandler: false);
		using (ctx)
		{
			Assert.That(RenderTop(ctx).Markup, Does.Contain("Version history not found"));
		}
	}

	[Test]
	public void Child_ChildHandlerMissing_ShowsNotFound()
	{
		var (ctx, _, _) = Build(registerChild: false);
		using (ctx)
		{
			Assert.That(RenderChild(ctx).Markup, Does.Contain("Version history not found"));
		}
	}

	[Test]
	public void TopLevel_RendersVersions_StatusFlags_RestoreLink_DeleteOnlyHistorical()
	{
		var (ctx, handler, _) = Build();
		var latest = Version(2, latest: true, published: true);
		var historical = Version(1, latest: false, published: false);
		handler.GetVersionHistoryViewModelAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
			.Returns(Vm(latest, historical));
		using (ctx)
		{
			var cut = RenderTop(ctx);
			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Contain("My Block"));
				Assert.That(cut.Markup, Does.Contain(">Current</span>"));
				Assert.That(cut.Markup, Does.Contain(">Historical</span>"));
				Assert.That(cut.Markup, Does.Contain("is-success")); // published latest
				Assert.That(cut.Markup, Does.Contain("is-warning")); // unpublished historical
				Assert.That(cut.Markup, Does.Contain($"/admin/blocks/create?restoreId={historical.Id}"));
				Assert.That(cut.Markup, Does.Contain("/admin/blocks\"")); // back link
				// Only the historical version has a Delete button (latest is excluded).
				Assert.That(cut.FindAll("tbody button.is-danger"), Has.Count.EqualTo(1));
			});
		}
	}

	[Test]
	public void Child_RendersRestoreLink_WithChildPath()
	{
		var (ctx, _, child) = Build();
		var historical = Version(1, latest: false);
		child.GetChildVersionHistoryViewModelAsync("news", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
			.Returns(new VersionHistoryViewModel { ContentType = "articles", ItemTitle = "Post", ParentKey = "news", ChildType = "articles", Versions = new() { historical } });
		using (ctx)
		{
			var cut = RenderChild(ctx);
			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Contain($"/admin/article-lists/news/articles/create?restoreId={historical.Id}"));
				Assert.That(cut.Markup, Does.Contain("/admin/article-lists/news/articles\"")); // child back link
			});
		}
	}

	[Test]
	public void ConfirmDelete_TopLevel_CallsHandler_AndReloads()
	{
		var (ctx, handler, _) = Build();
		var historical = Version(1, latest: false);
		handler.GetVersionHistoryViewModelAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
			.Returns(Vm(Version(2, latest: true), historical), Vm(Version(2, latest: true)));
		handler.DeleteVersionAsync(historical.Id, Arg.Any<CancellationToken>()).Returns(true);
		using (ctx)
		{
			var cut = RenderTop(ctx);
			cut.Find("tbody button.is-danger").Click();
			cut.Find("button.confirm-delete").Click();

			handler.Received(1).DeleteVersionAsync(historical.Id, Arg.Any<CancellationToken>());
			Assert.That(cut.FindAll("button.confirm-delete"), Is.Empty); // modal closed after reload
		}
	}

	[Test]
	public void ConfirmDelete_Child_CallsChildHandler()
	{
		var (ctx, _, child) = Build();
		var historical = Version(1, latest: false);
		child.GetChildVersionHistoryViewModelAsync("news", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
			.Returns(new VersionHistoryViewModel { ContentType = "articles", ItemTitle = "Post", ParentKey = "news", ChildType = "articles", Versions = new() { Version(2, latest: true), historical } });
		child.DeleteChildVersionAsync(historical.Id, Arg.Any<CancellationToken>()).Returns(true);
		using (ctx)
		{
			var cut = RenderChild(ctx);
			cut.Find("tbody button.is-danger").Click();
			cut.Find("button.confirm-delete").Click();

			child.Received(1).DeleteChildVersionAsync(historical.Id, Arg.Any<CancellationToken>());
		}
	}

	[Test]
	public void CancelDelete_ClosesModal()
	{
		var (ctx, handler, _) = Build();
		handler.GetVersionHistoryViewModelAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
			.Returns(Vm(Version(2, latest: true), Version(1, latest: false)));
		using (ctx)
		{
			var cut = RenderTop(ctx);
			cut.Find("tbody button.is-danger").Click();
			cut.Find("button.cancel-delete").Click();
			Assert.That(cut.Markup, Does.Not.Contain("modal is-active"));
		}
	}
}
