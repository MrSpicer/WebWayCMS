using System.Text.Json;

using Microsoft.AspNetCore.Mvc.ViewComponents;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Interfaces;
using WebWayCMS.Models.Article;
using WebWayCMS.ViewComponents;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class ArticleViewComponentTests
{
    private IArticleListModel _listModel = null!;
    private IArticleModel _articleModel = null!;
    private ArticleViewComponent _component = null!;
    private Microsoft.AspNetCore.Http.DefaultHttpContext _http = null!;

    [SetUp]
    public void SetUp()
    {
        _listModel = Substitute.For<IArticleListModel>();
        _articleModel = Substitute.For<IArticleModel>();
        _component = new ArticleViewComponent(_listModel, _articleModel);
        _http = ViewComponentHarness.Attach(_component);
    }

    [Test]
    public async Task UpsertModelMode_RendersUpsertForm()
    {
        var config = new ArticleContentZoneConfiguration { UpsertModel = new ArticleUpsertViewModel() };

        var result = await _component.InvokeAsync(config);

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("UpsertForm"));
    }

    [Test]
    public async Task DirectArticle_RendersArticleWithDefaultOrCustomView()
    {
        Assert.That(ViewComponentHarness.ViewName(await _component.InvokeAsync(
            new ArticleContentZoneConfiguration { Article = new ArticleViewModel() })), Is.EqualTo("Article"));

        Assert.That(ViewComponentHarness.ViewName(await _component.InvokeAsync(
            new ArticleContentZoneConfiguration { Article = new ArticleViewModel(), ViewName = "Custom" })), Is.EqualTo("Custom"));
    }

    [Test]
    public async Task SubRoute_RendersArticleWhenFound()
    {
        _component.ViewComponentContext!.ViewContext.RouteData.Values["slug"] = "my-slug";
        _articleModel.GetBySlugAsync("my-slug", Arg.Any<CancellationToken>()).Returns(new ArticleViewModel());

        var result = await _component.InvokeAsync(new ArticleContentZoneConfiguration());

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Article"));
    }

    [Test]
    public async Task SubRoute_NotFound_FallsThroughToList()
    {
        _component.ViewComponentContext!.ViewContext.RouteData.Values["slug"] = "missing";
        _articleModel.GetBySlugAsync("missing", Arg.Any<CancellationToken>()).Returns((ArticleViewModel?)null);
        _listModel.GetIndexViewModelAsync(Arg.Any<CancellationToken>()).Returns(new ArticleListViewModel());

        var result = await _component.InvokeAsync(new ArticleContentZoneConfiguration());

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("List"));
    }

    [Test]
    public async Task SingleMode_RendersArticle()
    {
        _articleModel.GetPostViewModelAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new ArticleViewModel());

        var result = await _component.InvokeAsync(new ArticleContentZoneConfiguration { Mode = "single", Id = Guid.NewGuid() });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Article"));
    }

    [Test]
    public async Task ListMode_RendersList()
    {
        _listModel.GetArticlesForListAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new ArticleListViewModel());

        var result = await _component.InvokeAsync(new ArticleContentZoneConfiguration { Mode = "list", ArticleListId = Guid.NewGuid() });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("List"));
        await _listModel.Received(1).GetArticlesForListAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Fallback_IdSet_RendersSingleArticle()
    {
        _articleModel.GetPostViewModelAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new ArticleViewModel());

        var result = await _component.InvokeAsync(new ArticleContentZoneConfiguration { Id = Guid.NewGuid() });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Article"));
    }

    [Test]
    public async Task Fallback_NullConfig_RendersFullList()
    {
        _listModel.GetIndexViewModelAsync(Arg.Any<CancellationToken>()).Returns(new ArticleListViewModel());

        var result = await _component.InvokeAsync(null!);

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("List"));
    }

    [Test]
    public void ExplicitInterface_ComponentName_ReturnsArticle()
    {
        IRoutableViewComponent routable = _component;

        Assert.That(routable.ComponentName, Is.EqualTo("Article"));
    }

    [Test]
    public async Task ExplicitInterface_GenerateRoutesAsync_ReturnsRouteWithExpectedProperties()
    {
        IRoutableViewComponent routable = _component;
        var nodeId = Guid.NewGuid();

        var routes = await routable.GenerateRoutesAsync("/blog", nodeId, CancellationToken.None);

        Assert.That(routes, Has.Count.EqualTo(1));
        var route = routes[0];

        Assert.That(route.Pattern, Is.EqualTo("{slug}"));

        using var constraints = JsonDocument.Parse(route.ConstraintsJson);
        Assert.That(constraints.RootElement.GetProperty("slug").GetString(), Is.EqualTo("regex(.+)"));

        using var defaults = JsonDocument.Parse(route.DefaultsJson);
        Assert.That(defaults.RootElement.GetProperty("_widget").GetString(), Is.EqualTo("Article"));

        Assert.That(route.OwningContentNodeId, Is.EqualTo(nodeId));
        Assert.That(route.OwningContentType, Is.EqualTo("ArticleWidget"));
        Assert.That(route.Order, Is.EqualTo(1));
    }
}