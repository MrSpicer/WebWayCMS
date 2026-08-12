using WebWayCMS.Attributes;

namespace WebWayCMS.Forms;

/// <summary>
/// Runtime representation of a registered form component, the registry's unit.
/// Mirrors <see cref="ContentZones.WidgetRegistrationInfo"/> in shape.
/// </summary>
public sealed class FormComponentInfo
{
    public string Name { get; set; } = string.Empty;
    public string ViewComponentName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string IconClass { get; set; } = string.Empty;
    public int Order { get; set; }
    public List<string> DataTypeNames { get; set; } = new();
    public EditorType? EditorTypeAlias { get; set; }
    public bool IsDefaultForType { get; set; }
    public string WriteViewName { get; set; } = "Write";
    public string ReadViewName { get; set; } = "Read";
}
