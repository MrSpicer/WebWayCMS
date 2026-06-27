using System.ComponentModel.DataAnnotations;

using WebWayCMS.Attributes;

namespace WebWayCMS.Models.Page;

public sealed class PageUpsertViewModel : BaseContentViewModel
{
    [Required]
    [FormProperty(Label = "Route", EditorType = EditorType.Text, IsRequired = true, Order = 2,
        Placeholder = "/about",
        HelpText = "Must start with \"/\", no trailing slash (except root \"/\"). Lowercase letters, numbers, hyphens, and slashes only.",
        Pattern = @"^\/[a-z0-9\-\/]*[a-z0-9\-]$|^\/$")]
    public string Route { get; set; } = string.Empty;

    [Required]
    [FormProperty(Label = "Page Controller", EditorType = EditorType.PageControllerPicker, IsRequired = true, Order = 3,
        HelpText = "The page type determines what the page renders and what configuration options are available.")]
    public string ControllerName { get; set; } = string.Empty;

    [FormProperty(
    Label = "View Name",
    HelpText = "The view template to use. Leave empty for default behavior.",
    Placeholder = "e.g., Default",
    EditorType = EditorType.ViewPicker,
    ViewComponentName = "Page",
    Order = 90)]
    public string? ViewName { get; set; }

    //todo: what in the AI hallucination is
    [FormProperty(EditorType = EditorType.Hidden, Order = 99)]
    public string ConfigurationJson { get; set; } = "{}";
}