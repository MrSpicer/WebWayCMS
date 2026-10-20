using WebWayCMS.ContentZones;

namespace WebWayCMS.Models.Layout;

/// <summary>
/// Configuration model for the Layout ViewComponent when used within a content zone.
/// The layout variant is picked with the inherited <see cref="WidgetConfiguration.ViewName"/>
/// picker (<c>view.*</c>); the zone editor mirrors that name to <c>edit.{variant}</c>, falling back
/// to <c>edit.default</c>.
/// </summary>
public class LayoutContentZoneConfiguration : WidgetConfiguration
{
}
