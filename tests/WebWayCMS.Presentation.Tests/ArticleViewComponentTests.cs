using Microsoft.AspNetCore.Mvc.ViewComponents;

using NSubstitute;

using NUnit.Framework;

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
        _http.Items["CMS:SubRoute"] = "my-slug";
        _articleModel.GetBySlugAsync("my-slug", Arg.Any<CancellationToken>()).Returns(new ArticleViewModel());

        var result = await _component.InvokeAsync(new ArticleContentZoneConfiguration());

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Article"));
    }

    [Test]
    public async Task SubRoute_NotFound_FallsThroughToList()
    {
        _http.Items["CMS:SubRoute"] = "missing";
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
        _listModel.GetArticlesForListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new ArticleListViewModel());

        var result = await _component.InvokeAsync(new ArticleContentZoneConfiguration { Mode = "list", ArticleListId = Guid.NewGuid() });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("List"));
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
}