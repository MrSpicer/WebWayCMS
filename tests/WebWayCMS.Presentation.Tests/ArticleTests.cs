using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Models.Article;
using WebWayCMS.Presentation.Components.Widgets;
using WebWayCMS.Presentation.Rendering;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class ArticleDetailLinkTests
{
	[Test]
	public void Build_NullPage_HasNoRoutePrefix()
		=> Assert.That(ArticleDetailLink.Build(null, new ArticleViewModel { Slug = "post" }), Is.EqualTo("/post"));

	[Test]
	public void Build_TrimsPageRouteAndLeadingSlugSlash()
		=> Assert.That(ArticleDetailLink.Build(new PageDTO { Route = "/blog/" }, new ArticleViewModel { Slug = "/post" }), Is.EqualTo("/blog/post"));

	[Test]
	public void Build_NoSlug_FallsBackToId()
	{
		var id = Guid.NewGuid();
		Assert.That(ArticleDetailLink.Build(new PageDTO { Route = "/blog" }, new ArticleViewModel { Slug = null, Id = id }), Is.EqualTo($"/blog/{id}"));
	}

	[Test]
	public void Build_NoSlugNoId_HasEmptyTail()
		=> Assert.That(ArticleDetailLink.Build(new PageDTO { Route = "/blog" }, new ArticleViewModel { Slug = null, Id = null }), Is.EqualTo("/blog/"));

	[Test]
	public void Build_NullRoute_HasNoPrefix()
		=> Assert.That(ArticleDetailLink.Build(new PageDTO { Route = null! }, new ArticleViewModel { Slug = "post" }), Is.EqualTo("/post"));
}

[TestFixture]
public class ArticleWidgetResolverTests
{
	private IArticleListModel _listModel = null!;
	private IArticleModel _articleModel = null!;
	private ArticleWidgetResolver _resolver = null!;

	[SetUp]
	public void SetUp()
	{
		_listModel = Substitute.For<IArticleListModel>();
		_articleModel = Substitute.For<IArticleModel>();
		_resolver = new ArticleWidgetResolver(_listModel, _articleModel);
	}

	[Test]
	public void Constructor_NullArguments_Throw()
	{
		Assert.Multiple(() =>
		{
			Assert.That(() => new ArticleWidgetResolver(null!, _articleModel), Throws.ArgumentNullException);
			Assert.That(() => new ArticleWidgetResolver(_listModel, null!), Throws.ArgumentNullException);
		});
	}

