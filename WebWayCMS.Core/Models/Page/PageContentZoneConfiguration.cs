using WebWayCMS.Attributes;

namespace WebWayCMS.Models.Page;

public class PageContentZoneConfiguration
{
    [FormProperty(
        Label = "Show Draft Pages",
        HelpText = "When enabled, includes unpublished (draft) pages in navigation.",
        EditorType = EditorType.Checkbox,
        Order = 1,
        FormComponent = "Checkbox"
    )]
    public bool ShowDraftPages { get; set; } = false;

    [FormProperty(
        Label = "Show Hidden Pages",
        HelpText = "When enabled, includes pages marked as hidden.",
        EditorType = EditorType.Checkbox,
        Order = 2,
        FormComponent = "Checkbox"
    )]
    public bool ShowHiddenPages { get; set; } = false;

    [FormProperty(
        Label = "Admin Pages",
        HelpText = "When enabled, displays pages whose route starts with /wadmin.",
        EditorType = EditorType.Checkbox,
        Order = 3,
        FormComponent = "Checkbox"
    )]
    public bool AdminPages { get; set; } = false;

    [FormProperty(
        Label = "View Name",
        HelpText = "The view template to use. Leave empty for default behavior.",
        Placeholder = "e.g., Default",
        EditorType = EditorType.ViewPicker,
        ViewComponentName = "Page",
        Order = 5,
        FormComponent = "ViewPicker"
    )]
    public string? ViewName { get; set; }
}
