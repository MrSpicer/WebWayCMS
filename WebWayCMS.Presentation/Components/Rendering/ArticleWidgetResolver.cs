using WebWayCMS.Models.Article;

namespace WebWayCMS.Presentation.Rendering;

/// <inheritdoc />
public sealed class ArticleWidgetResolver : IArticleWidgetResolver
{
    private readonly IArticleListModel _listModel;
    private readonly IArticleModel _articleModel;

    public ArticleWidgetResolver(IArticleListModel listModel, IArticleModel articleModel)
    {
        _listModel = listModel ?? throw new ArgumentNullException(nameof(listModel));
        _articleModel = articleModel ?? throw new ArgumentNullException(nameof(articleModel));
    }

    public async Task<ArticleRenderModel> ResolveAsync(ArticleContentZoneConfiguration? config, string? subRoute, CancellationToken ct = default)
    {
        config ??= new ArticleContentZoneConfiguration();

        // 1. Admin upsert form mode stays on the MVC ViewComponent, not the public widget.
        if (config.UpsertModel != null)
            return ArticleRenderModel.None;

        // 2. Direct article object passed in (used by the list renderer per item).
        if (config.Article != null)
            return ArticleRenderModel.ForSingle(config.Article);

        // 3. Sub-route detail view via slug.
        if (!string.IsNullOrEmpty(subRoute))
        {
            var bySlug = await _articleModel.GetBySlugAsync(subRoute, ct);
            if (bySlug != null)
                return ArticleRenderModel.ForSingle(bySlug);
        }

        // 4. Explicit single mode with an article id.
        if (string.Equals(config.Mode, "Single", StringComparison.OrdinalIgnoreCase) && config.Id.HasValue && config.Id.Value != Guid.Empty)
            return ArticleRenderModel.ForSingle(await _articleModel.GetPostViewModelAsync(config.Id.Value, ct));

        // 5. Explicit list mode with an article list id.
        if (string.Equals(config.Mode, "List", StringComparison.OrdinalIgnoreCase) && config.ArticleListId.HasValue && config.ArticleListId.Value != Guid.Empty)
            return ArticleRenderModel.ForList(await _listModel.GetArticlesForListAsync(config.ArticleListId.Value, ct));

        // 6. Fallback: an id renders a single article; otherwise the full index list.
        if (config.Id.HasValue && config.Id.Value != Guid.Empty)
            return ArticleRenderModel.ForSingle(await _articleModel.GetPostViewModelAsync(config.Id.Value, ct));

        return ArticleRenderModel.ForList(await _listModel.GetIndexViewModelAsync(ct));
    }
}
