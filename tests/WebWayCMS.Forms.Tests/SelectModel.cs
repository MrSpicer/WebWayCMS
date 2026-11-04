using WebWayCMS.Attributes;

namespace WebWayCMS.Forms.Tests;

/// <summary>A select whose stored value does not match any option, and a non-empty ViewPicker value.</summary>
public sealed class SelectModel
{
    [FormProperty(EditorType = EditorType.Dropdown, DropdownOptions = "x:X,y:Y")]
    public string Unmatched { get; set; } = "z";

    [FormProperty(EditorType = EditorType.ViewPicker, ViewComponentName = "Page")]
    public string Picked { get; set; } = "Custom";

    [FormProperty(EditorType = EditorType.PageControllerPicker)]
    public string EmptyController { get; set; } = string.Empty;
}
