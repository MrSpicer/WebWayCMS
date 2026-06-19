using System.ComponentModel.DataAnnotations;
using WebWayCMS.Attributes;

namespace WebWayCMS.Models.Layout;

/// <summary>
/// Configuration model for the Layout ViewComponent when used within a content zone.
/// </summary>
public class LayoutContentZoneConfiguration
{
    /// <summary>
    /// Gets or sets the name of the view to render.
    /// </summary>
    [FormProperty(
        Label = "View Name",
        HelpText = "Select the view template to render.",
        EditorType = EditorType.ViewPicker,
        ViewComponentName = "Layout",
        IsRequired = true,
        Order = 1
    )]
    [Required(ErrorMessage = "Please select a view.")]
    public string ViewName { get; set; } = string.Empty;
}
