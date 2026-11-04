using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;

namespace WebWayCMS.Services;

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
