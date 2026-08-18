using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;
using WebWayCMS.Data.Services;

namespace WebWayCMS.ViewComponents;

[ContentZoneComponent(
    DisplayName = "Route Navigation",
    Description = "Renders links for every active, non-parameterized CMS route.",
    Category = "Navigation",
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

    public IViewComponentResult Invoke()
    {
        var patterns = _routeRegistry.GetActiveRoutes()
            .Where(r => !r.Pattern.Contains('{'))
            .Select(r => r.Pattern)
            .ToList();
        return View(patterns);
    }
}
