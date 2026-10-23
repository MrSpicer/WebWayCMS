using Ganss.Xss;

namespace WebWayCMS.Security;

/// <summary>
/// Render-time neutralization of the raw page-head fields (<c>GenericPageConfiguration.Meta</c> and
/// <c>.Style</c>), which are plain TextArea fields emitted with <c>@Html.Raw</c>. The rich-text
/// sanitizer's body-content allow-list would strip every <c>&lt;meta&gt;</c> tag and HTML-encode CSS,
/// so each field gets rules for what it actually holds.
/// </summary>
public static class HeadMarkupSanitizer
{
    private static readonly HtmlSanitizer MetaSanitizer = CreateMetaSanitizer();

    /// <summary>
    /// Keeps only <c>&lt;meta&gt;</c> and <c>&lt;link&gt;</c> tags with descriptive attributes. Anything else
    /// (scripts, event handlers, <c>http-equiv</c> — whose <c>refresh</c> can redirect — and non-http(s)
    /// <c>href</c>s) is removed.
    /// </summary>
    public static string SanitizeMeta(string? html)
        => string.IsNullOrWhiteSpace(html) ? string.Empty : MetaSanitizer.Sanitize(html);

    /// <summary>
    /// Makes custom CSS safe to emit inside <c>&lt;style&gt;</c>. A style element's text is raw (entities
    /// are not decoded), so the CSS is kept verbatim except for <c>&lt;/</c>, the only sequence that can
    /// end the element. It becomes <c>&lt;\/</c>, which CSS reads as the same characters.
    /// </summary>
    public static string SanitizeCss(string? css)
        => string.IsNullOrEmpty(css) ? string.Empty : css.Replace("</", "<\\/", StringComparison.Ordinal);

    private static HtmlSanitizer CreateMetaSanitizer()
    {
        var sanitizer = new HtmlSanitizer();

        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.Add("meta");
        sanitizer.AllowedTags.Add("link");

        sanitizer.AllowedAttributes.Clear();
        foreach (var attribute in new[]
                 {
                     "name", "content", "property", "charset", "itemprop",
                     "rel", "href", "hreflang", "type", "sizes", "media", "title"
                 })
            sanitizer.AllowedAttributes.Add(attribute);

        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedAtRules.Clear();

        // href is a URI attribute, so the default http/https scheme allow-list applies to it.
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("http");
        sanitizer.AllowedSchemes.Add("https");

        return sanitizer;
    }
}
