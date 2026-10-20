namespace WebWayCMS.Attributes;

/// <summary>
/// Whether a zone item renders a single item or a list. Chosen per item in the zone editor (the
/// Add Widget modal and the settings dialog) for widgets that declare
/// <see cref="ContentZoneComponentAttribute.SupportsList"/>; every other widget is always
/// <see cref="Single"/>.
///
/// It is a flags enum so <see cref="FormPropertyAttribute.DisplayModes"/> can tag a configuration
/// field with the modes it applies to; an item's stored display mode is always exactly one value.
/// </summary>
[Flags]
public enum WidgetDisplayMode
{
    /// <summary>
    /// Single-item rendering (<c>view.*</c> on public pages, <c>admin.*</c> on admin pages).
    /// </summary>
    Single = 1,

    /// <summary>
    /// List rendering (<c>list.*</c> on public pages, <c>adminlist.*</c> on admin pages).
    /// </summary>
    List = 2
}
