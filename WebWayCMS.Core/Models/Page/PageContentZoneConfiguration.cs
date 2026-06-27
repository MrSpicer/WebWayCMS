using WebWayCMS.Attributes;

namespace WebWayCMS.Models.Page;

public class PageContentZoneConfiguration
{
    [FormProperty(
        Label = "Show Draft Pages",
        HelpText = "When enabled, includes unpublished (draft) pages in navigation.",
        EditorType = EditorType.Checkbox,
        Order = 1
    )]
    public bool ShowDraftPages { get; set; } = false;

    [FormProperty(
        Label = "Show Hidden Pages",
        HelpText = "When enabled, includes pages marked as hidden.",
        EditorType = EditorType.Checkbox,
        Order = 2
    )]
    public bool ShowHiddenPages { get; set; } = false;

    [FormProperty(
        Label = "Include Admin Pages",
        HelpText = "When enabled, includes pages whose route starts with /admin.",
        EditorType = EditorType.Checkbox,
        Order = 3
    )]
    public bool AdminPages { get; set; } = false;

    [FormProperty(
        Label = "View Name",
        HelpText = "The view template to use. Leave empty for default behavior.",
        Placeholder = "e.g., Default",
        EditorType = EditorType.ViewPicker,
        ViewComponentName = "Page",
        Order = 5
    )]
    public string? ViewName { get; set; }
}