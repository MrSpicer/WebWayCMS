using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Interfaces;
using WebWayCMS.Models.Article;

namespace WebWayCMS.ViewComponents;

/// <summary>
/// List-capable, routable article widget. The item's display mode picks the behaviour:
///
/// <list type="bullet">
/// <item><b>Single</b> renders one article: the one named by the URL slug when the request matched this
/// item's own <c>{slug}</c> route, else the configured <see cref="ArticleContentZoneConfiguration.ArticleId"/>,
/// else nothing; <c>view.*</c> views.</item>
/// <item><b>List</b> renders the articles of the configured list (sorted, optionally capped); <c>list.*</c>
/// views. It owns the same <c>{slug}</c> route, so its cards' <c>{page}/{slug}</c> links work on their own:
/// on that route it renders the article (only one from its own list) with the <c>view.*</c> views. It yields
/// on another item's widget route.</item>
/// </list>
///
/// Articles are only visible while their list is (see <see cref="IArticleModel"/>).
/// </summary>
[ContentZoneComponent(
    DisplayName = "Article",
    Description = "Renders a single article (from the URL slug or the configured article) or a list of articles.",
    Category = "Content",
    ConfigurationType = typeof(ArticleContentZoneConfiguration),
    IconClass = "fa-newspaper",
    Order = 2,
    SupportsList = true
)]
public class ArticleViewComponent : WidgetViewComponent, IRoutableViewComponent
{
    private const string RouteDataItemKey = "CMS:RouteData";

    private readonly IArticleModel _articleModel;
    private readonly ICMSRouteService _routeService;

    public ArticleViewComponent(IArticleModel articleModel, ICMSRouteService routeService)
    {
        _articleModel = articleModel ?? throw new ArgumentNullException(nameof(articleModel));
        _routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
    }

    public async Task<IViewComponentResult> InvokeAsync(ArticleContentZoneConfiguration config)
    {
        config ??= new ArticleContentZoneConfiguration();

        return DisplayModeFor(config) == WidgetDisplayMode.List
            ? await InvokeListAsync(config)
            : await InvokeSingleAsync(config);
    }

    private async Task<IViewComponentResult> InvokeListAsync(ArticleContentZoneConfiguration config)
    {
        var ct = HttpContext.RequestAborted;
        var isEditMode = RenderModeFor(config) == WidgetRenderMode.Edit;

        if (!isEditMode)
        {
            // The list's own {slug} route (/blog/my-post): render that article in place of the list.
            if (IsOwnWidgetRoute(config) && RouteSlug() is { } slug)
                return await InvokeListDetailAsync(config, slug, ct);

            // Another item's widget route: that widget takes the page over.
            if (IsWidgetRouteRequest() && !IsOwnWidgetRoute(config))
                return Content(string.Empty);
        }

        ArticleListViewModel? vm = null;
        if (config.ArticleListId is { } listId && listId != Guid.Empty)
            vm = await _articleModel.GetListAsync(listId, config.MaxItems, config.SortOrder, ct);

        // No list chosen (or it isn't visible): the zone editor shows its placeholder; public pages render nothing.
        if (vm == null && !isEditMode)
            return Content(string.Empty);

        vm ??= new ArticleListViewModel();
        vm.DetailBaseUrl = await DetailBaseUrlAsync(ct);
        return ModeView(config, vm);
    }

    /// <summary>
    /// A list item's own route: the article named by the slug, if it belongs to this list. Rendered through
    /// a Single-mode copy of the configuration, so the mode is still derived (<c>view.*</c>/<c>admin.*</c>).
    /// </summary>
    private async Task<IViewComponentResult> InvokeListDetailAsync(ArticleContentZoneConfiguration config, string slug, CancellationToken ct)
    {
        var article = await _articleModel.GetBySlugAsync(slug, ct);
        if (article == null || article.ArticleListId != config.ArticleListId)
            return View("_NotFound");

        var single = new ArticleContentZoneConfiguration
        {
            ViewName = config.ViewName,
            ZoneItemNodeId = config.ZoneItemNodeId,
            Metadata = new WidgetMetadata
            {
                DisplayMode = WidgetDisplayMode.Single,
                AdminLabel = config.Metadata.AdminLabel,
                AdminNote = config.Metadata.AdminNote,
                CssClass = config.Metadata.CssClass,
                AnchorId = config.Metadata.AnchorId
            }
        };
        return ModeView(single, article);
    }

