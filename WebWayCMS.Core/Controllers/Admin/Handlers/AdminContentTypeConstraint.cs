using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace WebWayCMS.Controllers.Admin.Handlers;

/// <summary>
/// Route constraint (<c>admincontenttype</c>) on <c>AdminContentController</c>'s <c>{contentType}</c> segment:
/// an incoming request matches only when an <see cref="IAdminHandlerRegistry"/> handler is registered under
/// that key. Without it the controller's <c>wadmin/{contentType}…</c> routes outrank the CMS <c>{**slug}</c>
/// catch-all and answer 404 for any CMS page one segment under <c>/wadmin</c> (e.g. the seeded
/// <c>/wadmin/users</c>); with it an unknown segment falls through to the CMS page routing. URL generation
/// is never constrained (callers only link to real content types). The keys come from the singleton
/// <see cref="AdminContentTypeKeys"/> (registered in admin mode), so a request builds no handlers; without it
/// the scoped registry is asked directly.
/// </summary>
public sealed class AdminContentTypeConstraint : IRouteConstraint
{
    /// <summary>The constraint's name in <c>RouteOptions.ConstraintMap</c>.</summary>
    public const string Name = "admincontenttype";

    public bool Match(HttpContext? httpContext, IRouter? route, string routeKey,
        RouteValueDictionary values, RouteDirection routeDirection)
    {
        if (routeDirection == RouteDirection.UrlGeneration)
            return true;

        if (httpContext is null
            || !values.TryGetValue(routeKey, out var value)
            || value is not string contentType
            || string.IsNullOrWhiteSpace(contentType))
            return false;

        var services = httpContext.RequestServices;
        if (services.GetService<AdminContentTypeKeys>() is { } keys)
            return keys.Contains(contentType, services);

        var registry = services.GetService<IAdminHandlerRegistry>();
        return registry?.GetHandler(contentType) is not null;
    }
}
