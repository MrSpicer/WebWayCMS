using WebWayCMS.Attributes;

namespace WebWayCMS.Models.CMSRoute;

public class RouteNavigationConfiguration
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

    [FormProperty(
        Label = "View Name",
        HelpText = "The view template to use. Leave empty for default behavior.",
        Placeholder = "e.g., Default",
        EditorType = EditorType.ViewPicker,
        ViewComponentName = "RouteNavigation",
        Order = 5,
        FormComponent = "ViewPicker"
    )]
    public string? ViewName { get; set; }
}
