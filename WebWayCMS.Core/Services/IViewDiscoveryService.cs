using WebWayCMS.Attributes;

namespace WebWayCMS.Services;

/// <summary>
/// Service for discovering available views for ViewComponents and controllers.
/// Combines compiled Razor views registered as application parts (the only source available in
/// Release/Docker builds where views are compiled into assemblies) with a scan of the ASP.NET view
/// search paths on disk (so freshly-added .cshtml files appear in development without a rebuild).
/// </summary>
public interface IViewDiscoveryService
{
    /// <summary>
    /// Gets the variant names of the views available to the specified ViewComponent for the
    /// specified render mode. Widget view files are named <c>{mode}.{variant}.cshtml</c>; only
    /// files carrying the mode's prefix are listed, and the prefix is stripped from the result
    /// (<c>view.TwoColumnEqual.cshtml</c> lists as <c>TwoColumnEqual</c>). Un-prefixed files and
    /// <c>_</c> partials are ignored.
    /// </summary>
    /// <param name="componentName">The name of the ViewComponent (without "ViewComponent" suffix).</param>
    /// <param name="mode">The render mode whose views to list.</param>
    /// <returns>
    /// A list of variant names for the given mode. Returns an empty list if no matching views are
    /// found or the component directory doesn't exist.
    /// </returns>
    IReadOnlyList<string> GetAvailableViews(string componentName, WidgetRenderMode mode);

    /// <summary>
    /// Resolves a (component, mode, variant) triple to the actual view file name to render (e.g.
    /// <c>view.TwoColumnEqual</c>), matching case-insensitively so the returned name always has the
    /// real casing on disk (a protection for case-sensitive filesystems with runtime compilation).
    /// An empty or "default" variant resolves to the mode's default view.
    /// </summary>
    /// <returns>True when a view file with that exact mode+variant exists.</returns>
    bool TryResolveView(string componentName, WidgetRenderMode mode, string? variant, out string? viewName);

    /// <summary>
    /// Gets a list of available view names for the specified page controller.
    /// Scans Views/{controllerName}/ in the main project and sibling projects.
    /// </summary>
    /// <param name="controllerName">The name of the page controller.</param>
    /// <returns>
    /// A list of view names (without .cshtml extension) found in the controller's view directory.
    /// Returns an empty list if no views are found or the directory doesn't exist.
    /// </returns>
    IReadOnlyList<string> GetControllerViews(string controllerName);
}
