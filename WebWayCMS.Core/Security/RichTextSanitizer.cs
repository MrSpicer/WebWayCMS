using System.Reflection;

using Ganss.Xss;

using WebWayCMS.Attributes;

namespace WebWayCMS.Security;

/// <summary>
/// Sanitizes rich-text (CKEditor) HTML on save so stored content is safe to render with
/// <c>@Html.Raw</c>. Operates generically: any <see cref="string"/> property on an upsert view model
/// marked <c>[FormProperty(EditorType = EditorType.RichText)]</c> is passed through the sanitizer.
/// </summary>
/// <remarks>
/// A single configured <see cref="HtmlSanitizer"/> instance is reused across calls; its
/// <c>Sanitize</c> method is safe for concurrent use. A stateless static entry point is used
/// deliberately: sanitization is a pure transformation shared by every content model's save path
/// (both the admin UI and the MCP tools), so threading it through four model constructors would add
/// churn without value.
/// </remarks>
public static class RichTextSanitizer
{
    private static readonly HtmlSanitizer Sanitizer = new();

    // Reflection results are stable per type; cache the rich-text properties we need to rewrite.
    private static readonly Dictionary<Type, PropertyInfo[]> RichTextPropertyCache = new();
    private static readonly object CacheLock = new();

    /// <summary>
    /// Sanitizes every writable rich-text string property on <paramref name="viewModel"/> in place.
    /// No-op for models with no rich-text fields.
    /// </summary>
    /// <param name="viewModel">The upsert view model whose rich-text fields should be sanitized.</param>
    public static void Sanitize(object viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        foreach (var prop in GetRichTextProperties(viewModel.GetType()))
        {
            if (prop.GetValue(viewModel) is string value && value.Length > 0)
                prop.SetValue(viewModel, SanitizeHtml(value));
        }
    }

    /// <summary>Sanitizes a single HTML fragment, stripping scripts, event handlers and unsafe URIs.</summary>
    /// <param name="html">The untrusted HTML to sanitize.</param>
    /// <returns>The sanitized HTML.</returns>
    public static string SanitizeHtml(string html) => Sanitizer.Sanitize(html);

    private static PropertyInfo[] GetRichTextProperties(Type type)
    {
        lock (CacheLock)
        {
            if (RichTextPropertyCache.TryGetValue(type, out var cached))
                return cached;

            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(string)
                    && p.CanRead
                    && p.CanWrite
                    && p.GetCustomAttribute<FormPropertyAttribute>()?.EditorType == EditorType.RichText)
                .ToArray();

            RichTextPropertyCache[type] = props;
            return props;
        }
    }
}
