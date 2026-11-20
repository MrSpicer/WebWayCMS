using System.Text;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services.ContentTypes;

/// <summary>
/// One content type the CMS knows about: the key its content store writes to
/// <see cref="ContentNode.ContentTypeKey"/>, its CLR type, and optional default names. Registered as a singleton
/// by every content store registration (CMS and host types alike) and collected by
/// <see cref="IContentTypeRegistry"/>.
/// </summary>
public sealed record ContentTypeDescriptor
{
    /// <summary>The length of <see cref="ContentNode.ContentTypeKey"/>.</summary>
    public const int MaxKeyLength = 64;

    public ContentTypeDescriptor(string key, Type clrType, string? displayName = null, string? pluralDisplayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(clrType);
        if (key.Length > MaxKeyLength)
            throw new ArgumentException($"Content type key '{key}' is longer than {MaxKeyLength} characters.", nameof(key));
        if (!typeof(IVersionedContent).IsAssignableFrom(clrType))
            throw new ArgumentException($"Content type '{key}': {clrType.FullName} does not implement {nameof(IVersionedContent)}.", nameof(clrType));

        Key = key;
        ClrType = clrType;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName;
        PluralDisplayName = string.IsNullOrWhiteSpace(pluralDisplayName) ? null : pluralDisplayName;
    }

    public string Key { get; }

    public Type ClrType { get; }

    /// <summary>The singular name a new registration starts with, when the code gives one.</summary>
    public string? DisplayName { get; }

    /// <summary>The plural name a new registration starts with, when the code gives one.</summary>
    public string? PluralDisplayName { get; }

    /// <summary>
    /// A name from the CLR type when nothing better is known: the type name without a "DTO" suffix, split into
    /// words ("ArticleListDTO" → "Article List", "FAQTopicDTO" → "FAQ Topic").
    /// </summary>
    public static string DeriveDisplayName(Type clrType)
    {
        ArgumentNullException.ThrowIfNull(clrType);
        var name = clrType.Name;
        if (name.Length > 3 && name.EndsWith("DTO", StringComparison.OrdinalIgnoreCase))
            name = name[..^3];

        var words = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            var startsWord = i > 0 && char.IsUpper(c)
                && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1])));
            if (startsWord)
                words.Append(' ');
            words.Append(c);
        }
        return words.ToString();
    }

    /// <summary>
    /// An English plural for a default name ("Page" → "Pages", "Category" → "Categories", "FAQ" → "FAQs",
    /// "Address" → "Addresses"). Admins can correct it on the registration. A blank name stays as it is.
    /// </summary>
    public static string Pluralize(string? singular)
    {
        if (string.IsNullOrWhiteSpace(singular))
            return singular ?? string.Empty;
        var lastWordUpper = singular.Split(' ')[^1].All(c => !char.IsLetter(c) || char.IsUpper(c));
        if (lastWordUpper)
            return singular + "s";
        if (singular.EndsWith("s", StringComparison.Ordinal) || singular.EndsWith("x", StringComparison.Ordinal)
            || singular.EndsWith("ch", StringComparison.Ordinal) || singular.EndsWith("sh", StringComparison.Ordinal))
            return singular + "es";
        if (singular.Length > 1 && singular.EndsWith("y", StringComparison.Ordinal) && !"aeiou".Contains(singular[^2]))
            return singular[..^1] + "ies";
        return singular + "s";
    }
}
