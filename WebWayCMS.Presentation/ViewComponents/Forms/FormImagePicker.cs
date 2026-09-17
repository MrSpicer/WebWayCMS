using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;
using WebWayCMS.Forms;

namespace WebWayCMS.ViewComponents.Forms;

/// <summary>
/// Uploads an image and stores the resulting content hash in a string field.
/// </summary>
/// <remarks>
/// Selected by registry name (<c>FormComponent = "ImagePicker"</c>) rather than by an
/// <see cref="EditorType"/> value, following <see cref="FormEntityPicker"/> — a new kind of control
/// does not require widening the shared editor enum.
/// <para>
/// The control posts the file to the media API on its own and writes only the returned hash into a
/// hidden input, which keeps the surrounding admin form url-encoded and keeps image bytes out of
/// every view model.
/// </para>
/// </remarks>
[CMSFormComponent("ImagePicker", DataTypes = new[] { typeof(string) },
                  Category = "Selection", Order = 5,
                  DisplayName = "Image Picker", Description = "Uploads an image and stores its content hash.")]
public sealed class FormImagePicker : FormFieldViewComponentBase { }
