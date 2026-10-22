using System.Reflection;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.Extensions.DependencyInjection;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Data.Models;
using WebWayCMS.Services;

namespace WebWayCMS.ViewComponents;

/// <summary>
/// Base class for content-zone widgets that participate in render modes. Derived widgets keep
/// their own constructors (the <see cref="IWidgetViewResolver"/> is resolved from the request
/// services on first use, so no constructor plumbing is needed) and call
/// <see cref="ModeView{TModel}"/> instead of <c>View(...)</c> to render:
///
/// <list type="number">
/// <item>The render mode is derived, never chosen by the widget: <see cref="WidgetRenderMode.Edit"/>
/// when the zone is in edit mode (the zone's <c>ContentZone:RenderMode</c> ViewData); otherwise
/// <see cref="WidgetRenderMode.Admin"/>/<see cref="WidgetRenderMode.AdminList"/> on an admin page
/// (request path under <c>/wadmin</c>) and <see cref="WidgetRenderMode.View"/>/<see cref="WidgetRenderMode.List"/>
/// elsewhere, single vs. list following the item's <see cref="WidgetMetadata.DisplayMode"/>.</item>
/// <item>The mode's view is resolved by the <see cref="IWidgetViewResolver"/> (chosen variant →
/// mode default → mode fallback → generic card/nothing, per mode).</item>
/// </list>
///
/// A list-capable widget (<c>[ContentZoneComponent(SupportsList = true)]</c>) calls
/// <see cref="DisplayModeFor"/> to decide whether to load one item or a collection.
/// </summary>
public abstract class WidgetViewComponent : ViewComponent
{
    private const string RenderModeViewDataKey = "ContentZone:RenderMode";
    private const string ParentItemViewDataKey = "ContentZone:ParentItemId";
    private const string RouteDataItemKey = "CMS:RouteData";
    private const string ParentPageNodeIdToken = "ParentPageNodeId";

    private IWidgetViewResolver? _viewResolver;

    /// <summary>
    /// The widget's component name, computed the way MVC names it: <c>[ViewComponent(Name = ...)]</c>
    /// when present, else the type name minus "ViewComponent" ("LayoutViewComponent" -> "Layout").
    /// Matches the seeded widget registration and the view directory name.
    /// </summary>
    protected string ComponentName => ViewComponentConventions.GetComponentName(GetType().GetTypeInfo());

    /// <summary>
    /// Resolves the view for the current render mode and returns a <see cref="ViewViewComponentResult"/>
    /// for it (or the generic edit card / empty content, per the resolver's outcome).
    /// </summary>
    /// <param name="config">
    /// The widget configuration (carries the view pickers + attached metadata); null for
    /// widgets with no configuration type.
    /// </param>
    /// <param name="model">The model to pass to the resolved view.</param>
    protected IViewComponentResult ModeView<TModel>(WidgetConfiguration? config, TModel model)
    {
        // Nested zones in the rendered view (e.g. a Layout's columns) are owned by this zone item. Set
        // here rather than relying on each ContentZone view, so a host override of those views can't
        // drop it.
        if (config?.ZoneItemNodeId is Guid itemNodeId)
            ViewData[ParentItemViewDataKey] = itemNodeId;

        var mode = GetContextMode(config);
        var resolution = ViewResolver.Resolve(ComponentName, mode, config);

        return resolution.Kind switch
        {
            WidgetViewResolutionKind.EditCard => View("~/Views/Shared/_WidgetEditCard.cshtml", EditCardInfo),
            WidgetViewResolutionKind.Empty => Content(string.Empty),
            _ => View(resolution.ViewName!, model)
        };
    }

    /// <summary>
    /// The render mode this widget is rendering in (see <see cref="GetContextMode"/>), for widgets
    /// that branch before calling <see cref="ModeView{TModel}"/> — e.g. to skip a public-only
    /// "not found" state in the zone editor.
    /// </summary>
    protected WidgetRenderMode RenderModeFor(WidgetConfiguration? config) => GetContextMode(config);

    /// <summary>
    /// True when the request matched a route owned by this zone item (one it registered through
    /// <c>IRoutableViewComponent</c>), i.e. the URL's route values — such as <c>slug</c> — are addressed to
    /// this widget. Every CMS request carries a <c>slug</c> value from the host's <c>{**slug}</c> catch-all
    /// (the whole path on a plain page request), so a widget must not read one without this check.
    /// </summary>
    protected bool IsOwnWidgetRoute(WidgetConfiguration? config)
        => config?.ZoneItemNodeId is Guid itemNodeId
           && MatchedRoute?.OwningContentNodeId == itemNodeId;

    /// <summary>
    /// True when the request matched a widget route (any zone item's, on the page being rendered) rather
    /// than the page's own route — e.g. <c>/blog/my-post</c> rather than <c>/blog</c>. A list widget
    /// yields on such a request so the single-item widget that owns the route takes the page over.
    /// </summary>
    protected bool IsWidgetRouteRequest()
    {
        var tokensJson = MatchedRoute?.DataTokensJson;
        if (string.IsNullOrWhiteSpace(tokensJson))
            return false;

        try
        {
            var tokens = JsonSerializer.Deserialize<Dictionary<string, string>>(tokensJson);
            return tokens?.ContainsKey(ParentPageNodeIdToken) == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>The CMS route the request matched (stashed by the route transformer), if any.</summary>
    private CMSRouteDTO? MatchedRoute => HttpContext.Items[RouteDataItemKey] as CMSRouteDTO;

    /// <summary>
    /// The item's display mode (single vs. list), from the metadata the zone attached to
    /// <paramref name="config"/>; <see cref="WidgetDisplayMode.Single"/> when there is none.
    /// </summary>
    protected static WidgetDisplayMode DisplayModeFor(WidgetConfiguration? config)
        => config?.Metadata.DisplayMode == WidgetDisplayMode.List ? WidgetDisplayMode.List : WidgetDisplayMode.Single;

    /// <summary>
    /// The widget registration info passed to the generic edit card (DisplayName, IconClass,
    /// Description). Null when the widget is not registered in the database.
    /// </summary>
    private WidgetRegistrationInfo? EditCardInfo
    {
        get
        {
            var registry = HttpContext.RequestServices.GetService<IWidgetRegistry>();
            return registry?.GetByName(ComponentName);
        }
    }

    private IWidgetViewResolver ViewResolver
        => _viewResolver ??= HttpContext.RequestServices.GetRequiredService<IWidgetViewResolver>();

    /// <summary>
    /// Computes the active render mode: <see cref="WidgetRenderMode.Edit"/> when the zone is in edit
    /// mode; otherwise admin vs. public from the request path, and single vs. list from the item's
    /// display mode.
    /// </summary>
    internal WidgetRenderMode GetContextMode(WidgetConfiguration? config)
    {
        if (ViewComponentContext.ViewData[RenderModeViewDataKey] is WidgetRenderMode.Edit)
            return WidgetRenderMode.Edit;

        var isAdmin = AdminPathPrefix.Matches(HttpContext.Request.Path.ToString());
        var isList = DisplayModeFor(config) == WidgetDisplayMode.List;

        return (isAdmin, isList) switch
        {
            (true, true) => WidgetRenderMode.AdminList,
            (true, false) => WidgetRenderMode.Admin,
            (false, true) => WidgetRenderMode.List,
            _ => WidgetRenderMode.View
        };
    }
}
