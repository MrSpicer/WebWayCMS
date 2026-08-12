using WebWayCMS.Attributes;
using WebWayCMS.Forms;

namespace WebWayCMS.ViewComponents.Forms;

[CMSFormComponent("Color", DataTypes = new[] { typeof(string) },
                  EditorType = EditorType.Color,
                  Category = "Text", Order = 6,
                  DisplayName = "Color Picker", Description = "Color picker input.")]
public sealed class FormColor : FormFieldViewComponentBase { }
