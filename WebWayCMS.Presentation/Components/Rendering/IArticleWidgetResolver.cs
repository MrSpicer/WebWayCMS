using WebWayCMS.Models.Article;

namespace WebWayCMS.Presentation.Rendering;

/// <summary>
/// Resolves what an article widget should render from its configuration and the current
/// sub-route, mirroring the display modes of the legacy ArticleViewComponent. Kept as a service
/// so the mode selection and data access are unit-testable without rendering a component.
/// </summary>
public interface IArticleWidgetResolver
{
    Task<ArticleRenderModel> ResolveAsync(ArticleContentZoneConfiguration? config, string? subRoute, CancellationToken ct = default);
}
