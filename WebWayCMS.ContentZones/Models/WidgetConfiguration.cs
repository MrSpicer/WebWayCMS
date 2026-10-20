using System.Text.Json.Serialization;

using WebWayCMS.Attributes;

namespace WebWayCMS.ContentZones;

/// <summary>
/// Abstract base for content-zone widget configurations that participate in render modes.
///
/// Render modes split a widget's views by purpose: files are named <c>{mode}.{variant}.cshtml</c>,
/// and the active mode is derived from where the widget renders (zone editor → <c>edit</c>, admin
/// page → <c>admin</c>/<c>adminlist</c>, anything else → <c>view</c>/<c>list</c>) and the item's
/// display mode (<see cref="WidgetMetadata.DisplayMode"/>). The two pickers here choose the variant:
/// <see cref="ViewName"/> for single-item rendering and <see cref="ListViewName"/> for list
/// rendering. The admin, admin-list and edit modes mirror the chosen variant name (e.g.
/// <c>admin.Card</c> for <c>view.Card</c>) before falling back to their mode's default view.
///
/// <see cref="ViewName"/>/<see cref="ListViewName"/> carry no <c>ViewComponentName</c>: the view
/// picker falls back to the widget name supplied by the form context, so one base class works for
/// every widget without each config repeating its own component name.
/// </summary>
public abstract class WidgetConfiguration
{
    /// <summary>
    /// The single-item variant (<c>view.{ViewName}</c>). Empty (or <c>default</c>) resolves to the
    /// widget's default view; when no view-mode view exists the widget renders nothing.
    /// </summary>
    [FormProperty(
        Label = "View",
        HelpText = "View variant for single-item rendering. Leave empty for the default view.",
        EditorType = EditorType.ViewPicker,
        RenderMode = WidgetRenderMode.View,
        DisplayModes = WidgetDisplayMode.Single,
        FormComponent = "ViewPicker"
    )]
    public string? ViewName { get; set; }

    /// <summary>
    /// The list variant (<c>list.{ListViewName}</c>), used when the item's display mode is
    /// <see cref="WidgetDisplayMode.List"/>. Empty (or <c>default</c>) resolves to the default list
    /// view; when no list-mode view exists, falls back to view-mode resolution.
    /// </summary>
    [FormProperty(
        Label = "List View",
        HelpText = "View variant for list rendering. Leave empty for the default list view.",
        EditorType = EditorType.ViewPicker,
        RenderMode = WidgetRenderMode.List,
        DisplayModes = WidgetDisplayMode.List,
        FormComponent = "ViewPicker"
    )]
    public string? ListViewName { get; set; }

    /// <summary>
    /// The item's per-widget metadata, attached at render time by the zone mapper from the zone
    /// item's <c>MetadataJson</c> column.
    /// </summary>
    [JsonIgnore]
    public WidgetMetadata Metadata { get; set; } = new();
}
