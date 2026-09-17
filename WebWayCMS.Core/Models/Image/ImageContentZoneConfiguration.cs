using WebWayCMS.Attributes;

namespace WebWayCMS.Models.Image;

/// <summary>
/// Configuration for the Image content-zone widget. References an image content node rather than a
/// blob hash directly, so alt text and caption stay defined once on the content item and the
/// reference can be resolved by a content seed file's <c>@seed:{guid}</c> token.
/// </summary>
public sealed class ImageContentZoneConfiguration
{
    /// <remarks>
    /// Nullable deliberately. A non-nullable value type here would silently discard the *entire*
    /// saved widget configuration whenever the admin leaves the field empty: the editor writes
    /// null, deserialization throws, and the zone model swallows it and falls back to an empty
    /// configuration object.
    /// </remarks>
    [FormProperty(Label = "Image", Order = 1, FormComponent = "EntityPicker", EntityType = "Image",
        HelpText = "Choose an existing image content item.")]
    public Guid? ImageNodeId { get; set; }

    [FormProperty(Label = "CSS class", EditorType = EditorType.Text, Order = 2, FormComponent = "Text",
        HelpText = "Optional class applied to the rendered image.")]
    public string? CssClass { get; set; }

    [FormProperty(Label = "Show caption", EditorType = EditorType.Checkbox, Order = 3, FormComponent = "Checkbox")]
    public bool ShowCaption { get; set; }

    [FormProperty(Label = "View", Order = 4, FormComponent = "ViewPicker", ViewComponentName = "Image",
        HelpText = "Optional alternate view.")]
    public string? ViewName { get; set; }
}
