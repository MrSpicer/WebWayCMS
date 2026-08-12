using WebWayCMS.Attributes;
using WebWayCMS.Forms;

namespace WebWayCMS.ViewComponents.Forms;

[CMSFormComponent("Text", DataTypes = new[] { typeof(string) },
                  EditorType = EditorType.Text, IsDefaultForType = true,
                  Category = "Text", Order = 1,
                  DisplayName = "Text Input", Description = "Single-line text input.")]
public sealed class FormText : FormFieldViewComponentBase { }
