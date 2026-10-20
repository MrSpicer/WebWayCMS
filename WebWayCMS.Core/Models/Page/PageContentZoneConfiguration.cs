using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;

namespace WebWayCMS.Models.Page;

public class PageContentZoneConfiguration : WidgetConfiguration
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
}
