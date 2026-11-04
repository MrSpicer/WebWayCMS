namespace WebWayCMS.Attributes;

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
