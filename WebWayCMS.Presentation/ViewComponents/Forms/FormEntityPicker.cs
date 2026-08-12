using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;
using WebWayCMS.Forms;

namespace WebWayCMS.ViewComponents.Forms;

[CMSFormComponent("EntityPicker", DataTypes = new[] { typeof(Guid) },
                  Category = "Selection", Order = 2,
                  DisplayName = "Entity Picker", Description = "Dropdown populated from an entity API list.")]
public sealed class FormEntityPicker : FormFieldViewComponentBase
{
    public override IViewComponentResult Invoke(FormFieldContext field, string? viewName = null)
    {
        var name = viewName ?? (field.Mode == FormFieldMode.Read ? "Read" : "Write");
        return View(name, field);
    }
}
