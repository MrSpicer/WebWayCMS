namespace WebWayCMS.Routing;

/// <summary>
/// Implemented by content components that can serve a dynamic page sub-route
/// (the trailing path segment beneath a matched page, e.g. an article slug under
/// a blog page). <see cref="PageRouteTransformer"/> queries every registered
/// resolver before resolving a parent page for a sub-route; if none can resolve the
/// sub-route the request is left unresolved (a 404) instead of rendering the parent
/// page with a 200.
/// </summary>
public interface ISubRouteContent
{
    /// <summary>
    /// Determines whether this content can resolve the supplied sub-route to
    /// real content.
    /// </summary>
    /// <param name="subRoute">The sub-route (path beneath the matched page).</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns><c>true</c> if the sub-route maps to existing content; otherwise <c>false</c>.</returns>
    Task<bool> CanResolveSubRouteAsync(string subRoute, CancellationToken ct = default);
}
