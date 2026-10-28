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

    /// <summary>ViewData key carrying the target content type key for the reference modal.</summary>
    public const string ContentTypeViewDataKey = "EntityPicker:ContentType";

    /// <summary>ViewData key carrying whether the current user may create/edit the target type.</summary>
    public const string CanWriteViewDataKey = "EntityPicker:CanWrite";

    public override IViewComponentResult Invoke(FormFieldContext field)
    {
        // Optional: a rendering-only host has no admin handler registry, so the picker has no endpoint.
        var registry = HttpContext.RequestServices.GetService<IAdminHandlerRegistry>();
        ViewData[EndpointViewDataKey] = EntityPickerEndpoint.Resolve(field.EntityType, registry);
        ViewData[ContentTypeViewDataKey] = EntityPickerEndpoint.ResolveContentType(field.EntityType, registry);

        var handler = EntityPickerEndpoint.ResolveHandler(field.EntityType, registry);
        var canWrite = handler != null
            && (handler.WriteRoles == null
                ? HttpContext.User.IsInRole(PreviewConstants.AdminRole)
                : handler.WriteRoles.Any(HttpContext.User.IsInRole));
        ViewData[CanWriteViewDataKey] = canWrite;

        return FieldView(field, field);
    }
}
