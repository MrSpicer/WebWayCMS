namespace WebWayCMS.Models.CMSRoute;

public sealed class RouteNavigationViewModel
{
    public List<RouteNavigationItem> Items { get; init; } = new();
}

public sealed class RouteNavigationItem
{
    /// <summary>
    /// The link text, taken from the route's <c>NavigationName</c>. Routes without one are not rendered.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public List<RouteNavigationItem> Children { get; init; } = new();
}
