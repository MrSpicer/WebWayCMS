using WebWayCMS.Data.Models;
using WebWayCMS.Models.Article;

namespace WebWayCMS.Presentation.Rendering;

/// <summary>
/// Builds the slug-based detail link for an article from the current page route, mirroring the
/// logic in Article/Article.cshtml. Extracted so the null/fallback branches are unit-testable.
/// </summary>
public static class ArticleDetailLink
{
    public static string Build(PageDTO? page, ArticleViewModel article)
    {
        var pageRoute = page?.Route?.TrimEnd('/') ?? string.Empty;
        var slug = (article.Slug ?? article.Id?.ToString() ?? string.Empty).TrimStart('/');
        return $"{pageRoute}/{slug}";
    }
}
