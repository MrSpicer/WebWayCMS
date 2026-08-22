using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.CMSRoute;

namespace WebWayCMS.ViewComponents;

[ContentZoneComponent(
    DisplayName = "Route Navigation",
    Description = "Renders links for every active, non-parameterized CMS route.",
    Category = "Navigation",
    ConfigurationType = typeof(RouteNavigationConfiguration),
    IconClass = "fa-route",
    Order = 20
)]
public class RouteNavigationViewComponent : ViewComponent
{
    private readonly ICMSRouteRegistry _routeRegistry;

    public RouteNavigationViewComponent(ICMSRouteRegistry routeRegistry)
    {
        _routeRegistry = routeRegistry ?? throw new ArgumentNullException(nameof(routeRegistry));
    }

    public IViewComponentResult Invoke(RouteNavigationConfiguration? config = null)
    {
        config ??= new RouteNavigationConfiguration();

        var routes = _routeRegistry.GetActiveRoutes()
            .Where(r => !r.Pattern.Contains('{'))
            .Where(r => !string.IsNullOrWhiteSpace(r.NavigationName))
            .Where(r => config.IncludeReserved || !r.IsReserved);

        routes = config.AdminRoutes
            ? routes.Where(r => AdminPathPrefix.Matches(r.Pattern))
            : routes.Where(r => !AdminPathPrefix.Matches(r.Pattern));

        var items = BuildTree(routes);
        var viewName = string.IsNullOrWhiteSpace(config.ViewName) ? "Default" : config.ViewName;
        return View(viewName, new RouteNavigationViewModel { Items = items });
    }

    /// <summary>
    /// Nests the surviving routes by path segment: a route becomes a child of its nearest
    /// surviving ancestor pattern, or a root item when it has none. Filtering happens before
    /// nesting, so a filtered-out parent's children rise to the top level.
    /// </summary>
    private static List<RouteNavigationItem> BuildTree(IEnumerable<CMSRouteDTO> routes)
    {
        var byPattern = new Dictionary<string, RouteNavigationItem>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        foreach (var route in routes)
        {
            if (byPattern.ContainsKey(route.Pattern))
                continue;

            byPattern[route.Pattern] = new RouteNavigationItem
            {
                Title = route.NavigationName!,
                Path = route.Pattern
            };
            ordered.Add(route.Pattern);
        }

        var roots = new List<RouteNavigationItem>();
        foreach (var pattern in ordered)
        {
            var parent = FindParent(pattern, byPattern);
            if (parent != null)
                parent.Children.Add(byPattern[pattern]);
            else
                roots.Add(byPattern[pattern]);
        }

        return roots;
    }

    /// <summary>
    /// Walks a pattern's ancestors, shortest trim first, until one is present in the set.
    /// The site root ("/") is never a parent, so it does not swallow every other link.
    /// </summary>
    private static RouteNavigationItem? FindParent(string pattern, Dictionary<string, RouteNavigationItem> byPattern)
    {
        var candidate = pattern.TrimEnd('/');
        while (true)
        {
            var lastSlash = candidate.LastIndexOf('/');
            if (lastSlash <= 0)
                return null;

            candidate = candidate[..lastSlash];
            if (byPattern.TryGetValue(candidate, out var parent))
                return parent;
        }
    }
}
