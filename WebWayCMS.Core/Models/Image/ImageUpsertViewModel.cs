using System.ComponentModel.DataAnnotations;

using WebWayCMS.Attributes;

namespace WebWayCMS.Models.Image;

/// <summary>
/// The admin form model for an image. Carries the media blob's content hash, never its bytes —
/// this view model is JSON round-tripped by <c>ContentFieldMerger</c> on every MCP update and
/// every content-seed apply, so binary data here would be base64'd through each partial write.
/// Upload happens out-of-band through the media API; this field holds only the resulting hash.
/// </summary>
public sealed class ImageUpsertViewModel : BaseContentViewModel
{
    [Required]
    [FormProperty(Label = "Image", IsRequired = true, Order = 3, FormComponent = "ImagePicker",
        HelpText = "Upload a PNG, JPEG, GIF, or WebP file.")]
    public string BlobHash { get; set; } = string.Empty;

    [Required]
    [StringLength(500)]
    [FormProperty(Label = "Alt text", EditorType = EditorType.Text, IsRequired = true, Order = 4,
        FormComponent = "Text",
        HelpText = "Describe the image for screen readers and for when it cannot be displayed.")]
    public string AltText { get; set; } = string.Empty;

    [StringLength(1000)]
    [FormProperty(Label = "Caption", EditorType = EditorType.Text, Order = 5, FormComponent = "Text",
        HelpText = "Optional visible caption.")]
    public string? Caption { get; set; }
}
