using WebWayCMS.Forms;

namespace WebWayCMS.Pages;

/// <summary>
/// Contains metadata about a controller registered for use as a dynamic page type.
/// </summary>
public class PageControllerInfo
{
    /// <summary>
    /// Gets or sets the controller name (class name without "Controller" suffix).
    /// This is the value stored in <see cref="Data.Models.PageDTO.ControllerName"/>.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name shown in the page type selection dropdown.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description of what the page type renders.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the category for grouping page types.
    /// </summary>
    public string Category { get; set; } = "General";

    /// <summary>
    /// Gets or sets the CSS icon class for the page type.
    /// </summary>
    public string IconClass { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the sort order within the category.
    /// </summary>
    public int Order { get; set; } = 0;

    /// <summary>
    /// Gets or sets the Type of the Controller class.
    /// </summary>
    public Type ControllerType { get; set; } = null!;

    /// <summary>
    /// Gets or sets the Type of the configuration model class.
    /// Null if the controller has no configuration.
    /// </summary>
    public Type? ConfigurationType { get; set; }

    /// <summary>
    /// Gets or sets the list of configurable properties for this page type.
    /// </summary>
    public List<FormPropertyInfo> Properties { get; set; } = new();

    /// <summary>
    /// Gets a value indicating whether this page type has configurable properties.
    /// </summary>
    public bool HasConfiguration => ConfigurationType != null && Properties.Count > 0;
}