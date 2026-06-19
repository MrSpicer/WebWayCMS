using WebWayCMS.Routing;

namespace WebWayCMS.Models.Article;

/// <summary>
/// Resolves a dynamic page sub-route to an article by slug. Lets a page hosting an
/// Article content zone serve detail views such as <c>/blog/my-post</c>, and lets
/// <see cref="PageRouteTransformer"/> 404 sub-routes whose slug does not exist.
/// </summary>
public class ArticleSubRouteResolver : ISubRouteContent
{
    private readonly IArticleModel _articleModel;

    public ArticleSubRouteResolver(IArticleModel articleModel)
    {
        _articleModel = articleModel ?? throw new ArgumentNullException(nameof(articleModel));
    }

    public async Task<bool> CanResolveSubRouteAsync(string subRoute, CancellationToken ct = default)
    {
        return await _articleModel.GetBySlugAsync(subRoute, ct) != null;
    }
}
