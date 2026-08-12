using WebWayCMS.Attributes;
using WebWayCMS.Forms;

namespace WebWayCMS.ViewComponents.Forms;

[CMSFormComponent("RichText", DataTypes = new[] { typeof(string) },
                  EditorType = EditorType.RichText,
                  Category = "Text", Order = 3,
                  DisplayName = "Rich Text", Description = "Rich text / HTML editor.")]
public sealed class FormRichText : FormFieldViewComponentBase { }
