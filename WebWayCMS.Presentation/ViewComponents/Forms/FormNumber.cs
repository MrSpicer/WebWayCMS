using WebWayCMS.Attributes;
using WebWayCMS.Forms;

namespace WebWayCMS.ViewComponents.Forms;

[CMSFormComponent("Number", DataTypes = new[] { typeof(int), typeof(long), typeof(short),
                                                typeof(decimal), typeof(double), typeof(float) },
                  EditorType = EditorType.Number, IsDefaultForType = true,
                  Category = "Numeric", Order = 1,
                  DisplayName = "Number Input", Description = "Numeric input with min/max validation.")]
public sealed class FormNumber : FormFieldViewComponentBase { }
