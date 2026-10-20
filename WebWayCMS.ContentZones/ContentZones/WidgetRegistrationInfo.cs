using WebWayCMS.Forms;

namespace WebWayCMS.ContentZones;

public class WidgetRegistrationInfo
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string IconClass { get; set; } = string.Empty;
    public int Order { get; set; }
    public string? ConfigurationTypeName { get; set; }

    /// <summary>
    /// Whether the widget can render a list as well as a single item, so the zone editor offers a
    /// Single/List choice and a <c>List</c> display mode may be stored for its items.
    /// </summary>
    public bool SupportsList { get; set; }
    public List<FormPropertyInfo> Properties { get; set; } = new();
    public bool HasConfiguration => Properties.Count > 0;
}
