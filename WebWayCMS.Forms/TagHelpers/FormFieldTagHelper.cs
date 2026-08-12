using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

using WebWayCMS.Forms;

namespace WebWayCMS.TagHelpers;

/// <summary>
/// Emits the shared Bulma chrome around child content:
/// <c>&lt;div class="field"&gt;</c>, <c>&lt;label&gt;</c>, required asterisk,
/// <c>&lt;div class="control"&gt;</c>, help text, and validation message span.
/// Set <c>chrome="none"</c> to suppress (used by Checkbox component).
/// </summary>
/// <example>
/// <![CDATA[<form-field field="@Model" chrome="none"> … </form-field>]]>
/// </example>
[HtmlTargetElement("form-field")]
public class FormFieldTagHelper : TagHelper
{
    /// <summary>
    /// The <see cref="FormFieldContext"/> that carries the field's metadata.
    /// </summary>
    [HtmlAttributeName("field")]
    public FormFieldContext? Field { get; set; }

    /// <summary>
    /// When set to "none", the tag helper renders only its child content without any chrome.
    /// </summary>
    [HtmlAttributeName("chrome")]
    public string Chrome { get; set; } = string.Empty;

    [ViewContext, HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (Field == null)
        {
            output.SuppressOutput();
            return;
        }

        var childContent = (await output.GetChildContentAsync()).GetContent(HtmlEncoder.Default);
        var isNoChrome = string.Equals(Chrome, "none", StringComparison.OrdinalIgnoreCase);

        if (isNoChrome)
        {
            output.TagName = null;
            output.Content.SetHtmlContent(childContent);
            return;
        }

        output.TagName = null;

        var cssClass = !string.IsNullOrEmpty(Field.Property.CssClass) ? $" {HtmlEncoder.Default.Encode(Field.Property.CssClass)}" : "";
        var encodedName = HtmlEncoder.Default.Encode(Field.Name);
        var encodedLabel = HtmlEncoder.Default.Encode(Field.Label);
        var requiredMarker = Field.IsRequired ? " <span class=\"has-text-danger\">*</span>" : "";

        var html = $"<div class=\"field{cssClass}\">";
        html += $"<label class=\"label\" for=\"{encodedName}\">{encodedLabel}{requiredMarker}</label>";
        html += $"<div class=\"control\">";
        html += childContent;
        html += "</div>";

        if (!string.IsNullOrEmpty(Field.HelpText))
        {
            var helpId = $"{encodedName}_help";
            html += $"<p class=\"help\" id=\"{helpId}\">{HtmlEncoder.Default.Encode(Field.HelpText)}</p>";
        }

        var errorText = GetModelStateError();
        html += $"<span role=\"alert\" data-valmsg-for=\"{encodedName}\" class=\"has-text-danger\">{errorText}</span>";
        html += "</div>";

        output.Content.SetHtmlContent(html);
    }

    private string GetModelStateError()
    {
        if (ViewContext?.ModelState == null)
            return string.Empty;

        if (ViewContext.ModelState.TryGetValue(Field!.Name, out var entry) && entry.Errors.Count > 0)
        {
            var firstError = entry.Errors.FirstOrDefault(e => !string.IsNullOrEmpty(e.ErrorMessage));
            if (firstError != null)
                return HtmlEncoder.Default.Encode(firstError.ErrorMessage);
        }

        return string.Empty;
    }
}
