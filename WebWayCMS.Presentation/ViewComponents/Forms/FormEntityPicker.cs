using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

using WebWayCMS.Attributes;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Forms;

namespace WebWayCMS.ViewComponents.Forms;

[CMSFormComponent("EntityPicker", DataTypes = new[] { typeof(Guid) },
                  Category = "Selection", Order = 2,
                  DisplayName = "Entity Picker", Description = "Dropdown populated from an entity API list.")]
public sealed class FormEntityPicker : FormFieldViewComponentBase
{
    /// <summary>ViewData key carrying the resolved list endpoint (null when unresolvable).</summary>
    public const string EndpointViewDataKey = "EntityPicker:Endpoint";

    public override IViewComponentResult Invoke(FormFieldContext field, string? viewName = null)
    {
        // Optional: a rendering-only host has no admin handler registry, so the picker has no endpoint.
        var registry = HttpContext.RequestServices?.GetService<IAdminHandlerRegistry>();
        ViewData[EndpointViewDataKey] = EntityPickerEndpoint.Resolve(field.EntityType, registry);

        var name = viewName ?? (field.Mode == FormFieldMode.Read ? "Read" : "Write");
        return View(name, field);
    }
}
