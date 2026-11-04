namespace WebWayCMS.Attributes;

/// <summary>
/// Helpers for <see cref="FormFieldRenderMode"/>.
/// </summary>
public static class FormFieldRenderModeExtensions
{
    /// <summary>
    /// The file-name prefix for the mode, e.g. <see cref="FormFieldRenderMode.AdminWrite"/> -> "adminwrite".
    /// Form component view files are named <c>{prefix}.{variant}.cshtml</c>.
    /// </summary>
    public static string ToFilePrefix(this FormFieldRenderMode mode) => mode.ToString().ToLowerInvariant();
}
