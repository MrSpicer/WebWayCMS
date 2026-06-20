using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Models.Page;
using WebWayCMS.Presentation.Components.Widgets;
using WebWayCMS.Presentation.Rendering;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class PageNavigationBuilderTests
{
	private static PageTreeNode Node(string route, string title, bool published = true, bool hidden = false, List<PageTreeNode>? children = null)
		=> new()
		{
			Route = route,
			Title = title,
			IsPublished = published,
			IsHidden = hidden,
			PageId = Guid.NewGuid(),
			Children = children ?? new(),
		};

	[Test]
	public void Build_DefaultConfig_ExcludesIntermediateDraftHiddenAndAdmin()
	{
		var nodes = new List<PageTreeNode>
		{
			Node("/", "Home"),
			Node("/draft", "Draft", published: false),
			Node("/hidden", "Hidden", hidden: true),
			Node("/admin", "Admin"),
			new() { Route = "/segment", Title = "NoPage", PageId = null }, // intermediate route node
		};

		var items = PageNavigationBuilder.Build(nodes, new PageContentZoneConfiguration());

		Assert.That(items.Select(i => i.Route), Is.EquivalentTo(new[] { "/" }));
	}

	[Test]
	public void Build_ShowDraftsAndHidden_IncludesThem()
	{
		var nodes = new List<PageTreeNode>
		{
			Node("/", "Home"),
			Node("/draft", "Draft", published: false),
			Node("/hidden", "Hidden", hidden: true),
		};

		var items = PageNavigationBuilder.Build(nodes, new PageContentZoneConfiguration { ShowDraftPages = true, ShowHiddenPages = true });

		Assert.That(items.Select(i => i.Route), Is.EquivalentTo(new[] { "/", "/draft", "/hidden" }));
	}

	[Test]
	public void Build_AdminPages_KeepsOnlyAdminRoutes()
	{
		var nodes = new List<PageTreeNode> { Node("/", "Home"), Node("/admin", "Admin") };

		var items = PageNavigationBuilder.Build(nodes, new PageContentZoneConfiguration { AdminPages = true });

		Assert.That(items.Select(i => i.Route), Is.EquivalentTo(new[] { "/admin" }));
	}

	[Test]
	public void Build_MapsChildrenRecursively_AndPreservesFields()
	{
		var child = Node("/parent/child", "Child");
		var parent = Node("/parent", "Parent", children: new List<PageTreeNode> { child });

		var items = PageNavigationBuilder.Build(new List<PageTreeNode> { parent }, new PageContentZoneConfiguration());

		Assert.Multiple(() =>
		{
			Assert.That(items, Has.Count.EqualTo(1));
			Assert.That(items[0].Title, Is.EqualTo("Parent"));
			Assert.That(items[0].IsPublished, Is.True);
			Assert.That(items[0].Children.Select(c => c.Route), Is.EquivalentTo(new[] { "/parent/child" }));
		});
	}
}

[TestFixture]
public class PageNavigationWidgetTests
{
	private static IPageModel ModelWith(params PageTreeNode[] roots)
	{
		var model = Substitute.For<IPageModel>();
		model.GetPageIndexAsync(Arg.Any<CancellationToken>())
			.Returns(new PageIndexViewModel { Pages = roots.ToList() });
		return model;
	}

	[Test]
	public async Task RendersTopLevelItems_WithAndWithoutChildren()
	{
		var child = new PageTreeNode { Route = "/a/b", Title = "Child", PageId = Guid.NewGuid(), IsPublished = true };
		var parent = new PageTreeNode { Route = "/a", Title = "Parent", PageId = Guid.NewGuid(), IsPublished = true, Children = new() { child } };
		var solo = new PageTreeNode { Route = "/c", Title = "Solo", PageId = Guid.NewGuid(), IsPublished = true };

		var html = await BlazorRenderHarness.RenderAsync<PageNavigationWidget>(
			new Dictionary<string, object?> { ["Config"] = new PageContentZoneConfiguration() },
			s => s.AddSingleton(ModelWith(parent, solo)));

		Assert.Multiple(() =>
		{
			Assert.That(html, Does.Contain("href=\"/a\""));
			Assert.That(html, Does.Contain(">Parent<"));
			Assert.That(html, Does.Contain("<ul>"));
			Assert.That(html, Does.Contain("href=\"/a/b\""));
			Assert.That(html, Does.Contain("href=\"/c\""));
		});
	}

	[Test]
	public async Task NoPages_RendersNothing()
	{
		var html = await BlazorRenderHarness.RenderAsync<PageNavigationWidget>(
			configureServices: s => s.AddSingleton(ModelWith()));

		Assert.That(html.Trim(), Is.Empty);
	}
}
