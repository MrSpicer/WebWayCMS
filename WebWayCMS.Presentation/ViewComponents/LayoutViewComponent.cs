using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;
using WebWayCMS.Models.Layout;

namespace WebWayCMS.ViewComponents;

/// <summary>
/// Renders a layout view template. The variant is picked with the configuration's view picker
/// (<c>view.*</c>); the zone editor renders the matching <c>edit.{variant}</c> (falling back to
/// <c>edit.default</c>), so there is no separate edit-view picker.
/// </summary>
[ContentZoneComponent(
    DisplayName = "Layout",
    Description = "Renders a selected view template.",
    Category = "General",
    ConfigurationType = typeof(LayoutContentZoneConfiguration),
    IconClass = "fa-columns",
    Order = 10
)]
public class LayoutViewComponent : WidgetViewComponent
{
    /// <summary>
    /// Renders the configured view for the current render mode.
    /// </summary>
    /// <param name="config">The content zone configuration.</param>
    /// <returns>The view component result.</returns>
    public IViewComponentResult Invoke(LayoutContentZoneConfiguration config)
    {
        config ??= new LayoutContentZoneConfiguration();
        return ModeView(config, (object?)null);
    }
}
