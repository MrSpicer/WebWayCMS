using Microsoft.AspNetCore.Mvc.ViewComponents;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Interfaces;
using WebWayCMS.Models.Article;
using WebWayCMS.ViewComponents;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class ArticleViewComponentTests
{
    private const string WidgetRouteTokens = "{\"ParentPageNodeId\":\"6f1d3a1e-0000-0000-0000-000000000001\"}";

    private IArticleModel _articleModel = null!;
    private ICMSRouteService _routeService = null!;
    private ArticleViewComponent _component = null!;
    private WidgetComponentHarness.Context _widget = null!;
    private Guid _itemId;

    [SetUp]
    public void SetUp()
    {
        _articleModel = Substitute.For<IArticleModel>();
        _routeService = Substitute.For<ICMSRouteService>();
        _component = new ArticleViewComponent(_articleModel, _routeService);
        _widget = WidgetComponentHarness.Attach(_component);
        _widget.Hit("Article", WidgetRenderMode.View, "default", "view.default");
        _widget.Hit("Article", WidgetRenderMode.List, "default", "list.default");
        _widget.Hit("Article", WidgetRenderMode.Edit, "default", "edit.default");
        _itemId = Guid.NewGuid();
    }

    private ArticleContentZoneConfiguration Single(Guid? articleId = null)
        => new() { ArticleId = articleId, ZoneItemNodeId = _itemId };

    private ArticleContentZoneConfiguration List(Guid? listId = null, int? max = null, string? sort = null)
        => new()
        {
            ArticleListId = listId,
            MaxItems = max,
            SortOrder = sort,
            ZoneItemNodeId = _itemId,
            Metadata = new WidgetMetadata { DisplayMode = WidgetDisplayMode.List }
        };

    private void EditMode() => _component.ViewComponentContext.ViewData["ContentZone:RenderMode"] = WidgetRenderMode.Edit;

    /// <summary>The request matched a route: this item's own widget route, another item's, or the page's.</summary>
    private void MatchedRoute(Guid? owner, string dataTokens, string? slug = null, string pattern = "/blog")
    {
        _component.HttpContext.Items["CMS:RouteData"] = new CMSRouteDTO { OwningContentNodeId = owner, DataTokensJson = dataTokens, Pattern = pattern };
        if (slug != null)
            _component.ViewContext.RouteData.Values["slug"] = slug;
    }

    [Test]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new ArticleViewComponent(null!, _routeService), Throws.ArgumentNullException);
            Assert.That(() => new ArticleViewComponent(_articleModel, null!), Throws.ArgumentNullException);
        });
    }

    // ─── Single ───────────────────────────────────────────────────────────────

    [Test]
    public async Task Single_OwnRouteWithSlug_RendersTheArticle()
    {
        var article = new ArticleViewModel { Title = "Post" };
        _articleModel.GetBySlugAsync("my-post", Arg.Any<CancellationToken>()).Returns(article);
        MatchedRoute(_itemId, WidgetRouteTokens, "my-post");

        var result = await _component.InvokeAsync(Single());

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("view.default"));
            Assert.That(ViewComponentHarness.Model(result), Is.SameAs(article));
        });
    }

    [Test]
    public async Task Single_SlugIsUrlDecoded()
    {
        _articleModel.GetBySlugAsync("caf%C3%A9", Arg.Any<CancellationToken>()).Returns((ArticleViewModel?)null);
        MatchedRoute(_itemId, WidgetRouteTokens, "caf%25C3%25A9");

        await _component.InvokeAsync(Single());

        await _articleModel.Received(1).GetBySlugAsync("caf%C3%A9", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Single_PlainPageRequest_IgnoresTheCatchAllSlug()
    {
        // The {**slug} catch-all puts the page path in "slug" on every CMS request.
        MatchedRoute(Guid.NewGuid(), "{}", "about");

        var result = await _component.InvokeAsync(Single());

        Assert.That(result, Is.InstanceOf<ContentViewComponentResult>());
        await _articleModel.DidNotReceive().GetBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Single_OwnRouteWithoutASlugValue_FallsBackToTheConfiguredArticle()
    {
        var articleId = Guid.NewGuid();
        var article = new ArticleViewModel { Title = "Configured" };
        _articleModel.GetViewModelAsync(articleId, Arg.Any<CancellationToken>()).Returns(article);
        MatchedRoute(_itemId, WidgetRouteTokens, slug: "");

        var result = await _component.InvokeAsync(Single(articleId));

        Assert.That(ViewComponentHarness.Model(result), Is.SameAs(article));
        await _articleModel.DidNotReceive().GetBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Single_UnknownSlug_FallsBackToTheConfiguredArticle()
    {
        var articleId = Guid.NewGuid();
        var article = new ArticleViewModel();
        _articleModel.GetViewModelAsync(articleId, Arg.Any<CancellationToken>()).Returns(article);
        MatchedRoute(_itemId, WidgetRouteTokens, "missing");

        var result = await _component.InvokeAsync(Single(articleId));

        Assert.That(ViewComponentHarness.Model(result), Is.SameAs(article));
    }

    [Test]
    public async Task Single_UnknownSlugAndNoArticle_RendersNotFound()
    {
        MatchedRoute(_itemId, WidgetRouteTokens, "missing");

        var result = await _component.InvokeAsync(Single());

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("_NotFound"));
    }

    [Test]
    public async Task Single_ConfiguredArticleNotVisible_RendersNotFound()
    {
        var result = await _component.InvokeAsync(Single(Guid.NewGuid()));

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("_NotFound"));
    }

    [Test]
    public async Task Single_NothingAskedFor_RendersNothing()
    {
        var empty = await _component.InvokeAsync(Single(Guid.Empty));
        var nullConfig = await _component.InvokeAsync(null!);

        Assert.Multiple(() =>
        {
            Assert.That(empty, Is.InstanceOf<ContentViewComponentResult>());
            Assert.That(nullConfig, Is.InstanceOf<ContentViewComponentResult>());
        });
    }

    [Test]
    public async Task Single_EditMode_RendersTheEditViewEvenWithoutAnArticle()
    {
        EditMode();

        var result = await _component.InvokeAsync(Single(Guid.NewGuid()));

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("edit.default"));
            Assert.That(ViewComponentHarness.Model(result), Is.InstanceOf<ArticleViewModel>());
        });
    }

    // ─── List ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task List_RendersTheListWithItsOptions()
    {
        var listId = Guid.NewGuid();
        var vm = new ArticleListViewModel { ArticleListId = listId };
        _articleModel.GetListAsync(listId, 3, ArticleSortOrder.Title, Arg.Any<CancellationToken>()).Returns(vm);
        MatchedRoute(Guid.NewGuid(), "{}", "blog");

        var result = await _component.InvokeAsync(List(listId, 3, ArticleSortOrder.Title));

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("list.default"));
            Assert.That(ViewComponentHarness.Model(result), Is.SameAs(vm));
        });
    }

    [Test]
    public async Task List_OnAnotherItemsWidgetRoute_Yields()
    {
        MatchedRoute(Guid.NewGuid(), WidgetRouteTokens, "my-post");

        var result = await _component.InvokeAsync(List(Guid.NewGuid()));

        Assert.That(result, Is.InstanceOf<ContentViewComponentResult>());
        await _articleModel.DidNotReceive().GetListAsync(Arg.Any<Guid>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task List_NoListOrListNotVisible_RendersNothing()
    {
        var noList = await _component.InvokeAsync(List());
        var emptyList = await _component.InvokeAsync(List(Guid.Empty));
        var hidden = await _component.InvokeAsync(List(Guid.NewGuid()));

        Assert.Multiple(() =>
        {
            Assert.That(noList, Is.InstanceOf<ContentViewComponentResult>());
            Assert.That(emptyList, Is.InstanceOf<ContentViewComponentResult>());
            Assert.That(hidden, Is.InstanceOf<ContentViewComponentResult>());
        });
    }

    [Test]
    public async Task List_EditMode_RendersEvenOnAWidgetRouteAndWithoutAList()
    {
        EditMode();
        MatchedRoute(Guid.NewGuid(), WidgetRouteTokens);

        var result = await _component.InvokeAsync(List());

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("edit.default"));
            Assert.That(ViewComponentHarness.Model(result), Is.InstanceOf<ArticleListViewModel>());
        });
    }

    // ─── routes ───────────────────────────────────────────────────────────────

    [Test]
    public void ComponentName_IsArticle()
    {
        IRoutableViewComponent routable = _component;

        Assert.That(routable.ComponentName, Is.EqualTo("Article"));
    }

    [Test]
    public async Task GenerateRoutesAsync_Single_RegistersAnInlineConstrainedSlugRoute()
    {
        IRoutableViewComponent routable = _component;
        var nodeId = Guid.NewGuid();

        var routes = await routable.GenerateRoutesAsync("/blog", nodeId, WidgetDisplayMode.Single, CancellationToken.None);

        var route = routes.Single();
        Assert.Multiple(() =>
        {
            Assert.That(route.Pattern, Is.EqualTo("{slug:regex(^[a-z0-9-]+$)}"));
            Assert.That(route.OwningContentNodeId, Is.EqualTo(nodeId));
            Assert.That(route.OwningContentType, Is.EqualTo("ArticleWidget"));
            Assert.That(route.Order, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GenerateRoutesAsync_List_OwnsTheSameDetailRoute()
    {
        IRoutableViewComponent routable = _component;

        var routes = await routable.GenerateRoutesAsync("/blog", Guid.NewGuid(), WidgetDisplayMode.List, CancellationToken.None);

        Assert.That(routes.Single().Pattern, Is.EqualTo("{slug:regex(^[a-z0-9-]+$)}"));
    }

    // ─── List: its own detail route ───────────────────────────────────────────

    [Test]
    public async Task List_OwnRoute_RendersTheArticleOfItsListAsASingleView()
    {
        var listId = Guid.NewGuid();
        var article = new ArticleViewModel { Title = "Post", ArticleListId = listId };
        _articleModel.GetBySlugAsync("my-post", Arg.Any<CancellationToken>()).Returns(article);
        MatchedRoute(_itemId, WidgetRouteTokens, "my-post", "/blog/{slug:regex(^[a-z0-9-]+$)}");
        var config = List(listId);
        config.ViewName = "fancy";
        config.Metadata.CssClass = "c";
        _widget.Hit("Article", WidgetRenderMode.View, "fancy", "view.fancy");

        var result = await _component.InvokeAsync(config);

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("view.fancy"));
            Assert.That(ViewComponentHarness.Model(result), Is.SameAs(article));
        });
        await _articleModel.DidNotReceive().GetListAsync(Arg.Any<Guid>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task List_OwnRoute_ArticleOfAnotherListOrUnknown_RendersNotFound()
    {
        _articleModel.GetBySlugAsync("other", Arg.Any<CancellationToken>()).Returns(new ArticleViewModel { ArticleListId = Guid.NewGuid() });
        MatchedRoute(_itemId, WidgetRouteTokens, "other");
        var otherList = await _component.InvokeAsync(List(Guid.NewGuid()));

        _component.ViewContext.RouteData.Values["slug"] = "missing";
        var unknown = await _component.InvokeAsync(List(Guid.NewGuid()));

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(otherList), Is.EqualTo("_NotFound"));
            Assert.That(ViewComponentHarness.ViewName(unknown), Is.EqualTo("_NotFound"));
        });
    }

    [Test]
    public async Task List_OwnRouteWithoutASlug_RendersTheList()
    {
        var listId = Guid.NewGuid();
        _articleModel.GetListAsync(listId, null, null, Arg.Any<CancellationToken>()).Returns(new ArticleListViewModel());
        MatchedRoute(_itemId, WidgetRouteTokens);

        var result = await _component.InvokeAsync(List(listId));

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("list.default"));
    }

    // ─── List: detail link base ───────────────────────────────────────────────

    private async Task<string?> DetailBaseFor(Action arrange)
    {
        var listId = Guid.NewGuid();
        _articleModel.GetListAsync(listId, null, null, Arg.Any<CancellationToken>()).Returns(_ => new ArticleListViewModel());
        arrange();
        var result = await _component.InvokeAsync(List(listId));
        return ((ArticleListViewModel)ViewComponentHarness.Model(result)!).DetailBaseUrl;
    }

    [Test]
    public async Task List_DetailBaseUrl_IsThePagesLiteralRoute()
    {
        Assert.Multiple(async () =>
        {
            Assert.That(await DetailBaseFor(() => MatchedRoute(Guid.NewGuid(), "{}", pattern: "/blog")), Is.EqualTo("/blog"));
            Assert.That(await DetailBaseFor(() => MatchedRoute(Guid.NewGuid(), "{}", pattern: "/code/{id}")), Is.EqualTo("/code"));
            Assert.That(await DetailBaseFor(() => MatchedRoute(Guid.NewGuid(), "{}", pattern: "/")), Is.EqualTo(""));
            Assert.That(await DetailBaseFor(() => _component.HttpContext.Items.Remove("CMS:RouteData")), Is.EqualTo(""));
        });
    }

    [Test]
    public async Task List_DetailBaseUrl_InPreview_IsThePublishedPageRoute()
    {
        var pageId = Guid.NewGuid();
        _routeService.GetByOwningContentAsync(pageId, Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { new() { Pattern = "/news" } });

        var baseUrl = await DetailBaseFor(() => MatchedRoute(pageId, "{}", pattern: $"/_preview/{pageId}"));

        Assert.That(baseUrl, Is.EqualTo("/news"));
    }

    [Test]
    public async Task List_DetailBaseUrl_InPreviewOfAnUnpublishedPage_IsNull()
    {
        _routeService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());

        Assert.Multiple(async () =>
        {
            Assert.That(await DetailBaseFor(() => MatchedRoute(Guid.NewGuid(), "{}", pattern: $"/_preview/{Guid.NewGuid()}")), Is.Null);
            Assert.That(await DetailBaseFor(() => MatchedRoute(null, "{}", pattern: $"/_preview/{Guid.NewGuid()}")), Is.Null);
        });
    }
}
