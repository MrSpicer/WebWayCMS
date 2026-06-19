using Microsoft.AspNetCore.Mvc;
using WebWayCMS.Attributes;
using WebWayCMS.Models.Layout;

namespace WebWayCMS.ViewComponents;

/// <summary>
/// Renders a selected view template. Used for flexible layout/partial rendering in content zones.
/// </summary>
[ContentZoneComponent(
    DisplayName = "Layout",
    Description = "Renders a selected view template.",
    Category = "General",
    ConfigurationType = typeof(LayoutContentZoneConfiguration),
    IconClass = "fa-columns",
    Order = 10
)]
public class LayoutViewComponent : ViewComponent
{
    /// <summary>
    /// Renders the configured view.
    /// </summary>
    /// <param name="config">The content zone configuration.</param>
    /// <returns>The view component result.</returns>
    public IViewComponentResult Invoke(LayoutContentZoneConfiguration config)
    {
        config ??= new LayoutContentZoneConfiguration();
        return View(config.ViewName);
    }
}