using WebWayCMS.Attributes;
using WebWayCMS.Forms;

namespace WebWayCMS.ViewComponents.Forms;

[CMSFormComponent("Email", DataTypes = new[] { typeof(string) },
                  EditorType = EditorType.Email,
                  Category = "Text", Order = 5,
                  DisplayName = "Email Input", Description = "Email input with validation.")]
public sealed class FormEmail : FormFieldViewComponentBase { }
