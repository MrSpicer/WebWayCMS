using WebWayCMS.Attributes;
using WebWayCMS.Forms;

namespace WebWayCMS.ViewComponents.Forms;

[CMSFormComponent("Hidden", DataTypes = new Type[0],
                  EditorType = EditorType.Hidden,
                  Category = "Special", Order = 99,
                  DisplayName = "Hidden Field", Description = "Hidden form field.")]
public sealed class FormHidden : FormFieldViewComponentBase { }
