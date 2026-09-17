namespace WebWayCMS.Media;

/// <summary>
/// Builds the public URL for a stored media blob.
/// </summary>
/// <remarks>
/// The URL carries the content hash as its identity and the original file name only as a readable,
/// sanitized tail. Because the address is the content hash, the bytes it names can never change,
/// which is what lets the serving endpoint mark responses immutable.
/// </remarks>
public static class MediaUrl
{
    /// <summary>The fixed path prefix the media endpoint is served from.</summary>
    public const string BasePath = "/media";

    internal const string FallbackFileName = "image";

    /// <summary>Returns the public URL for a blob, or an empty string when the hash is missing.</summary>
    public static string For(string? hash, string? originalFileName = null)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return string.Empty;

        var name = SafeFileName(originalFileName);
        return $"{BasePath}/{hash}/{name}";
    }

    /// <summary>
    /// Reduces a stored file name to a conservative set of URL-safe characters. The result is
    /// cosmetic — the hash segment alone identifies the blob — so anything unexpected is dropped
    /// rather than escaped.
    /// </summary>
    internal static string SafeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return FallbackFileName;

        var trimmed = fileName.Trim();
        var lastSlash = trimmed.LastIndexOfAny(['/', '\\']);
        if (lastSlash >= 0)
            trimmed = trimmed[(lastSlash + 1)..];

        var buffer = new System.Text.StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
        {
            if (char.IsAsciiLetterOrDigit(c) || c == '.' || c == '-' || c == '_')
                buffer.Append(c);
            else if (c == ' ')
                buffer.Append('-');
        }

        var result = buffer.ToString().Trim('.', '-');
        return result.Length == 0 ? FallbackFileName : result;
    }
}
