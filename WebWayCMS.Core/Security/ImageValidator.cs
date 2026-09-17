namespace WebWayCMS.Security;

/// <summary>
/// The outcome of validating an uploaded image. <see cref="ContentType"/> is the type sniffed from
/// the file's own bytes, which is what callers should store — never the client-declared header.
/// </summary>
public readonly record struct ImageValidationResult(
    bool Success,
    string? ErrorMessage,
    string? ContentType,
    int Width,
    int Height);

/// <summary>
/// Verifies that uploaded bytes really are an image of a supported format, and reads the pixel
/// dimensions straight out of the file header.
/// </summary>
/// <remarks>
/// Static and stateless by design, mirroring <see cref="RichTextSanitizer"/> and
/// <see cref="ModelValidator"/> — validation is a pure function shared by every upload path.
/// <para>
/// Formats are identified by magic bytes rather than by the declared content type or the file
/// extension, both of which are attacker-controlled. Dimensions are parsed from the header rather
/// than decoded, which is what lets the CMS store width/height (and so render an <c>img</c> with
/// explicit dimensions, avoiding layout shift) without taking a dependency on an imaging library.
/// </para>
/// <para>
/// SVG is rejected outright: it is executable content, and serving it same-origin would place
/// script inside the site's own CSP origin.
/// </para>
/// </remarks>
public static class ImageValidator
{
    public const string PngContentType = "image/png";
    public const string JpegContentType = "image/jpeg";
    public const string GifContentType = "image/gif";
    public const string WebpContentType = "image/webp";

    internal const string EmptyMessage = "The file is empty.";
    internal const string TooLargeMessage = "The image is larger than the {0} MB upload limit.";
    internal const string SvgRejectedMessage =
        "SVG images are not accepted because they can contain scripts. Use PNG, JPEG, GIF, or WebP.";
    internal const string UnsupportedMessage =
        "That file is not a supported image. Use PNG, JPEG, GIF, or WebP.";
    internal const string CorruptMessage = "The image file is truncated or corrupt.";
    internal const string NotAllowedMessage = "{0} images are not accepted by this site.";

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Validates <paramref name="bytes"/> as an image, enforcing <paramref name="maxBytes"/> and,
    /// when supplied and non-empty, restricting to <paramref name="allowedContentTypes"/>.
    /// </summary>
    public static ImageValidationResult Validate(
        byte[]? bytes,
        long maxBytes,
        IReadOnlyCollection<string>? allowedContentTypes = null)
    {
        if (bytes == null || bytes.Length == 0)
            return new ImageValidationResult(false, EmptyMessage, null, 0, 0);

        if (bytes.LongLength > maxBytes)
        {
            var megabytes = Math.Max(1, maxBytes / (1024 * 1024));
            return new ImageValidationResult(false, string.Format(TooLargeMessage, megabytes), null, 0, 0);
        }

        if (LooksLikeSvg(bytes))
            return new ImageValidationResult(false, SvgRejectedMessage, null, 0, 0);

        var contentType = SniffContentType(bytes);
        if (contentType == null)
            return new ImageValidationResult(false, UnsupportedMessage, null, 0, 0);

        if (allowedContentTypes is { Count: > 0 } && !allowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            return new ImageValidationResult(false, string.Format(NotAllowedMessage, contentType), null, 0, 0);

        var dimensions = ReadDimensions(bytes, contentType);
        if (dimensions == null)
            return new ImageValidationResult(false, CorruptMessage, null, 0, 0);

        return new ImageValidationResult(true, null, contentType, dimensions.Value.Width, dimensions.Value.Height);
    }

    /// <summary>Identifies the format from its magic bytes, or <c>null</c> when unrecognized.</summary>
    public static string? SniffContentType(byte[] bytes)
    {
        if (bytes == null) return null;

        if (StartsWith(bytes, PngSignature))
            return PngContentType;

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return JpegContentType;

        if (bytes.Length >= 6 && bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F'
            && bytes[3] == (byte)'8' && (bytes[4] == (byte)'7' || bytes[4] == (byte)'9') && bytes[5] == (byte)'a')
            return GifContentType;

        if (bytes.Length >= 12 && Ascii(bytes, 0, "RIFF") && Ascii(bytes, 8, "WEBP"))
            return WebpContentType;

        return null;
    }

    // Only ever called with a content type SniffContentType just returned, so WebP is the
    // fallthrough rather than a catch-all that could never run.
    private static (int Width, int Height)? ReadDimensions(byte[] bytes, string contentType) => contentType switch
    {
        PngContentType => ReadPng(bytes),
        JpegContentType => ReadJpeg(bytes),
        GifContentType => ReadGif(bytes),
        _ => ReadWebp(bytes)
    };

    // PNG: the IHDR chunk is mandatory and always first, so width/height sit at fixed offsets.
    private static (int Width, int Height)? ReadPng(byte[] b)
    {
        if (b.Length < 24) return null;
        if (!Ascii(b, 12, "IHDR")) return null;

        var width = BigEndianInt32(b, 16);
        var height = BigEndianInt32(b, 20);
        return Valid(width, height);
    }

    // GIF: fixed-position little-endian logical screen descriptor.
    private static (int Width, int Height)? ReadGif(byte[] b)
    {
        if (b.Length < 10) return null;

        var width = b[6] | (b[7] << 8);
        var height = b[8] | (b[9] << 8);
        return Valid(width, height);
    }

    // JPEG: segments are variable-length, so walk the marker chain to the frame header (SOFn).
    private static (int Width, int Height)? ReadJpeg(byte[] b)
    {
        var i = 2;
        while (i + 3 < b.Length)
        {
            if (b[i] != 0xFF)
            {
                i++;
                continue;
            }

            var marker = b[i + 1];

            // Padding and standalone markers carry no length field.
            if (marker == 0xFF)
            {
                i++;
                continue;
            }

            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD9))
            {
                i += 2;
                continue;
            }

            var length = (b[i + 2] << 8) | b[i + 3];
            if (length < 2) return null;

            // SOF0..SOF15 carry the frame dimensions; C4/C8/CC are DHT/JPG/DAC, not frame headers.
            if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
            {
                if (i + 9 > b.Length) return null;

                var height = (b[i + 5] << 8) | b[i + 6];
                var width = (b[i + 7] << 8) | b[i + 8];
                return Valid(width, height);
            }

            i += 2 + length;
        }

