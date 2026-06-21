using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.ContentZones;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.ContentBlock;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Presentation.Components.Admin;
using WebWayCMS.Presentation.Rendering;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class AdminDashboardTests
{
	private static Action<IServiceCollection> WithData(
		List<PageDTO> pages, List<ArticleDTO> articles, List<ContentBlockDTO> blocks, List<ContentZoneDTO> zones)
	{
		var pageService = Substitute.For<IPageService>();
		pageService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(pages);
		var articleService = Substitute.For<IContentService<ArticleDTO>>();
		articleService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(articles);
		var blockService = Substitute.For<IContentService<ContentBlockDTO>>();
		blockService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(blocks);
		var zoneService = Substitute.For<IContentZoneService>();
		zoneService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(zones);

		// The dashboard also renders the "Main" content zone, which needs the zone resolver/registry.
		var resolver = Substitute.For<IContentZoneResolver>();
		resolver.ResolveAsync(Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<Guid?>(), Arg.Any<PageDTO?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
			.Returns(new ContentZoneViewModel { Id = Guid.Empty, ZoneObjects = new List<ContentZoneObject>() });

		return s =>
		{
			s.AddSingleton(pageService);
			s.AddSingleton(articleService);
			s.AddSingleton(blockService);
			s.AddSingleton(zoneService);
			s.AddSingleton(resolver);
			s.AddSingleton<IContentZoneWidgetRegistry>(new ContentZoneWidgetRegistry(new Dictionary<string, Type>()));
			s.AddSingleton(Substitute.For<IContentBlockModel>());
		};
	}

	[Test]
	public async Task EmptyData_ShowsZeroStats_AndEmptyStates()
	{
		var html = await BlazorRenderHarness.RenderAsync<AdminDashboard>(
			configureServices: WithData(new(), new(), new(), new()));

		Assert.Multiple(() =>
		{
			Assert.That(html, Does.Contain("Dashboard"));
			Assert.That(html, Does.Contain("No pages yet"));
			Assert.That(html, Does.Contain("No articles yet"));
			Assert.That(html, Does.Contain("0 published"));
		});
	}

	[Test]
	public async Task WithData_RendersStats_RecentTables_PublishedDraft_AndAuthorVariants()
	{
		var pages = new List<PageDTO>
		{
			new() { Route = "/home", ContentMeta = new ContentDTO { Title = "Home", IsPublished = true } },
			new() { Route = "/about", ContentMeta = new ContentDTO { Title = "About", IsPublished = false } },
		};
		var articles = new List<ArticleDTO>
		{
			new() { AuthorName = "Jane", ContentMeta = new ContentDTO { Title = "Post 1", IsPublished = true } },
			new() { AuthorName = "", ContentMeta = new ContentDTO { Title = "Post 2", IsPublished = false } },
		};
		var blocks = new List<ContentBlockDTO> { new() { ContentMeta = new ContentDTO { Title = "B" } } };
		var zones = new List<ContentZoneDTO> { new() { Name = "Main" } };

		var html = await BlazorRenderHarness.RenderAsync<AdminDashboard>(
			configureServices: WithData(pages, articles, blocks, zones));

		Assert.Multiple(() =>
		{
			Assert.That(html, Does.Contain("1 published"));
			Assert.That(html, Does.Contain("1 draft"));
			Assert.That(html, Does.Contain("Home"));
			Assert.That(html, Does.Contain("/admin/site-pages/edit/")); // recent page edit link
			Assert.That(html, Does.Contain("Jane")); // article with author
			Assert.That(html, Does.Contain("Published"));
			Assert.That(html, Does.Contain("Draft"));
			Assert.That(html, Does.Contain("—")); // article without author -> dash
		});
	}
}
