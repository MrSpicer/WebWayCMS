namespace WebWayCMS.Attributes;

/// <summary>
/// Marks a Controller as available for use as a dynamic page controller.
/// The controller will appear in the admin UI when creating or editing pages.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PageControllerAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the display name shown in the page type selection dropdown.
    /// If not specified, the controller name (without "Controller" suffix) is used with spaces inserted.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a description of what the page type renders.
    /// Shown as help text in the admin UI.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the category for grouping related page types in the UI.
    /// </summary>
    public string Category { get; set; } = "General";

    /// <summary>
    /// Gets or sets the Type of the configuration model class.
    /// Properties on this class should be decorated with <see cref="FormPropertyAttribute"/>.
    /// </summary>
    public Type? ConfigurationType { get; set; }

    /// <summary>
    /// Gets or sets an optional CSS icon class for display in the UI.
    /// </summary>
    public string IconClass { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the sort order for display within a category.
    /// Lower values appear first. Default is 0.
    /// </summary>
    public int Order { get; set; } = 0;

    public PageControllerAttribute()
    {
    }

    public PageControllerAttribute(string displayName, Type configurationType)
    {
        DisplayName = displayName;
        ConfigurationType = configurationType;
    }
}