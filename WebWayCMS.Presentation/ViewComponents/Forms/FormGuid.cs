using WebWayCMS.Attributes;
using WebWayCMS.Forms;

namespace WebWayCMS.ViewComponents.Forms;

[CMSFormComponent("Guid", DataTypes = new[] { typeof(Guid) },
                  EditorType = EditorType.Guid, IsDefaultForType = true,
                  Category = "Special", Order = 1,
                  DisplayName = "Guid Input", Description = "GUID input with optional entity picker.")]
public sealed class FormGuid : FormFieldViewComponentBase { }
