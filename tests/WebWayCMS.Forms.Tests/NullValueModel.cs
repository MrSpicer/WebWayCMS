using WebWayCMS.Attributes;

namespace WebWayCMS.Forms.Tests;

/// <summary>Null values: a checkbox bound to a null bool? (not a bool) and a null text field.</summary>
public sealed class NullValueModel
{
    [FormProperty(EditorType = EditorType.Checkbox, Order = 1)]
    public bool? MaybeFlag { get; set; }

    [FormProperty(EditorType = EditorType.Text, Order = 2)]
    public string? Missing { get; set; }
}
