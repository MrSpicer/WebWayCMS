using WebWayCMS.Attributes;
using WebWayCMS.Forms;

namespace WebWayCMS.ViewComponents.Forms;

[CMSFormComponent("DateTime", DataTypes = new[] { typeof(DateTime), typeof(DateTimeOffset) },
                  EditorType = EditorType.DateTime, IsDefaultForType = true,
                  Category = "Temporal", Order = 2,
                  DisplayName = "Date & Time Picker", Description = "Date and time picker.")]
public sealed class FormDateTime : FormFieldViewComponentBase { }
