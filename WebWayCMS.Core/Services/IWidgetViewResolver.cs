using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;

namespace WebWayCMS.Services;

/// <summary>
/// Pure, unit-testable fallback logic for resolving a widget render to a view file. Given the
/// component name, the active render mode, and the widget's configuration (which carries the
/// <c>ViewName</c>/<c>ListViewName</c> pickers and the per-item display mode), computes which view
/// file to render. <c>V</c> is <c>ViewName</c>, <c>L</c> is <c>ListViewName</c>:
///
/// <list type="bullet">
/// <item><b>View:</b> <c>view.{V}</c> → <c>view.default</c> → nothing.</item>
/// <item><b>List:</b> <c>list.{L}</c> → <c>list.default</c> → the View chain.</item>
/// <item><b>Admin:</b> <c>admin.{V}</c> → <c>admin.default</c> → the View chain.</item>
/// <item><b>AdminList:</b> <c>adminlist.{L}</c> → <c>adminlist.default</c> → the Admin chain
/// (without its View tail) → the List chain.</item>
/// <item><b>Edit:</b> <c>edit.{V}</c> (or <c>edit.{L}</c> when the item's display mode is
/// <see cref="WidgetDisplayMode.List"/>) → <c>edit.default</c> → the generic CMS card.</item>
/// </list>
///
/// The admin and edit modes mirror the public variant name, so a widget that ships
/// <c>view.Card</c> + <c>admin.Card</c> renders the matching admin view without a separate picker.
/// An empty or "default" value always means the mode's default view; resolution itself is
/// side-effect free.
/// </summary>
public interface IWidgetViewResolver
{
    /// <summary>
    /// Resolves a (component, mode, variant) triple to the view file name to render (e.g.
    /// <c>view.TwoColumnEqual</c>), delegating to <see cref="IViewDiscoveryService.TryResolveView"/>.
    /// An empty or "default" variant resolves to the mode's default view.
    /// </summary>
    bool TryResolveView(string componentName, WidgetRenderMode mode, string? variant, out string? viewName);

    /// <summary>
    /// Runs the full per-mode fallback chain for a widget render (see the type documentation).
    /// </summary>
    WidgetViewResolution Resolve(string componentName, WidgetRenderMode contextMode, WidgetConfiguration? config);
}
