namespace WebWayCMS.Attributes;

/// <summary>
/// Marks a content-zone widget (a Blazor component) as available for use within content zones.
/// The component appears in the admin UI dropdown when adding items to a content zone.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ContentZoneComponentAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the stored component name (persisted in zone items, used by the widget registry).
    /// If not specified, the component's class name with a "Widget" or "ViewComponent" suffix removed
    /// is used.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name shown in the component selection dropdown.
    /// If not specified, the component name is used.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a description of what the component renders.
    /// Shown as help text in the admin UI.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the category for grouping related components in the UI.
    /// Examples: "Content", "Layout", "Media", "Navigation".
    /// </summary>
    public string Category { get; set; } = "General";

    /// <summary>
    /// Gets or sets the Type of the configuration model class.
    /// This class defines the properties that can be configured in the admin UI.
    /// Properties on this class should be decorated with <see cref="FormPropertyAttribute"/>.
    /// </summary>
    public Type? ConfigurationType { get; set; }

    /// <summary>
    /// Gets or sets an optional CSS icon class for display in the UI.
    /// Examples: "fa-file-text", "mdi-image", "bi-layout-split".
    /// </summary>
    public string IconClass { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the sort order for display within a category.
    /// Lower values appear first. Default is 0.
    /// </summary>
    public int Order { get; set; } = 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentZoneComponentAttribute"/> class.
    /// </summary>
    public ContentZoneComponentAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentZoneComponentAttribute"/> class
    /// with a display name and configuration type.
    /// </summary>
    /// <param name="displayName">The display name for the component.</param>
    /// <param name="configurationType">The type of the configuration model.</param>
    public ContentZoneComponentAttribute(string displayName, Type configurationType)
    {
        DisplayName = displayName;
        ConfigurationType = configurationType;
    }
}
