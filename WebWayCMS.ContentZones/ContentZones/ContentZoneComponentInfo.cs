using WebWayCMS.Forms;

namespace WebWayCMS.ContentZones;

/// <summary>
/// Contains metadata about a ViewComponent registered for use in content zones.
/// </summary>
public class ContentZoneComponentInfo
{
    /// <summary>
    /// Gets or sets the component name (ViewComponent class name without "ViewComponent" suffix).
    /// This is the value stored in <see cref="Data.Models.ContentZoneItemDTO.ComponentName"/>.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name shown in the component selection dropdown.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description of what the component renders.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the category for grouping components.
    /// </summary>
    public string Category { get; set; } = "General";

    /// <summary>
    /// Gets or sets the CSS icon class for the component.
    /// </summary>
    public string IconClass { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the sort order within the category.
    /// </summary>
    public int Order { get; set; } = 0;

    /// <summary>
    /// Gets or sets the Type of the configuration model class.
    /// Null if the component has no configuration.
    /// </summary>
    public Type? ConfigurationType { get; set; }

    /// <summary>
    /// Gets or sets the list of configurable properties for this component.
    /// </summary>
    public List<FormPropertyInfo> Properties { get; set; } = new();

    /// <summary>
    /// Gets a value indicating whether this component has configurable properties.
    /// </summary>
    public bool HasConfiguration => ConfigurationType != null && Properties.Count > 0;
}