    /// <summary>
    /// The URL the list's cards link under (<c>{base}/{slug}</c>): the matched page route's literal prefix.
    /// In a draft preview (<c>/_preview/{pageId}</c>) links go to the published page instead; a page that has
    /// never been published has no URL, so null — the cards render without links.
    /// </summary>
    private async Task<string?> DetailBaseUrlAsync(CancellationToken ct)
    {
        if (HttpContext.Items[RouteDataItemKey] is not CMSRouteDTO route)
            return string.Empty;

        if (!route.Pattern.StartsWith(PreviewConstants.PathPrefix + "/", StringComparison.OrdinalIgnoreCase))
            return LiteralPrefix(route.Pattern);

        var pageRoutes = route.OwningContentNodeId is { } pageNodeId
            ? await _routeService.GetByOwningContentAsync(pageNodeId, ct)
            : [];
        return pageRoutes.Count > 0 ? LiteralPrefix(pageRoutes[0].Pattern) : null;
    }

    private static string LiteralPrefix(string pattern)
    {
        var braceIdx = pattern.IndexOf('{');
        return (braceIdx >= 0 ? pattern[..braceIdx] : pattern).TrimEnd('/');
    }

    /// <summary>The URL-decoded <c>slug</c> route value, or null when absent or empty.</summary>
    private string? RouteSlug()
        => ViewContext.RouteData.Values.TryGetValue("slug", out var slugObj) && slugObj is string slug && slug.Length > 0
            ? System.Net.WebUtility.UrlDecode(slug)
            : null;

    private async Task<IViewComponentResult> InvokeSingleAsync(ArticleContentZoneConfiguration config)
    {
        var ct = HttpContext.RequestAborted;
        var isEditMode = RenderModeFor(config) == WidgetRenderMode.Edit;

        ArticleViewModel? article = null;
        var attempted = false;

        // Only this item's own route addresses a slug to it; on a plain page request the "slug" route
        // value is the page path itself.
        if (IsOwnWidgetRoute(config) && RouteSlug() is { } slug)
        {
            attempted = true;
            article = await _articleModel.GetBySlugAsync(slug, ct);
        }

        if (article == null && config.ArticleId is { } articleId && articleId != Guid.Empty)
        {
            attempted = true;
            article = await _articleModel.GetViewModelAsync(articleId, ct);
        }

        if (article != null || isEditMode)
            return ModeView(config, article ?? new ArticleViewModel());

        // Nothing was asked for (no slug, no configured article): render nothing. "_NotFound" is reserved
        // for a lookup that was attempted and failed; the underscore keeps it out of the view picker.
        return attempted ? View("_NotFound") : Content(string.Empty);
    }

    string IRoutableViewComponent.ComponentName => "Article";

    Task<IReadOnlyList<CMSRouteDTO>> IRoutableViewComponent.GenerateRoutesAsync(
        string parentRoute, Guid contentZoneItemNodeId, WidgetDisplayMode displayMode, CancellationToken ct)
    {
        // Both modes own the detail route: a list's cards link to {page}/{slug}, and the list renders the
        // article there itself, so a lone List widget needs no Single widget beside it. (A List and a Single
        // Article on one page would therefore claim the same pattern; the page publish reports the collision.)
        //
        // The constraint must be inline in the pattern — CMSRouteService.TryMatchPattern parses it from the
        // pattern text and never reads ConstraintsJson. Lowercase only: RouteRegistrationService lowercases
        // the whole pattern, and stored slugs are normalized to [a-z0-9-].
        var route = new CMSRouteDTO
        {
            Pattern = "{slug:regex(^[a-z0-9-]+$)}",
            OwningContentNodeId = contentZoneItemNodeId,
            OwningContentType = "ArticleWidget",
            Order = 1
        };

        return Task.FromResult<IReadOnlyList<CMSRouteDTO>>([route]);
    }
}
