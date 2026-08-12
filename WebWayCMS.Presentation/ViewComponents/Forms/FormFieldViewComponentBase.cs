using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Forms;

namespace WebWayCMS.ViewComponents.Forms;

/// <summary>
/// Base class for form field view components. Selects the Write or Read view based on
/// <see cref="FormFieldContext.Mode"/> and passes the field context as the view model.
/// </summary>
public abstract class FormFieldViewComponentBase : ViewComponent
{
    public virtual IViewComponentResult Invoke(FormFieldContext field, string? viewName = null)
    {
        var name = viewName ?? (field.Mode == FormFieldMode.Read ? "Read" : "Write");
        return View(name, field);
    }
}
