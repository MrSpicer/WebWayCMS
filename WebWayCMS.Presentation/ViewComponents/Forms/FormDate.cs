using WebWayCMS.Attributes;
using WebWayCMS.Forms;

namespace WebWayCMS.ViewComponents.Forms;

[CMSFormComponent("Date", DataTypes = new[] { typeof(DateOnly) },
                  EditorType = EditorType.Date, IsDefaultForType = true,
                  Category = "Temporal", Order = 1,
                  DisplayName = "Date Picker", Description = "Date-only picker.")]
public sealed class FormDate : FormFieldViewComponentBase { }
