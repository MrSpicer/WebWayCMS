using Microsoft.AspNetCore.Mvc;
using WebWayCMS.Attributes;
using WebWayCMS.Models.Article;

namespace WebWayCMS.ViewComponents;

[ContentZoneComponent(
    DisplayName = "Article",
    Description = "Displays blog articles - either a list or a single post.",
    Category = "Content",
    ConfigurationType = typeof(ArticleContentZoneConfiguration),
    IconClass = "fa-newspaper",
    Order = 2
)]
public class ArticleViewComponent : ViewComponent
{
    private readonly IArticleListModel _listModel;
    private readonly IArticleModel _articleModel;

    public ArticleViewComponent(IArticleListModel listModel, IArticleModel articleModel)
    {
        _listModel = listModel;
        _articleModel = articleModel;
    }

    public async Task<IViewComponentResult> InvokeAsync(ArticleContentZoneConfiguration config)
    {
        config ??= new ArticleContentZoneConfiguration();

        // 1. Admin upsert form mode
        if (config.UpsertModel != null)
        {
            return View("UpsertForm", config.UpsertModel);
        }

        // 2. Direct article object passed in
        if (config.Article != null)
        {
            return View(config.ViewName ?? "Article", config.Article);
        }

        // 3. Check for sub-route in HttpContext (detail view via slug)
        if (HttpContext.Items.TryGetValue("CMS:SubRoute", out var subRouteObj) && subRouteObj is string subRoute && !string.IsNullOrEmpty(subRoute))
        {
            var article = await _articleModel.GetBySlugAsync(subRoute);
            if (article != null)
            {
                return View("Article", article);
            }
        }

        // 4. Single mode with explicit article ID
        if (string.Equals(config.Mode, "Single", StringComparison.OrdinalIgnoreCase) && config.Id.HasValue && config.Id.Value != Guid.Empty)
        {
            var loadedArticle = await _articleModel.GetPostViewModelAsync(config.Id.Value);
            return View(config.ViewName ?? "Article", loadedArticle);
        }

        // 5. List mode with ArticleListId
        if (string.Equals(config.Mode, "List", StringComparison.OrdinalIgnoreCase) && config.ArticleListId.HasValue && config.ArticleListId.Value != Guid.Empty)
        {
            var listVm = await _listModel.GetArticlesForListAsync(config.ArticleListId.Value);
            return View(config.ViewName ?? "List", listVm);
        }

        // 6. Fallback: ID set -> single (legacy), else -> full list (legacy)
        if (config.Id.HasValue && config.Id.Value != Guid.Empty)
        {
            var loadedArticle = await _articleModel.GetPostViewModelAsync(config.Id.Value);
            return View(config.ViewName ?? "Article", loadedArticle);
        }

        var vm = await _listModel.GetIndexViewModelAsync(CancellationToken.None);
        return View(config.ViewName ?? "List", vm);
    }
}
