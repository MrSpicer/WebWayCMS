using WebWayCMS.Models.Article;

namespace WebWayCMS.Presentation.Rendering;

/// <summary>What an <c>ArticleWidget</c> should render once its configuration is resolved.</summary>
public enum ArticleRenderKind
{
    None,
    Single,
    List,
}

/// <summary>
/// The resolved rendering instruction for the article widget: a single article, a list of
/// articles, or nothing.
/// </summary>
public sealed record ArticleRenderModel(ArticleRenderKind Kind, ArticleViewModel? Article, ArticleListViewModel? List)
{
    public static readonly ArticleRenderModel None = new(ArticleRenderKind.None, null, null);

    public static ArticleRenderModel ForSingle(ArticleViewModel? article) => new(ArticleRenderKind.Single, article, null);

    public static ArticleRenderModel ForList(ArticleListViewModel? list) => new(ArticleRenderKind.List, null, list);
}
