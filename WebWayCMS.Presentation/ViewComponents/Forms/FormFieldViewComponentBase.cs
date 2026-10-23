using System.Reflection;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.Extensions.DependencyInjection;

using WebWayCMS.Attributes;
using WebWayCMS.Forms;
using WebWayCMS.Services;

namespace WebWayCMS.ViewComponents.Forms;

/// <summary>
/// Base class for form field view components. Renders the view for the field's
/// <see cref="FormFieldRenderMode"/>, which is derived, never chosen: read vs. write from
/// <see cref="FormFieldContext.Mode"/>, admin vs. public from the request path. The view file
/// (<c>{mode}.{variant}.cshtml</c>, variant from <see cref="FormPropertyInfo.FieldViewName"/>) is
/// resolved through the <see cref="IFormFieldViewResolver"/> fallback chain, pulled from
/// <c>HttpContext.RequestServices</c> on first use so derived classes keep their own constructors.
/// </summary>
public abstract class FormFieldViewComponentBase : ViewComponent
{
    private IFormFieldViewResolver? _viewResolver;

    public virtual IViewComponentResult Invoke(FormFieldContext field) => FieldView(field, field);

    /// <summary>
    /// The component name as MVC sees it (honours <c>[ViewComponent(Name = ...)]</c>), which is
    /// also the view directory name.
    /// </summary>
    protected string ComponentName => ViewComponentConventions.GetComponentName(GetType().GetTypeInfo());

    /// <summary>
    /// The active render mode: read vs. write from the field, admin vs. public from the request path.
    /// </summary>
    protected FormFieldRenderMode RenderModeFor(FormFieldContext field)
    {
        var isAdmin = AdminPathPrefix.Matches(HttpContext.Request.Path.ToString());
        var isRead = field.Mode == FormFieldMode.Read;

        return (isAdmin, isRead) switch
        {
            (true, true) => FormFieldRenderMode.AdminRead,
            (true, false) => FormFieldRenderMode.AdminWrite,
            (false, true) => FormFieldRenderMode.Read,
            _ => FormFieldRenderMode.Write
        };
    }

    /// <summary>
    /// Renders <paramref name="model"/> with the view the fallback chain resolves for this field.
    /// Throws when the component has no view anywhere on the chain: a silently missing input on a
    /// model-bound form would drop that value on save.
    /// </summary>
    protected IViewComponentResult FieldView(FormFieldContext field, object model)
    {
        var mode = RenderModeFor(field);
        var variant = field.Property.FieldViewName;
        var viewName = ViewResolver.Resolve(ComponentName, mode, variant)
            ?? throw new InvalidOperationException(
                $"Form component '{ComponentName}' has no view for render mode '{mode.ToFilePrefix()}' " +
                $"(variant '{(string.IsNullOrWhiteSpace(variant) ? "default" : variant)}') or any of its fallbacks.");

        return View(viewName, model);
    }

    private IFormFieldViewResolver ViewResolver
        => _viewResolver ??= HttpContext.RequestServices.GetRequiredService<IFormFieldViewResolver>();
}
