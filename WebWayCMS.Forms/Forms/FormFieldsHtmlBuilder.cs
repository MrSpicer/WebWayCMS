using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;

using WebWayCMS.Attributes;

namespace WebWayCMS.Forms;

/// <summary>
/// Builds Bulma-styled form-field HTML from a model's <see cref="FormPropertyAttribute"/> metadata.
/// Shared by the <c>form-fields</c> tag helper (MVC views) and the <c>FormFields</c> Razor component
/// (Blazor) so both produce identical markup.
/// </summary>
public static class FormFieldsHtmlBuilder
{
    /// <summary>
    /// Renders the form fields for <paramref name="model"/>, or an empty string when the model is
    /// null or has no <see cref="FormPropertyAttribute"/>-annotated properties.
    /// </summary>
    public static string Build(object? model)
    {
        if (model == null)
            return string.Empty;

        var modelType = model.GetType();
        var properties = FormPropertyBuilder.BuildPropertyInfos(modelType);

        if (properties.Count == 0)
            return string.Empty;

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
                    sb.AppendLine("</div>"); // close previous group
                }
                currentGroup = prop.Group;
                sb.AppendLine($"<div class=\"form-group-section mt-4\">");
                sb.AppendLine($"<h3 class=\"subtitle is-5\">{HtmlEncoder.Default.Encode(prop.Group)}</h3>");
            }
            else if (string.IsNullOrEmpty(prop.Group) && currentGroup != null)
            {
                sb.AppendLine("</div>"); // close previous group
                currentGroup = null;
            }

            // Check if this starts a horizontal group (GroupWithNext)
            if (prop.GroupWithNext)
            {
                sb.AppendLine("<div class=\"field is-horizontal\">");
                sb.AppendLine("<div class=\"field-body\">");

                // Render this field and all subsequent GroupWithNext fields, plus the final one
                while (i < properties.Count)
                {
                    var groupProp = properties[i];
                    RenderField(sb, groupProp, model);
                    i++;

                    if (!groupProp.GroupWithNext)
                        break;
                }

                sb.AppendLine("</div>");
                sb.AppendLine("</div>");
            }
            else
            {
                RenderField(sb, prop, model);
                i++;
            }
        }

        // Close any open group
        if (currentGroup != null)
        {
            sb.AppendLine("</div>");
        }

        return sb.ToString();
    }

    private static void RenderField(StringBuilder sb, FormPropertyInfo prop, object? model)
    {
        var value = GetModelValue(model, prop.Name);
        var encodedName = HtmlEncoder.Default.Encode(prop.Name);
        var encodedLabel = HtmlEncoder.Default.Encode(prop.Label);

        switch (prop.EditorType)
        {
            case EditorType.Hidden:
                RenderHiddenField(sb, prop, encodedName, value);
                break;
            case EditorType.Checkbox:
                RenderCheckboxField(sb, prop, encodedName, encodedLabel, value);
                break;
            default:
                RenderStandardField(sb, prop, encodedName, encodedLabel, value);
                break;
        }
    }

    private static void RenderHiddenField(StringBuilder sb, FormPropertyInfo prop, string encodedName, object? value)
    {
        var strValue = FormatValue(value, prop.EditorType);
        sb.AppendLine($"<input type=\"hidden\" name=\"{encodedName}\" id=\"{encodedName}\" value=\"{HtmlEncoder.Default.Encode(strValue)}\" />");
    }

    private static void RenderCheckboxField(StringBuilder sb, FormPropertyInfo prop, string encodedName, string encodedLabel, object? value)
    {
        var isChecked = value is true;
        var checkedAttr = isChecked ? " checked" : "";
        var cssClass = !string.IsNullOrEmpty(prop.CssClass) ? $" {prop.CssClass}" : "";

        sb.AppendLine($"<div class=\"field{cssClass}\">");
        sb.AppendLine($"<label class=\"checkbox\">");
        sb.AppendLine($"<input type=\"checkbox\" name=\"{encodedName}\" id=\"{encodedName}\" value=\"true\"{checkedAttr} />");
        sb.AppendLine($" {encodedLabel}{(prop.IsRequired ? " <span class=\"has-text-danger\">*</span>" : "")}");
        sb.AppendLine("</label>");

        if (!string.IsNullOrEmpty(prop.HelpText))
        {
            sb.AppendLine($"<p class=\"help\">{HtmlEncoder.Default.Encode(prop.HelpText)}</p>");
        }

        sb.AppendLine("</div>");
    }

    private static void RenderStandardField(StringBuilder sb, FormPropertyInfo prop, string encodedName, string encodedLabel, object? value)
    {
        var cssClass = !string.IsNullOrEmpty(prop.CssClass) ? $" {prop.CssClass}" : "";

        sb.AppendLine($"<div class=\"field{cssClass}\">");
        sb.AppendLine($"<label class=\"label\" for=\"{encodedName}\">{encodedLabel}{(prop.IsRequired ? " <span class=\"has-text-danger\">*</span>" : "")}</label>");
        sb.AppendLine("<div class=\"control\">");

        switch (prop.EditorType)
        {
            case EditorType.TextArea:
                RenderTextArea(sb, prop, encodedName, value, "textarea");
                break;
            case EditorType.RichText:
                RenderTextArea(sb, prop, encodedName, value, "textarea rich-text-editor");
                break;
            case EditorType.Number:
                RenderNumberInput(sb, prop, encodedName, value);
                break;
            case EditorType.DateTime:
                RenderInput(sb, prop, encodedName, value, "datetime-local");
                break;
            case EditorType.Date:
                RenderInput(sb, prop, encodedName, value, "date");
                break;
            case EditorType.Url:
                RenderInput(sb, prop, encodedName, value, "url");
                break;
            case EditorType.Email:
                RenderInput(sb, prop, encodedName, value, "email");
                break;
            case EditorType.Color:
                RenderInput(sb, prop, encodedName, value, "color");
                break;
            case EditorType.Dropdown:
            case EditorType.ViewPicker:
            case EditorType.PageControllerPicker:
                RenderSelect(sb, prop, encodedName, value);
                break;
            case EditorType.Guid:
                RenderInput(sb, prop, encodedName, value, "text");
                break;
            default: // Text
                RenderInput(sb, prop, encodedName, value, "text");
                break;
        }

        sb.AppendLine("</div>");

        if (!string.IsNullOrEmpty(prop.HelpText))
        {
            var helpId = $"{encodedName}_help";
            sb.AppendLine($"<p class=\"help\" id=\"{helpId}\">{HtmlEncoder.Default.Encode(prop.HelpText)}</p>");
        }

        sb.AppendLine($"<span role=\"alert\" data-valmsg-for=\"{encodedName}\" class=\"has-text-danger\"></span>");
        sb.AppendLine("</div>");
    }

    private static void RenderInput(StringBuilder sb, FormPropertyInfo prop, string encodedName, object? value, string inputType)
    {
        var strValue = FormatValue(value, prop.EditorType);
        var attrs = BuildCommonAttributes(prop, encodedName, strValue);
        sb.AppendLine($"<input class=\"input\" type=\"{inputType}\" {attrs} />");
    }

    private static void RenderNumberInput(StringBuilder sb, FormPropertyInfo prop, string encodedName, object? value)
    {
        var strValue = FormatValue(value, prop.EditorType);
        var attrs = BuildCommonAttributes(prop, encodedName, strValue);

        if (prop.Min.HasValue)
            attrs += $" min=\"{prop.Min.Value}\"";
        if (prop.Max.HasValue)
            attrs += $" max=\"{prop.Max.Value}\"";

        sb.AppendLine($"<input class=\"input\" type=\"number\" {attrs} />");
    }

    private static void RenderTextArea(StringBuilder sb, FormPropertyInfo prop, string encodedName, object? value, string cssClass)
    {
        var strValue = FormatValue(value, prop.EditorType);
        var attrs = $"name=\"{encodedName}\" id=\"{encodedName}\"";

        if (!string.IsNullOrEmpty(prop.Placeholder))
            attrs += $" placeholder=\"{HtmlEncoder.Default.Encode(prop.Placeholder)}\"";
        if (prop.IsRequired && prop.EditorType != EditorType.RichText)
            attrs += " required aria-required=\"true\"";
        // Note: aria-required is intentionally omitted for RichText — CKEditor replaces the textarea,
        // so native HTML validation does not fire on the underlying textarea element.
        if (prop.MaxLength.HasValue)
            attrs += $" maxlength=\"{prop.MaxLength.Value}\"";
        if (!string.IsNullOrEmpty(prop.HelpText))
            attrs += $" aria-describedby=\"{encodedName}_help\"";

        sb.AppendLine($"<textarea class=\"{cssClass}\" {attrs} rows=\"6\">{HtmlEncoder.Default.Encode(strValue)}</textarea>");
    }

    private static void RenderSelect(StringBuilder sb, FormPropertyInfo prop, string encodedName, object? value)
    {
        var strValue = FormatValue(value, prop.EditorType);
        var requiredAttr = prop.IsRequired ? " required aria-required=\"true\"" : "";
        var describedByAttr = !string.IsNullOrEmpty(prop.HelpText) ? $" aria-describedby=\"{encodedName}_help\"" : "";

        var populatedClientSide = prop.EditorType == EditorType.ViewPicker || prop.EditorType == EditorType.PageControllerPicker;
        var dataCurrentValue = populatedClientSide && !string.IsNullOrEmpty(strValue)
            ? $" data-current-value=\"{HtmlEncoder.Default.Encode(strValue)}\""
            : "";

        var pickerMarker = prop.EditorType == EditorType.PageControllerPicker ? " data-page-controller-picker" : "";

        sb.AppendLine($"<div class=\"select is-fullwidth\">");
        sb.AppendLine($"<select name=\"{encodedName}\" id=\"{encodedName}\"{requiredAttr}{describedByAttr}{dataCurrentValue}{pickerMarker}>");
        sb.AppendLine("<option value=\"\">-- Select --</option>");

        if (prop.DropdownOptions.Count > 0)
        {
            foreach (var (optValue, optLabel) in prop.DropdownOptions)
            {
                var selected = string.Equals(optValue, strValue, StringComparison.OrdinalIgnoreCase) ? " selected" : "";
                sb.AppendLine($"<option value=\"{HtmlEncoder.Default.Encode(optValue)}\"{selected}>{HtmlEncoder.Default.Encode(optLabel)}</option>");
            }
        }

        sb.AppendLine("</select>");
        sb.AppendLine("</div>");
    }

    private static string BuildCommonAttributes(FormPropertyInfo prop, string encodedName, string value)
    {
        var attrs = $"name=\"{encodedName}\" id=\"{encodedName}\" value=\"{HtmlEncoder.Default.Encode(value)}\"";

        if (!string.IsNullOrEmpty(prop.Placeholder))
            attrs += $" placeholder=\"{HtmlEncoder.Default.Encode(prop.Placeholder)}\"";
        if (prop.IsRequired)
            attrs += " required aria-required=\"true\"";
        if (prop.MaxLength.HasValue)
            attrs += $" maxlength=\"{prop.MaxLength.Value}\"";
        if (!string.IsNullOrEmpty(prop.Pattern))
            attrs += $" pattern=\"{HtmlEncoder.Default.Encode(prop.Pattern)}\"";
        if (!string.IsNullOrEmpty(prop.HelpText))
            attrs += $" aria-describedby=\"{encodedName}_help\"";

        return attrs;
    }

    private static string FormatValue(object? value, EditorType editorType)
    {
        if (value == null)
            return string.Empty;

        return editorType switch
        {
            EditorType.DateTime when value is DateTime dt => dt == DateTime.MinValue ? string.Empty : dt.ToString("yyyy-MM-ddTHH:mm"),
            EditorType.DateTime when value is DateTimeOffset dto => dto == DateTimeOffset.MinValue ? string.Empty : dto.ToString("yyyy-MM-ddTHH:mm"),
            EditorType.Date when value is DateTime dt => dt == DateTime.MinValue ? string.Empty : dt.ToString("yyyy-MM-dd"),
            EditorType.Date when value is DateOnly d => d == DateOnly.MinValue ? string.Empty : d.ToString("yyyy-MM-dd"),
            EditorType.Guid when value is Guid g => g == Guid.Empty ? string.Empty : g.ToString(),
            _ => value.ToString() ?? string.Empty
        };
    }

    // Defensive null-guards here are unreachable through Build (the model is null-checked and the
    // property name always originates from the same model type), so this helper is excluded from
    // coverage rather than padded with unreachable tests.
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static object? GetModelValue(object? model, string propertyName)
    {
        if (model == null)
            return null;

        var prop = model.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        return prop?.GetValue(model);
    }
}
