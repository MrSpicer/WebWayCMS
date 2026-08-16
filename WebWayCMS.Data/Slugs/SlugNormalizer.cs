using System.Globalization;
using System.Text;

namespace WebWayCMS.Data.Slugs;

/// <summary>
/// The single source of truth for slug normalization, used by <see cref="Services.ContentStore{T}"/>
/// (write path) and the page model's slug-collision checks (comparison path) so the two agree on the
/// shape of a stored slug.
/// </summary>
public static class SlugNormalizer
{
    /// <summary>
    /// Normalizes a slug source to its URL-segment form: lowercase, whitespace/punctuation collapsed to
    /// single hyphens, accented Latin folded to ASCII, and nothing outside <c>[a-z0-9-]</c> kept. When
    /// slugification yields nothing (e.g. a purely non-Latin or punctuation-only source), the result falls
    /// back to <see cref="Uri.EscapeDataString(string)"/> so the slug stays routable. The operation is
    /// idempotent — normalizing an already-normalized slug returns it unchanged.
    /// </summary>
    public static string Normalize(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return string.Empty;

        // A slug that is already fully percent-encoded (the fallback's own output) is passed through
        // unchanged, which is what keeps Normalize idempotent for non-Latin sources.
        if (IsFullyPercentEncoded(source))
            return source;

        var slug = Slugify(source);
        return slug.Length == 0 ? Uri.EscapeDataString(source) : slug;
    }

    private static string Slugify(string source)
    {
        // Normalize to decomposed form so accented Latin folds to ASCII: "Café Ünïcode" -> "cafe-unicode".
        // Combining marks (NonSpacingMark) are dropped; characters with no decomposition (ø, ł, ß) fall
        // through to a hyphen, as a transliteration map is intentionally out of scope.
        var decomposed = source.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingHyphen = false;

        foreach (var c in decomposed)
        {
            if (char.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                pendingHyphen = false;
            }
            else if (!pendingHyphen)
            {
                builder.Append('-');
                pendingHyphen = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static bool IsFullyPercentEncoded(string source)
    {
        var hasEscape = false;
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            if (c == '%')
            {
                hasEscape = true;
                if (i + 2 >= source.Length || !IsHex(source[i + 1]) || !IsHex(source[i + 2]))
                    return false;
                i += 2;
            }
            else if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~'))
            {
                return false;
            }
        }

        return hasEscape;
    }

    private static bool IsHex(char c)
        => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}
