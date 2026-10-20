using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;

namespace WebWayCMS.Services;

/// <summary>
/// The outcome of resolving a widget render to a view.
/// </summary>
public enum WidgetViewResolutionKind
{
    /// <summary>A concrete view file was resolved; render it (<see cref="WidgetViewResolution.ViewName"/>).</summary>
    View = 0,

    /// <summary>Edit mode with no edit-mode views at all; render the generic CMS card.</summary>
    EditCard,

    /// <summary>View mode with no view-mode views; render nothing.</summary>
    Empty
}

/// <summary>
/// A resolved widget render: either a concrete view file name to render, the generic edit card,
/// or nothing at all.
/// </summary>
public sealed record WidgetViewResolution(WidgetViewResolutionKind Kind, string? ViewName)
{
    public static WidgetViewResolution Of(string viewName) => new(WidgetViewResolutionKind.View, viewName);
    public static readonly WidgetViewResolution EditCard = new(WidgetViewResolutionKind.EditCard, null);
    public static readonly WidgetViewResolution Empty = new(WidgetViewResolutionKind.Empty, null);
}

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

/// <summary>
/// Default <see cref="IWidgetViewResolver"/> implementation over <see cref="IViewDiscoveryService"/>.
/// </summary>
public sealed class WidgetViewResolver : IWidgetViewResolver
{
    private readonly IViewDiscoveryService _viewDiscoveryService;

    public WidgetViewResolver(IViewDiscoveryService viewDiscoveryService)
    {
        _viewDiscoveryService = viewDiscoveryService ?? throw new ArgumentNullException(nameof(viewDiscoveryService));
    }

    public bool TryResolveView(string componentName, WidgetRenderMode mode, string? variant, out string? viewName)
        => _viewDiscoveryService.TryResolveView(componentName, mode, variant, out viewName);

    public WidgetViewResolution Resolve(string componentName, WidgetRenderMode contextMode, WidgetConfiguration? config)
    {
        var viewName = config?.ViewName;
        var listViewName = config?.ListViewName;
        var isList = config?.Metadata.DisplayMode == WidgetDisplayMode.List;

        return contextMode switch
        {
            WidgetRenderMode.Edit =>
                TryVariantOrDefault(componentName, WidgetRenderMode.Edit, isList ? listViewName : viewName)
                    ?? WidgetViewResolution.EditCard,

            WidgetRenderMode.List =>
                TryVariantOrDefault(componentName, WidgetRenderMode.List, listViewName)
                    ?? ResolveViewMode(componentName, viewName),

            WidgetRenderMode.Admin =>
                TryVariantOrDefault(componentName, WidgetRenderMode.Admin, viewName)
                    ?? ResolveViewMode(componentName, viewName),

            WidgetRenderMode.AdminList =>
                TryVariantOrDefault(componentName, WidgetRenderMode.AdminList, listViewName)
                    ?? TryVariantOrDefault(componentName, WidgetRenderMode.Admin, viewName)
                    ?? TryVariantOrDefault(componentName, WidgetRenderMode.List, listViewName)
                    ?? ResolveViewMode(componentName, viewName),

            _ => ResolveViewMode(componentName, viewName)
        };
    }

    /// <summary>
    /// The mode's chosen variant, else the mode's default view, else null (the caller falls back
    /// to another mode).
    /// </summary>
    private WidgetViewResolution? TryVariantOrDefault(string componentName, WidgetRenderMode mode, string? variant)
    {
        if (TryResolveView(componentName, mode, variant, out var view))
            return WidgetViewResolution.Of(view!);

        return TryResolveView(componentName, mode, "default", out var modeDefault)
            ? WidgetViewResolution.Of(modeDefault!)
            : null;
    }

    private WidgetViewResolution ResolveViewMode(string componentName, string? viewName)
        => TryVariantOrDefault(componentName, WidgetRenderMode.View, viewName) ?? WidgetViewResolution.Empty;
}
