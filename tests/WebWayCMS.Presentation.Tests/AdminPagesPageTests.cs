using Bunit;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Models.Page;
using WebWayCMS.Presentation.Components.Admin;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class AdminPagesPageTests
{
	private static PageTreeNode Leaf(string route, string title, bool published, string controller, bool withMaster = true) => new()
	{
		Route = route,
		Title = title,
		PageId = Guid.NewGuid(),
		PageMasterId = withMaster ? Guid.NewGuid() : null,
		ControllerName = controller,
		IsPublished = published,
	};

	// Root with an intermediate (no page) node that has a leaf child — exercises every PageTreeRows branch.
	private static PageIndexViewModel Tree() => new()
	{
		Pages = new()
		{
			Leaf("/", "Home", published: true, controller: "Standard"),
			new PageTreeNode
			{
				Route = "/products",
				Title = "products",
				PageId = null, // intermediate segment, no page
				ControllerName = string.Empty,
				Children = new() { Leaf("/products/widget", "Widget", published: false, controller: string.Empty, withMaster: false) },
			},
		},
	};

	private static (BunitContext Ctx, IPageModel Model) Build(params PageIndexViewModel[] sequentialResults)
	{
		var ctx = new BunitContext();
		var model = Substitute.For<IPageModel>();
		if (sequentialResults.Length == 1)
			model.GetPageIndexAsync(Arg.Any<CancellationToken>()).Returns(sequentialResults[0]);
		else
			model.GetPageIndexAsync(Arg.Any<CancellationToken>()).Returns(sequentialResults[0], sequentialResults[1..]);
		ctx.Services.AddSingleton(model);
		return (ctx, model);
	}

	[Test]
	public void EmptyTree_ShowsEmptyState()
	{
		var (ctx, _) = Build(new PageIndexViewModel());
		using (ctx)
		{
			Assert.That(ctx.Render<AdminPagesPage>().Markup, Does.Contain("No pages yet"));
		}
	}

	[Test]
	public void Tree_RendersNodes_IntermediatePlaceholder_Tags_AndChildIndent()
	{
		var (ctx, _) = Build(Tree());
		using (ctx)
		{
			var cut = ctx.Render<AdminPagesPage>();
			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Contain("Home"));
				Assert.That(cut.Markup, Does.Contain("Widget"));
				Assert.That(cut.Markup, Does.Contain(">Standard</span>")); // controller tag on root
				Assert.That(cut.Markup, Does.Contain("—")); // intermediate node placeholder (no page)
				Assert.That(cut.Markup, Does.Contain("└─")); // child indent marker (depth > 0)
				Assert.That(cut.Markup, Does.Contain("is-success")); // published root
				Assert.That(cut.Markup, Does.Contain("is-warning")); // unpublished child
				Assert.That(cut.Markup, Does.Contain("/admin/zones?pageId=")); // Content Zones link (root has masterId)
			});
		}
	}

	[Test]
	public void ConfirmDelete_CallsModel_AndReloads()
	{
		var (ctx, model) = Build(Tree(), new PageIndexViewModel()); // reload after delete -> empty
		using (ctx)
		{
			var cut = ctx.Render<AdminPagesPage>();
			cut.Find("tbody button.is-danger").Click(); // first deletable node (root "/")
			cut.Find("button.confirm-delete").Click();

			Assert.That(cut.Markup, Does.Contain("No pages yet"));
			model.Received(1).DeletePageAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
		}
	}

	[Test]
	public void CancelDelete_ClosesModal_KeepsTree()
	{
		var (ctx, _) = Build(Tree());
		using (ctx)
		{
			var cut = ctx.Render<AdminPagesPage>();
			cut.Find("tbody button.is-danger").Click();
			cut.Find("button.cancel-delete").Click();

			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Not.Contain("modal is-active"));
				Assert.That(cut.Markup, Does.Contain("Home"));
			});
		}
	}
}
