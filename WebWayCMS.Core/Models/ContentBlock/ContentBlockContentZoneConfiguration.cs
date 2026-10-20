using System.ComponentModel.DataAnnotations;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;

namespace WebWayCMS.Models.ContentBlock;

/// <summary>
/// Configuration model for the ContentBlock ViewComponent when used within a content zone.
/// Defines the properties that can be configured in the admin UI. The view template is picked per
/// render mode through the inherited <see cref="WidgetConfiguration"/> pickers.
/// </summary>
public class ContentBlockContentZoneConfiguration : WidgetConfiguration
{
    /// <summary>
    /// Gets or sets the ID of the content block to render.
    /// </summary>
    [FormProperty(
        Label = "Content Block",
        HelpText = "Select the content block to display in this zone.",
        EditorType = EditorType.Guid,
        EntityType = "ContentBlock",
        IsRequired = true,
        Order = 1,
        FormComponent = "EntityPicker"
    )]
    [Required(ErrorMessage = "Please select a content block.")]
    public Guid ContentBlockID { get; set; }
}
