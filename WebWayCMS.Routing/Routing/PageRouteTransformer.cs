using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;

using WebWayCMS.Data.Services;
using WebWayCMS.Pages;

namespace WebWayCMS.Routing;

public class PageRouteTransformer : DynamicRouteValueTransformer
{
    /// <summary>
    /// <see cref="HttpContext.Items"/> key under which the unmatched trailing
    /// portion of the request path (the "sub-route") is stored for content
    /// components to consume.
    /// </summary>
    public const string SubRouteItemKey = "CMS:SubRoute";

    private readonly IPageService _pageService;
    private readonly IPageControllerRegistry _registry;
    private readonly IEnumerable<ISubRouteContent> _subRouteResolvers;

    public PageRouteTransformer(
        IPageService pageService,
        IPageControllerRegistry registry,
        IEnumerable<ISubRouteContent> subRouteResolvers)
    {
        _pageService = pageService ?? throw new ArgumentNullException(nameof(pageService));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _subRouteResolvers = subRouteResolvers ?? throw new ArgumentNullException(nameof(subRouteResolvers));
    }

    public override async ValueTask<RouteValueDictionary> TransformAsync(
        HttpContext httpContext, RouteValueDictionary values)
    {
        var path = httpContext.Request.Path.Value;

        // Normalize: an empty/unset path is the root, lowercase, trim trailing slash (keep root)
        if (string.IsNullOrEmpty(path))
            path = "/";
        path = path.ToLowerInvariant();
        if (path.Length > 1 && path.EndsWith('/'))
            path = path.TrimEnd('/');

        // Try exact match first
        var page = await _pageService.GetByRouteAsync(path);
        string? subRoute = null;

        // If no exact match, try progressively shorter paths for sub-route matching.
        // A page only acts as a sub-route parent for paths nested beneath its own
        // route; the root page ("/") is deliberately NOT treated as a catch-all
        // parent, so unmatched top-level paths fall through to a 404 instead of
        // silently rendering the home page.
        //
        // A parent page only serves the path when some registered ISubRouteContent
        // resolver can actually resolve the trailing sub-route (e.g. an article slug).
        // Otherwise the path belongs to no page -- it may be a controller route such
        // as "/admin/page" or simply not exist -- so we leave 'page' null and let the
        // request fall through to the controller route table (and ultimately a 404).
        if (page == null && path != "/")
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = segments.Length - 1; i >= 1; i--)
            {
                var parentPath = "/" + string.Join('/', segments[..i]);
                var parent = await _pageService.GetByRouteAsync(parentPath);
                if (parent == null)
                    continue;

                var candidateSubRoute = string.Join('/', segments[i..]);
                if (await CanResolveSubRouteAsync(candidateSubRoute, httpContext.RequestAborted))
                {
                    page = parent;
                    subRoute = candidateSubRoute;
                }
                break;
            }
        }

        if (page == null)
            return null!;

        var controllerInfo = _registry.GetByName(page.ControllerName);
        if (controllerInfo == null)
            return null!;

        // Store page data and deserialized config in HttpContext.Items
        httpContext.Items["CMS:PageData"] = page;

        if (subRoute != null)
            httpContext.Items[SubRouteItemKey] = subRoute;

        if (controllerInfo.ConfigurationType != null && !string.IsNullOrWhiteSpace(page.ConfigurationJson))
        {
            try
            {
                var config = JsonSerializer.Deserialize(page.ConfigurationJson, controllerInfo.ConfigurationType,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                httpContext.Items["CMS:PageConfig"] = config;
            }
            catch
            {
                // If deserialization fails, create a default instance
                httpContext.Items["CMS:PageConfig"] = Activator.CreateInstance(controllerInfo.ConfigurationType);
            }
        }

        return new RouteValueDictionary
        {
            { "controller", page.ControllerName },
            { "action", "Index" }
        };
    }

    /// <summary>
    /// Returns <c>true</c> when at least one registered <see cref="ISubRouteContent"/>
    /// resolver can serve the supplied sub-route.
    /// </summary>
    private async Task<bool> CanResolveSubRouteAsync(string subRoute, CancellationToken ct)
    {
        foreach (var resolver in _subRouteResolvers)
        {
            if (await resolver.CanResolveSubRouteAsync(subRoute, ct))
                return true;
        }

        return false;
    }
}