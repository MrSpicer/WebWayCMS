using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;

namespace WebWayCMS.Models.CMSRoute;

public class RouteNavigationConfiguration : WidgetConfiguration
{
    [FormProperty(
        Label = "Admin Routes",
        HelpText = "When enabled, displays routes whose pattern starts with /wadmin.",
        EditorType = EditorType.Checkbox,
        Order = 1,
        FormComponent = "Checkbox"
    )]
    public bool AdminRoutes { get; set; } = false;

    [FormProperty(
        Label = "Include Reserved Routes",
        HelpText = "When enabled, includes routes marked as reserved. Reserved routes never dispatch, so they are normally hidden.",
        EditorType = EditorType.Checkbox,
        Order = 2,
        FormComponent = "Checkbox"
    )]
    public bool IncludeReserved { get; set; } = false;
}
