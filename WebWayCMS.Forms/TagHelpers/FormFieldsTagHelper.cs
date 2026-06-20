using WebWayCMS.Attributes;
using WebWayCMS.Forms;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace WebWayCMS.TagHelpers;

/// <summary>
/// Tag helper that renders Bulma-styled form fields from <see cref="FormPropertyAttribute"/> metadata.
/// Delegates to <see cref="FormFieldsHtmlBuilder"/>, which is shared with the Blazor
/// <c>FormFields</c> component so both produce identical markup.
/// </summary>
/// <example>
/// <![CDATA[<form-fields for="@Model" />]]>
/// </example>
[HtmlTargetElement("form-fields", TagStructure = TagStructure.WithoutEndTag)]
public class FormFieldsTagHelper : TagHelper
{
    /// <summary>
    /// The model instance to generate form fields for.
    /// </summary>
    [HtmlAttributeName("for")]
    public object? For { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null; // Don't render a wrapping element
        output.Content.SetHtmlContent(FormFieldsHtmlBuilder.Build(For));
    }
}
