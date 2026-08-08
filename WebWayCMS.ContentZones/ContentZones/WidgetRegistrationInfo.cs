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
    public List<FormPropertyInfo> Properties { get; set; } = new();
    public bool HasConfiguration => Properties.Count > 0;
}
