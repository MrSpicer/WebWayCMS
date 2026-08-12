using WebWayCMS.Attributes;
using WebWayCMS.Forms;

namespace WebWayCMS.ViewComponents.Forms;

[CMSFormComponent("TextArea", DataTypes = new[] { typeof(string) },
                  EditorType = EditorType.TextArea,
                  Category = "Text", Order = 2,
                  DisplayName = "Text Area", Description = "Multi-line text area.")]
public sealed class FormTextArea : FormFieldViewComponentBase { }
