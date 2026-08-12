using WebWayCMS.Attributes;
using WebWayCMS.Forms;

namespace WebWayCMS.ViewComponents.Forms;

[CMSFormComponent("Url", DataTypes = new[] { typeof(string) },
                  EditorType = EditorType.Url,
                  Category = "Text", Order = 4,
                  DisplayName = "URL Input", Description = "URL input with validation.")]
public sealed class FormUrl : FormFieldViewComponentBase { }
