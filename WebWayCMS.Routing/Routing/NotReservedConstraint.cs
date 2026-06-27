using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace WebWayCMS.Routing;

/// <summary>
/// Route constraint that prevents reserved words from matching the {parentKey} segment,
/// avoiding conflicts with literal action route segments like "edit", "delete", "api", "registry".
/// </summary>
public class NotReservedConstraint : IRouteConstraint
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "edit", "delete", "create", "registry", "api", "reorder", "versions"
    };

    public bool Match(HttpContext? httpContext, IRouter? route, string routeKey,
        RouteValueDictionary values, RouteDirection routeDirection)
    {
        return values.TryGetValue(routeKey, out var val)
            && val is string s
            && !Reserved.Contains(s);
    }
}