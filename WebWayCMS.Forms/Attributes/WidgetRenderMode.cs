namespace WebWayCMS.Attributes;

/// <summary>
/// The render context for a widget view. A widget's view files are named
/// <c>{mode}.{variant}.cshtml</c> (mode lowercase), and exactly one mode is active per render. The
/// mode is never chosen directly: it is derived from where the widget renders and the item's
/// <see cref="WidgetDisplayMode"/>:
///
/// <list type="bullet">
/// <item>the admin zone editor renders <see cref="Edit"/>;</item>
/// <item>otherwise an admin page (request path under <c>/wadmin</c>) renders <see cref="Admin"/>
/// (single) or <see cref="AdminList"/> (list);</item>
/// <item>and any other page renders <see cref="View"/> (single) or <see cref="List"/> (list).</item>
/// </list>
///
/// Modes are CMS-owned and fixed; hosts cannot add new ones.
/// </summary>
public enum WidgetRenderMode
{
    /// <summary>
    /// Public single-item rendering (the default).
    /// </summary>
    View = 0,

    /// <summary>
    /// Public list rendering, for an item whose display mode is <see cref="WidgetDisplayMode.List"/>.
    /// </summary>
    List,

    /// <summary>
    /// Single-item rendering on an admin page, styled for the admin rather than the site.
    /// </summary>
    Admin,

    /// <summary>
    /// List rendering on an admin page, styled for the admin rather than the site.
    /// </summary>
    AdminList,

    /// <summary>
    /// The admin zone editor, rendered with Bulma. Only the zone editor selects it.
    /// </summary>
    Edit
}

/// <summary>
/// Helpers for <see cref="WidgetRenderMode"/>.
/// </summary>
public static class WidgetRenderModeExtensions
{
    /// <summary>
    /// The file-name prefix for the mode, e.g. <see cref="WidgetRenderMode.AdminList"/> -> "adminlist".
    /// Widget view files are named <c>{prefix}.{variant}.cshtml</c>.
    /// </summary>
    public static string ToFilePrefix(this WidgetRenderMode mode) => mode.ToString().ToLowerInvariant();
}
