using WebWayCMS.Attributes;

namespace WebWayCMS.Forms.Tests;

/// <summary>Checkbox permutations: unchecked, no CSS, no help text, not required.</summary>
public sealed class PlainCheckboxModel
{
    [FormProperty(EditorType = EditorType.Checkbox)]
    public bool Flag { get; set; } = false;
}