	[Test]
	public async Task NullConfig_FallsBackToIndexList()
	{
		_listModel.GetIndexViewModelAsync(Arg.Any<CancellationToken>()).Returns(new ArticleListViewModel());

		var result = await _resolver.ResolveAsync(null, null);

		Assert.That(result.Kind, Is.EqualTo(ArticleRenderKind.List));
		await _listModel.Received(1).GetIndexViewModelAsync(Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task UpsertModel_ReturnsNone()
	{
		var result = await _resolver.ResolveAsync(new ArticleContentZoneConfiguration { UpsertModel = new ArticleUpsertViewModel() }, null);
		Assert.That(result.Kind, Is.EqualTo(ArticleRenderKind.None));
	}

	[Test]
	public async Task DirectArticle_ReturnsSingle()
	{
		var article = new ArticleViewModel { Title = "A" };
		var result = await _resolver.ResolveAsync(new ArticleContentZoneConfiguration { Article = article }, null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ArticleRenderKind.Single));
			Assert.That(result.Article, Is.SameAs(article));
		});
	}

	[Test]
	public async Task SubRoute_Found_ReturnsSingle()
	{
		var article = new ArticleViewModel { Title = "Detail" };
		_articleModel.GetBySlugAsync("my-slug", Arg.Any<CancellationToken>()).Returns(article);

		var result = await _resolver.ResolveAsync(new ArticleContentZoneConfiguration(), "my-slug");

		Assert.That(result.Article, Is.SameAs(article));
	}

	[Test]
	public async Task SubRoute_NotFound_FallsThroughToList()
	{
		_articleModel.GetBySlugAsync("missing", Arg.Any<CancellationToken>()).Returns((ArticleViewModel?)null);
		_listModel.GetIndexViewModelAsync(Arg.Any<CancellationToken>()).Returns(new ArticleListViewModel());

		var result = await _resolver.ResolveAsync(new ArticleContentZoneConfiguration(), "missing");

		Assert.That(result.Kind, Is.EqualTo(ArticleRenderKind.List));
	}

	[Test]
	public async Task ModeSingle_WithId_ReturnsSingle()
	{
		var id = Guid.NewGuid();
		_articleModel.GetPostViewModelAsync(id, Arg.Any<CancellationToken>()).Returns(new ArticleViewModel());

		var result = await _resolver.ResolveAsync(new ArticleContentZoneConfiguration { Mode = "single", Id = id }, null);

		Assert.That(result.Kind, Is.EqualTo(ArticleRenderKind.Single));
		await _articleModel.Received(1).GetPostViewModelAsync(id, Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task ModeList_WithListId_ReturnsList()
	{
		var listId = Guid.NewGuid();
		_listModel.GetArticlesForListAsync(listId, Arg.Any<CancellationToken>()).Returns(new ArticleListViewModel());

		var result = await _resolver.ResolveAsync(new ArticleContentZoneConfiguration { Mode = "LIST", ArticleListId = listId }, null);

		Assert.That(result.Kind, Is.EqualTo(ArticleRenderKind.List));
		await _listModel.Received(1).GetArticlesForListAsync(listId, Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Fallback_IdSet_ReturnsSingle()
	{
		var id = Guid.NewGuid();
		_articleModel.GetPostViewModelAsync(id, Arg.Any<CancellationToken>()).Returns(new ArticleViewModel());

		var result = await _resolver.ResolveAsync(new ArticleContentZoneConfiguration { Id = id }, null);

		Assert.That(result.Kind, Is.EqualTo(ArticleRenderKind.Single));
	}

	[Test]
	public async Task ModeSingle_NoId_FallsThroughToList()
	{
		_listModel.GetIndexViewModelAsync(Arg.Any<CancellationToken>()).Returns(new ArticleListViewModel());

		var result = await _resolver.ResolveAsync(new ArticleContentZoneConfiguration { Mode = "Single", Id = null }, null);

		Assert.That(result.Kind, Is.EqualTo(ArticleRenderKind.List));
	}

	[Test]
	public async Task ModeSingle_EmptyId_FallsThroughToList()
	{
		_listModel.GetIndexViewModelAsync(Arg.Any<CancellationToken>()).Returns(new ArticleListViewModel());

		var result = await _resolver.ResolveAsync(new ArticleContentZoneConfiguration { Mode = "Single", Id = Guid.Empty }, null);

		Assert.That(result.Kind, Is.EqualTo(ArticleRenderKind.List));
	}

	[Test]
	public async Task ModeList_NoListId_FallsThroughToList()
	{
		_listModel.GetIndexViewModelAsync(Arg.Any<CancellationToken>()).Returns(new ArticleListViewModel());

		var result = await _resolver.ResolveAsync(new ArticleContentZoneConfiguration { Mode = "List", ArticleListId = null }, null);

		Assert.That(result.Kind, Is.EqualTo(ArticleRenderKind.List));
		await _listModel.Received(1).GetIndexViewModelAsync(Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task ModeList_EmptyListId_FallsThroughToList()
	{
		_listModel.GetIndexViewModelAsync(Arg.Any<CancellationToken>()).Returns(new ArticleListViewModel());

		var result = await _resolver.ResolveAsync(new ArticleContentZoneConfiguration { Mode = "List", ArticleListId = Guid.Empty }, null);

		Assert.That(result.Kind, Is.EqualTo(ArticleRenderKind.List));
	}
}

[TestFixture]
public class ArticleWidgetTests
{
	private static Action<IServiceCollection> Resolver(ArticleRenderModel model)
	{
		var resolver = Substitute.For<IArticleWidgetResolver>();
		resolver.ResolveAsync(Arg.Any<ArticleContentZoneConfiguration?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(model);
		return s => s.AddSingleton(resolver);
	}

	[Test]
	public async Task Single_WithArticle_RendersCard_NoPageContext()
	{
		var model = ArticleRenderModel.ForSingle(new ArticleViewModel
		{
			Title = "Hello",
			Body = "<p>body</p>",
			Slug = "hello",
			PublicationDate = new DateTime(2024, 1, 15),
		});

		var html = await BlazorRenderHarness.RenderAsync<ArticleWidget>(
			new Dictionary<string, object?> { ["Config"] = new ArticleContentZoneConfiguration() },
			Resolver(model));

		Assert.Multiple(() =>
		{
			Assert.That(html, Does.Contain(">Hello<"));
			Assert.That(html, Does.Contain("<p>body</p>"));
			Assert.That(html, Does.Contain("href=\"/hello\""));
			Assert.That(html, Does.Contain("January 15, 2024"));
		});
	}

	[Test]
	public async Task Single_WithArticle_InPageContext_UsesPageRoute()
	{
		var model = ArticleRenderModel.ForSingle(new ArticleViewModel { Title = "Hello", Body = "b", Slug = "hello" });
		var context = new CmsRenderContext(new PageDTO { Route = "/blog" }, null, "sub");

		var html = await BlazorRenderHarness.RenderInContextAsync<ArticleWidget>(
			context,
			new Dictionary<string, object?> { ["Config"] = new ArticleContentZoneConfiguration() },
			Resolver(model));

		Assert.That(html, Does.Contain("href=\"/blog/hello\""));
	}

	[Test]
	public async Task Single_NullArticle_RendersNotFound()
	{
		var html = await BlazorRenderHarness.RenderAsync<ArticleWidget>(
			new Dictionary<string, object?> { ["Config"] = new ArticleContentZoneConfiguration() },
			Resolver(ArticleRenderModel.ForSingle(null)));

		Assert.That(html, Does.Contain("Article not found"));
	}

	[Test]
	public async Task List_WithArticles_RendersEach()
	{
		var model = ArticleRenderModel.ForList(new ArticleListViewModel
		{
			Articles = new List<ArticleViewModel>
			{
				new() { Title = "First", Body = "1" },
				new() { Title = "Second", Body = "2" },
			},
		});

		var html = await BlazorRenderHarness.RenderAsync<ArticleWidget>(
			new Dictionary<string, object?> { ["Config"] = new ArticleContentZoneConfiguration() },
			Resolver(model));

		Assert.Multiple(() =>
		{
			Assert.That(html, Does.Contain("columns is-multiline"));
			Assert.That(html, Does.Contain(">First<"));
			Assert.That(html, Does.Contain(">Second<"));
		});
	}

	[Test]
	public async Task List_Null_RendersSectionWithoutCards()
	{
		var html = await BlazorRenderHarness.RenderAsync<ArticleWidget>(
			new Dictionary<string, object?> { ["Config"] = new ArticleContentZoneConfiguration() },
			Resolver(ArticleRenderModel.ForList(null)));

		Assert.Multiple(() =>
		{
			Assert.That(html, Does.Contain("columns is-multiline"));
			Assert.That(html, Does.Not.Contain("card-content"));
		});
	}

	[Test]
	public async Task None_RendersNothing()
	{
		var html = await BlazorRenderHarness.RenderAsync<ArticleWidget>(
			new Dictionary<string, object?> { ["Config"] = new ArticleContentZoneConfiguration() },
			Resolver(ArticleRenderModel.None));

		Assert.That(html.Trim(), Is.Empty);
	}
}
