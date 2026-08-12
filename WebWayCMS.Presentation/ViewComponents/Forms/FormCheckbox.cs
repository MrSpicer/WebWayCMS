using WebWayCMS.Attributes;
using WebWayCMS.Forms;

namespace WebWayCMS.ViewComponents.Forms;

[CMSFormComponent("Checkbox", DataTypes = new[] { typeof(bool) },
                  EditorType = EditorType.Checkbox, IsDefaultForType = true,
                  Category = "Boolean", Order = 1,
                  DisplayName = "Checkbox", Description = "Checkbox for boolean values.")]
public sealed class FormCheckbox : FormFieldViewComponentBase { }
