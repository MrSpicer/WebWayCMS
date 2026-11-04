using WebWayCMS.Attributes;

namespace WebWayCMS.Forms.Tests;

/// <summary>Covers every EditorType branch plus value-formatting paths in the tag helper.</summary>
public sealed class AllEditorsModel
{
    [FormProperty("Plain Text", EditorType.Text)]
    public string Text { get; set; } = "hello";

    [FormProperty(Label = "Area", EditorType = EditorType.TextArea, Placeholder = "ph", HelpText = "help", MaxLength = 50)]
    public string Area { get; set; } = "multi";

    [FormProperty(EditorType = EditorType.RichText, IsRequired = true)]
    public string Rich { get; set; } = "<b>x</b>";

    [FormProperty(EditorType = EditorType.Number, Min = 1, Max = 10)]
    public int Count { get; set; } = 5;

    [FormProperty(EditorType = EditorType.Checkbox, HelpText = "check help", CssClass = "extra", IsRequired = true)]
    public bool Flag { get; set; } = true;

    [FormProperty(EditorType = EditorType.Guid)]
    public Guid Identifier { get; set; } = Guid.NewGuid();

    [FormProperty(EditorType = EditorType.Dropdown, DropdownOptions = "a:Alpha,b:Beta")]
    public string Choice { get; set; } = "a";

    [FormProperty(EditorType = EditorType.Date)]
    public DateTime Day { get; set; } = new(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    [FormProperty(EditorType = EditorType.DateTime)]
    public DateTime Moment { get; set; } = new(2024, 1, 2, 3, 4, 0, DateTimeKind.Utc);

    [FormProperty(EditorType = EditorType.Color)]
    public string Colour { get; set; } = "#fff";

    [FormProperty(EditorType = EditorType.Url, Placeholder = "https://", HelpText = "u", IsRequired = true, Pattern = "^https?://")]
    public string Link { get; set; } = "https://x";

    [FormProperty(EditorType = EditorType.Email)]
    public string Mail { get; set; } = "a@b.c";

    [FormProperty(EditorType = EditorType.ViewPicker, ViewComponentName = "Article")]
    public string View { get; set; } = "Default";

    [FormProperty(EditorType = EditorType.PageControllerPicker)]
    public string Controller { get; set; } = "GenericPage";

    [FormProperty(EditorType = EditorType.Hidden)]
    public string Secret { get; set; } = "s";
}
