using System.Text.Json;

using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;
using WebWayCMS.Data.Models;
using WebWayCMS.Interfaces;
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
public class ArticleViewComponent : ViewComponent, IRoutableViewComponent
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

        if (config.UpsertModel != null)
        {
            return View("UpsertForm", config.UpsertModel);
        }

        if (config.Article != null)
        {
            return View(config.ViewName ?? "Article", config.Article);
        }

        if (ViewContext.RouteData.Values.TryGetValue("slug", out var slugObj)
            && slugObj is string slug && !string.IsNullOrEmpty(slug))
        {
            var decodedSlug = System.Net.WebUtility.UrlDecode(slug);
            var article = await _articleModel.GetBySlugAsync(decodedSlug)
                ?? await _articleModel.GetBySlugAsync(slug);
            if (article != null)
            {
                return View("Article", article);
            }
        }

        if (string.Equals(config.Mode, "Single", StringComparison.OrdinalIgnoreCase)
            && config.Id.HasValue && config.Id.Value != Guid.Empty)
        {
            var loadedArticle = await _articleModel.GetPostViewModelAsync(config.Id.Value);
            return View(config.ViewName ?? "Article", loadedArticle);
        }

        if (string.Equals(config.Mode, "List", StringComparison.OrdinalIgnoreCase)
            && config.ArticleListId.HasValue && config.ArticleListId.Value != Guid.Empty)
        {
            var listVm = await _listModel.GetArticlesForListAsync(config.ArticleListId.Value);
            return View(config.ViewName ?? "List", listVm);
        }

        if (config.Id.HasValue && config.Id.Value != Guid.Empty)
        {
            var loadedArticle = await _articleModel.GetPostViewModelAsync(config.Id.Value);
            return View(config.ViewName ?? "Article", loadedArticle);
        }

        var vm = await _listModel.GetIndexViewModelAsync(CancellationToken.None);
        return View(config.ViewName ?? "List", vm);
    }

    string IRoutableViewComponent.ComponentName => "Article";

    Task<IReadOnlyList<CMSRouteDTO>> IRoutableViewComponent.GenerateRoutesAsync(
        string parentRoute, Guid contentZoneItemMasterId, CancellationToken ct)
    {
        var route = new CMSRouteDTO
        {
            Pattern = "{slug}",
            ConstraintsJson = JsonSerializer.Serialize(
                new Dictionary<string, string>
                {
                    { "slug", "regex(.+)" }
                }),
            DefaultsJson = JsonSerializer.Serialize(
                new Dictionary<string, string>
                {
                    { "_widget", "Article" }
                }),
            OwningContentMasterId = contentZoneItemMasterId,
            OwningContentType = "ArticleWidget",
            Order = 1
        };

        return Task.FromResult<IReadOnlyList<CMSRouteDTO>>(new List<CMSRouteDTO> { route }.AsReadOnly());
    }
}
