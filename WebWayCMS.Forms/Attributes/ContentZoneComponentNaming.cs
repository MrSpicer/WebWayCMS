namespace WebWayCMS.Attributes;

/// <summary>
/// Resolves the stored component name for a content-zone widget from its type and
/// <see cref="ContentZoneComponentAttribute"/>. Shared by the admin metadata registry and the
/// render-side widget registry so both derive identical names for the same widget.
/// </summary>
public static class ContentZoneComponentNaming
{
    /// <summary>
    /// Returns the explicit <see cref="ContentZoneComponentAttribute.Name"/> when set; otherwise the
    /// component's class name with a known "Widget" or "ViewComponent" suffix removed.
    /// </summary>
    public static string ResolveName(Type type, ContentZoneComponentAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(attribute);

        if (!string.IsNullOrEmpty(attribute.Name))
            return attribute.Name;

        var name = type.Name;
        foreach (var suffix in new[] { "Widget", "ViewComponent" })
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal))
                return name[..^suffix.Length];
        }
        return name;
    }
}
