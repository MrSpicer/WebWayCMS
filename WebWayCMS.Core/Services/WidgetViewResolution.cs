namespace WebWayCMS.Services;

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
