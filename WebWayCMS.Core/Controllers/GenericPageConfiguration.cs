using WebWayCMS.Attributes;
using WebWayCMS.Security;

namespace WebWayCMS.Controllers;

/// <summary>
/// Configuration model for generic pages.
/// </summary>
public class GenericPageConfiguration
{
    [FormProperty("Custom CSS", EditorType.TextArea,
        HelpText = "Inline CSS styles to be injected into the page head. Do not include <style> tags.",
        Order = 10,
        FormComponent = "TextArea")]
    public string Style { get; set; } = string.Empty;

    [FormProperty("Meta Tags", EditorType.TextArea,
        HelpText = "HTML meta tags for SEO and social media. Include full tags, e.g., <meta name=\"description\" content=\"...\">",
        Order = 30,
        FormComponent = "TextArea")]
    public string Meta { get; set; } = string.Empty;

    /// <summary>
    /// A copy safe to hand to the view, which injects both fields into the page head with
    /// <c>@Html.Raw</c>. They are TextArea fields, outside the save-time rich-text pipeline, so they
    /// are neutralized at render time (which also covers values stored before this existed).
    /// </summary>
    public GenericPageConfiguration SanitizedForRender() => new()
    {
        Style = HeadMarkupSanitizer.SanitizeCss(Style),
        Meta = HeadMarkupSanitizer.SanitizeMeta(Meta)
    };
}
