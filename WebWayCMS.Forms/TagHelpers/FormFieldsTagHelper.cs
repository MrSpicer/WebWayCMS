using System.Text;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;

using WebWayCMS.Forms;

namespace WebWayCMS.TagHelpers;

/// <summary>
/// Tag helper that renders Bulma-styled form fields by delegating to view components
/// resolved through <see cref="IFormComponentResolver"/>.
/// </summary>
/// <example>
/// <![CDATA[<form-fields for="@Model" />]]>
/// <![CDATA[<form-fields for="@Model" mode="read" />]]>
/// <![CDATA[<form-fields for="@Model" binding="json" />]]>
/// </example>
[HtmlTargetElement("form-fields", TagStructure = TagStructure.WithoutEndTag)]
public class FormFieldsTagHelper : TagHelper
{
    private readonly IFormComponentResolver _resolver;

    public FormFieldsTagHelper(IFormComponentResolver resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    [HtmlAttributeNotBound, ViewContext]
    public ViewContext ViewContext { get; set; } = null!;

    /// <summary>
    /// The model instance to generate form fields for.
    /// </summary>
    [HtmlAttributeName("for")]
    public object? For { get; set; }

    /// <summary>
    /// Rendering mode: Write (edit) or Read (display). Default is Write.
    /// </summary>
    [HtmlAttributeName("mode")]
    public FormFieldMode Mode { get; set; } = FormFieldMode.Write;

    /// <summary>
    /// Binding mode: Model (name attribute) or Json (data-prop attribute, no name). Default is Model.
    /// </summary>
    [HtmlAttributeName("binding")]
    public FormFieldBinding Binding { get; set; } = FormFieldBinding.Model;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;

        if (For == null)
            return;

        var modelType = For.GetType();
        var model = For;
        var properties = FormPropertyBuilder.BuildPropertyInfos(modelType);

        if (properties.Count == 0)
            return;

        var vch = ViewContext.HttpContext.RequestServices.GetRequiredService<IViewComponentHelper>();
        ((IViewContextAware)vch).Contextualize(ViewContext);

        var sb = new StringBuilder();
        var currentGroup = (string?)null;
        var i = 0;

        while (i < properties.Count)
        {
            var prop = properties[i];

            // Render group heading if entering a new group
            if (!string.IsNullOrEmpty(prop.Group) && prop.Group != currentGroup)
            {
                if (currentGroup != null)
                {
                    sb.AppendLine("</div>");
                }
                currentGroup = prop.Group;
                sb.AppendLine($"<div class=\"form-group-section mt-4\">");
                sb.AppendLine($"<h3 class=\"subtitle is-5\">{HtmlEncoder.Default.Encode(prop.Group)}</h3>");
            }
            else if (string.IsNullOrEmpty(prop.Group) && currentGroup != null)
            {
                sb.AppendLine("</div>");
                currentGroup = null;
            }

            // Check if this starts a horizontal group (GroupWithNext)
            if (prop.GroupWithNext)
            {
                var groupElements = new List<string>();

                while (i < properties.Count)
                {
                    var groupProp = properties[i];
                    var fieldContext = BuildFieldContext(groupProp, model);
                    var componentInfo = _resolver.Resolve(groupProp);

                    if (componentInfo != null)
                    {
                        var viewName = ResolveViewName(componentInfo);
                        var fieldContent = await vch.InvokeAsync(componentInfo.ViewComponentName, new { field = fieldContext, viewName });
                        if (fieldContent is IHtmlContent hc)
                        {
                            using var writer = new StringWriter();
                            hc.WriteTo(writer, HtmlEncoder.Default);
                            groupElements.Add(writer.ToString());
                        }
                    }

                    i++;
                    if (!groupProp.GroupWithNext)
                        break;
                }

                if (groupElements.Count > 0)
                {
                    sb.AppendLine("<div class=\"field is-horizontal\">");
                    sb.AppendLine("<div class=\"field-body\">");
                    foreach (var html in groupElements)
                    {
                        sb.Append(html);
                    }
                    sb.AppendLine("</div>");
                    sb.AppendLine("</div>");
                }
            }
            else
            {
                var fieldContext = BuildFieldContext(prop, model);
                var componentInfo = _resolver.Resolve(prop);

                if (componentInfo != null)
                {
                    var viewName = ResolveViewName(componentInfo);
                    var fieldContent = await vch.InvokeAsync(componentInfo.ViewComponentName, new { field = fieldContext, viewName });
                    if (fieldContent is IHtmlContent hc)
                    {
                        using var writer = new StringWriter();
                        hc.WriteTo(writer, HtmlEncoder.Default);
                        sb.Append(writer.ToString());
                    }
                }

                i++;
            }
        }

        // Close any open group
        if (currentGroup != null)
        {
            sb.AppendLine("</div>");
        }

        output.Content.SetHtmlContent(sb.ToString());
    }

    private string ResolveViewName(FormComponentInfo componentInfo)
        => Mode == FormFieldMode.Read ? componentInfo.ReadViewName : componentInfo.WriteViewName;

    private FormFieldContext BuildFieldContext(FormPropertyInfo prop, object model)
    {
        var value = GetModelValue(model, prop.Name);
        var nameAttr = Binding == FormFieldBinding.Model ? prop.Name : string.Empty;

        return new FormFieldContext
        {
            Property = prop,
            Value = value,
            Mode = Mode,
            InputName = nameAttr,
            ElementId = prop.Name,
            JsonBound = Binding == FormFieldBinding.Json
        };
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static object? GetModelValue(object? model, string propertyName)
    {
        if (model == null)
            return null;

        var prop = model.GetType().GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        return prop?.GetValue(model);
    }
}
