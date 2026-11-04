using WebWayCMS.Attributes;

namespace WebWayCMS.Forms.Tests;

/// <summary>A standard field carrying a CSS class, and a required select with help text.</summary>
public sealed class StyledFieldsModel
{
    [FormProperty(EditorType = EditorType.Text, CssClass = "my-css", Order = 1)]
    public string Styled { get; set; } = "v";

    [FormProperty(EditorType = EditorType.Dropdown, DropdownOptions = "a:A,b:B", IsRequired = true, HelpText = "pick", Order = 2)]
    public string Pick { get; set; } = "a";

    [FormProperty(EditorType = EditorType.TextArea, IsRequired = true, Order = 3)]
    public string RequiredArea { get; set; } = "text";
}