        return null;
    }

    // WebP has three container variants, each storing the canvas size differently.
    private static (int Width, int Height)? ReadWebp(byte[] b)
    {
        // No length guard needed: Ascii() bounds-checks, so a file too short to hold a chunk
        // header simply matches nothing and falls through to null.
        if (Ascii(b, 12, "VP8 "))
        {
            if (b.Length < 30) return null;
            // Frame tag is followed by a fixed sync code; without it the frame header is not lossy VP8.
            if (b[23] != 0x9D || b[24] != 0x01 || b[25] != 0x2A) return null;

            var width = (b[26] | (b[27] << 8)) & 0x3FFF;
            var height = (b[28] | (b[29] << 8)) & 0x3FFF;
            return Valid(width, height);
        }

        if (Ascii(b, 12, "VP8L"))
        {
            if (b.Length < 25) return null;
            if (b[20] != 0x2F) return null;

            var bits = b[21] | (b[22] << 8) | (b[23] << 16) | (b[24] << 24);
            var width = (bits & 0x3FFF) + 1;
            var height = ((bits >> 14) & 0x3FFF) + 1;
            return Valid(width, height);
        }

        if (Ascii(b, 12, "VP8X"))
        {
            if (b.Length < 30) return null;

            var width = (b[24] | (b[25] << 8) | (b[26] << 16)) + 1;
            var height = (b[27] | (b[28] << 8) | (b[29] << 16)) + 1;
            return Valid(width, height);
        }

        return null;
    }

    // An SVG is text, so sniff the leading non-whitespace for an XML or <svg> opening.
    private static bool LooksLikeSvg(byte[] b)
    {
        var limit = Math.Min(b.Length, 1024);
        var start = 0;

        // Skip a UTF-8 BOM and any leading whitespace before looking at the first real character.
        if (limit >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
            start = 3;

        while (start < limit && (b[start] == (byte)' ' || b[start] == (byte)'\t'
            || b[start] == (byte)'\r' || b[start] == (byte)'\n'))
            start++;

        if (start >= limit || b[start] != (byte)'<')
            return false;

        var text = System.Text.Encoding.UTF8.GetString(b, start, limit - start);
        return text.Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }

    private static (int Width, int Height)? Valid(int width, int height) =>
        width > 0 && height > 0 ? (width, height) : null;

    private static int BigEndianInt32(byte[] b, int offset) =>
        (b[offset] << 24) | (b[offset + 1] << 16) | (b[offset + 2] << 8) | b[offset + 3];

    private static bool StartsWith(byte[] b, byte[] signature)
    {
        if (b.Length < signature.Length) return false;

        for (var i = 0; i < signature.Length; i++)
            if (b[i] != signature[i]) return false;

        return true;
    }

    private static bool Ascii(byte[] b, int offset, string value)
    {
        if (b.Length < offset + value.Length) return false;

        for (var i = 0; i < value.Length; i++)
            if (b[offset + i] != (byte)value[i]) return false;

        return true;
    }
}
