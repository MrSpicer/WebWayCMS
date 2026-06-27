using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Models.Article;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class ArticleSubRouteResolverTests
{
    private IArticleModel _articleModel = null!;
    private ArticleSubRouteResolver _resolver = null!;

    [SetUp]
    public void SetUp()
    {
        _articleModel = Substitute.For<IArticleModel>();
        _resolver = new ArticleSubRouteResolver(_articleModel);
    }

    [Test]
    public void Constructor_NullArticleModel_Throws()
    {
        Assert.That(() => new ArticleSubRouteResolver(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task CanResolveSubRouteAsync_SlugMatchesArticle_ReturnsTrue()
    {
        _articleModel.GetBySlugAsync("my-post", Arg.Any<CancellationToken>())
            .Returns(new ArticleViewModel());

        var result = await _resolver.CanResolveSubRouteAsync("my-post");

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task CanResolveSubRouteAsync_NoMatchingArticle_ReturnsFalse()
    {
        _articleModel.GetBySlugAsync("missing", Arg.Any<CancellationToken>())
            .Returns((ArticleViewModel?)null);

        var result = await _resolver.CanResolveSubRouteAsync("missing");

        Assert.That(result, Is.False);
    }
}