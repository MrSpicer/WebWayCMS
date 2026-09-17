using NUnit.Framework;

using WebWayCMS.Security;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class ImageValidatorTests
{
    private const long MaxBytes = 10 * 1024 * 1024;

    // ---- builders for minimal but structurally valid files -------------------------------------

    private static byte[] Png(int width = 3, int height = 7, bool withIhdr = true, int length = 24)
    {
        var b = new byte[length];
        byte[] sig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Array.Copy(sig, b, Math.Min(sig.Length, length));
        if (length >= 16 && withIhdr)
        {
            b[12] = (byte)'I'; b[13] = (byte)'H'; b[14] = (byte)'D'; b[15] = (byte)'R';
        }
        if (length >= 24)
        {
            WriteBigEndian(b, 16, width);
            WriteBigEndian(b, 20, height);
        }
        return b;
    }

    private static void WriteBigEndian(byte[] b, int offset, int value)
    {
        b[offset] = (byte)(value >> 24);
        b[offset + 1] = (byte)(value >> 16);
        b[offset + 2] = (byte)(value >> 8);
        b[offset + 3] = (byte)value;
    }

    private static byte[] Gif(int width = 5, int height = 9, char version = '9', int length = 10)
    {
        var b = new byte[length];
        if (length >= 6)
        {
            b[0] = (byte)'G'; b[1] = (byte)'I'; b[2] = (byte)'F';
            b[3] = (byte)'8'; b[4] = (byte)version; b[5] = (byte)'a';
        }
        if (length >= 10)
        {
            b[6] = (byte)(width & 0xFF); b[7] = (byte)(width >> 8);
            b[8] = (byte)(height & 0xFF); b[9] = (byte)(height >> 8);
        }
        return b;
    }

    /// <summary>Builds a JPEG as SOI followed by the given segments.</summary>
    private static byte[] Jpeg(params byte[][] segments)
    {
        var body = segments.SelectMany(s => s).ToArray();
        return new byte[] { 0xFF, 0xD8 }.Concat(body).ToArray();
    }

    private static byte[] Sof(byte marker, int width, int height) =>
    [
        0xFF, marker, 0x00, 0x11, 0x08,
        (byte)(height >> 8), (byte)(height & 0xFF),
        (byte)(width >> 8), (byte)(width & 0xFF),
        0x03, 0x01, 0x11, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01
    ];

    private static byte[] Segment(byte marker, int payloadLength)
    {
        var total = 2 + 2 + payloadLength;
        var b = new byte[total];
        b[0] = 0xFF; b[1] = marker;
        var len = payloadLength + 2;
        b[2] = (byte)(len >> 8); b[3] = (byte)(len & 0xFF);
        return b;
    }

    private static byte[] WebpRiff(string fourCc, byte[] payload, int? truncateTo = null)
    {
        var b = new byte[12 + 4 + payload.Length];
        b[0] = (byte)'R'; b[1] = (byte)'I'; b[2] = (byte)'F'; b[3] = (byte)'F';
        b[8] = (byte)'W'; b[9] = (byte)'E'; b[10] = (byte)'B'; b[11] = (byte)'P';
        for (var i = 0; i < 4; i++) b[12 + i] = (byte)fourCc[i];
        Array.Copy(payload, 0, b, 16, payload.Length);
        return truncateTo.HasValue ? b.Take(truncateTo.Value).ToArray() : b;
    }

    private static byte[] WebpLossy(int width = 11, int height = 13)
    {
        // payload starts at file offset 16:
        //   [0..3] chunk size (16-19), [4..6] frame tag (20-22),
        //   [7..9] sync code (23-25), [10..11] width (26-27), [12..13] height (28-29)
        var payload = new byte[14];
        payload[7] = 0x9D; payload[8] = 0x01; payload[9] = 0x2A;
        payload[10] = (byte)(width & 0xFF); payload[11] = (byte)(width >> 8);
        payload[12] = (byte)(height & 0xFF); payload[13] = (byte)(height >> 8);
        return payload;
    }

    private static byte[] WebpLossless(int width = 4, int height = 6)
    {
        var payload = new byte[9];
        payload[4] = 0x2F;                                          // signature at file offset 20
        var bits = (width - 1) | ((height - 1) << 14);
        payload[5] = (byte)bits;
        payload[6] = (byte)(bits >> 8);
        payload[7] = (byte)(bits >> 16);
        payload[8] = (byte)(bits >> 24);
        return payload;
    }

    private static byte[] WebpExtended(int width = 300, int height = 200)
    {
        var payload = new byte[14];
        var w = width - 1;
        var h = height - 1;
        payload[8] = (byte)w; payload[9] = (byte)(w >> 8); payload[10] = (byte)(w >> 16);
        payload[11] = (byte)h; payload[12] = (byte)(h >> 8); payload[13] = (byte)(h >> 16);
        return payload;
    }

    // ---- size and emptiness --------------------------------------------------------------------

    [Test]
    public void Validate_NullBytes_Fails()
    {
        var result = ImageValidator.Validate(null, MaxBytes);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.EmptyMessage));
        });
    }

    [Test]
    public void Validate_EmptyBytes_Fails()
    {
        var result = ImageValidator.Validate([], MaxBytes);
        Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.EmptyMessage));
    }

    [Test]
    public void Validate_OverSizeLimit_FailsWithLimitInMegabytes()
    {
        var oversized = new byte[3 * 1024 * 1024];
        Array.Copy(Png(), oversized, 24);

        var result = ImageValidator.Validate(oversized, maxBytes: 2 * 1024 * 1024);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo(string.Format(ImageValidator.TooLargeMessage, 2)));
        });
    }

    [Test]
    public void Validate_SizeLimitBelowOneMegabyte_ReportsAtLeastOne()
    {
        var result = ImageValidator.Validate(Png(), maxBytes: 4);
        Assert.That(result.ErrorMessage, Is.EqualTo(string.Format(ImageValidator.TooLargeMessage, 1)));
    }

    [Test]
    public void Validate_ExactlyAtSizeLimit_Succeeds()
    {
        var png = Png();
        var result = ImageValidator.Validate(png, png.Length);
        Assert.That(result.Success, Is.True);
    }

    // ---- SVG rejection -------------------------------------------------------------------------

    [Test]
    public void Validate_SvgDocument_IsRejectedExplicitly()
    {
        var svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");
        var result = ImageValidator.Validate(svg, MaxBytes);
        Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.SvgRejectedMessage));
    }

    [Test]
    public void Validate_SvgWithXmlPrologAndLeadingWhitespace_IsRejected()
    {
        var svg = System.Text.Encoding.UTF8.GetBytes("  \r\n\t<?xml version=\"1.0\"?><svg></svg>");
        var result = ImageValidator.Validate(svg, MaxBytes);
        Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.SvgRejectedMessage));
    }

    [Test]
    public void Validate_SvgWithUtf8Bom_IsRejected()
    {
        var svg = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(System.Text.Encoding.UTF8.GetBytes("<svg></svg>")).ToArray();
        var result = ImageValidator.Validate(svg, MaxBytes);
        Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.SvgRejectedMessage));
    }

    [Test]
    public void Validate_XmlThatIsNotSvg_IsUnsupportedRatherThanSvgRejection()
    {
        var xml = System.Text.Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><rss></rss>");
        var result = ImageValidator.Validate(xml, MaxBytes);
        Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.UnsupportedMessage));
    }

    [Test]
    public void Validate_TextNotStartingWithAngleBracket_IsUnsupported()
    {
        var text = System.Text.Encoding.UTF8.GetBytes("hello world, not an image at all");
        var result = ImageValidator.Validate(text, MaxBytes);
        Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.UnsupportedMessage));
    }

    [Test]
    public void Validate_OnlyWhitespace_IsUnsupported()
    {
        var result = ImageValidator.Validate("    "u8.ToArray(), MaxBytes);
        Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.UnsupportedMessage));
    }

    // ---- format sniffing -----------------------------------------------------------------------

    [Test]
    public void SniffContentType_Null_ReturnsNull() =>
        Assert.That(ImageValidator.SniffContentType(null!), Is.Null);

    [Test]
    public void SniffContentType_ShorterThanEverySignature_ReturnsNull() =>
        Assert.That(ImageValidator.SniffContentType([0x89]), Is.Null);

    [Test]
    public void SniffContentType_PngSignatureWithWrongByte_ReturnsNull() =>
        Assert.That(ImageValidator.SniffContentType([0x89, 0x50, 0x4E, 0x00, 0x0D, 0x0A, 0x1A, 0x0A]), Is.Null);

    [Test]
    public void SniffContentType_RiffThatIsNotWebp_ReturnsNull()
    {
        byte[] b = [.. "RIFF"u8, 0, 0, 0, 0, .. "AVI "u8];
        Assert.That(ImageValidator.SniffContentType(b), Is.Null);
    }

    [Test]
    public void SniffContentType_Gif87a_IsRecognized() =>
        Assert.That(ImageValidator.SniffContentType(Gif(version: '7')), Is.EqualTo(ImageValidator.GifContentType));

    [Test]
    public void SniffContentType_GifWithUnknownVersion_ReturnsNull() =>
        Assert.That(ImageValidator.SniffContentType(Gif(version: '5')), Is.Null);

    [Test]
    public void Validate_UnrecognizedBinary_IsUnsupported()
    {
        var result = ImageValidator.Validate([0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B], MaxBytes);
        Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.UnsupportedMessage));
    }

    // ---- allow list ----------------------------------------------------------------------------

    [Test]
    public void Validate_TypeNotOnAllowList_IsRejected()
    {
        var result = ImageValidator.Validate(Png(), MaxBytes, [ImageValidator.JpegContentType]);
        Assert.That(result.ErrorMessage, Is.EqualTo(string.Format(ImageValidator.NotAllowedMessage, ImageValidator.PngContentType)));
    }

    [Test]
    public void Validate_TypeOnAllowList_IsAccepted()
    {
        var result = ImageValidator.Validate(Png(), MaxBytes, ["IMAGE/PNG"]);
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public void Validate_EmptyAllowList_AcceptsEverySupportedType()
    {
        var result = ImageValidator.Validate(Png(), MaxBytes, []);
        Assert.That(result.Success, Is.True);
    }

    // ---- PNG dimensions ------------------------------------------------------------------------

    [Test]
    public void Validate_Png_ReadsDimensions()
    {
        var result = ImageValidator.Validate(Png(640, 480), MaxBytes);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ContentType, Is.EqualTo(ImageValidator.PngContentType));
            Assert.That(result.Width, Is.EqualTo(640));
            Assert.That(result.Height, Is.EqualTo(480));
        });
    }

    [Test]
    public void Validate_PngTruncatedBeforeDimensions_IsCorrupt()
    {
        var result = ImageValidator.Validate(Png(length: 20), MaxBytes);
        Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.CorruptMessage));
    }

    [Test]
    public void Validate_PngWithoutIhdrChunk_IsCorrupt()
    {
        var result = ImageValidator.Validate(Png(withIhdr: false), MaxBytes);
        Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.CorruptMessage));
    }

    [Test]
    public void Validate_PngWithZeroDimensions_IsCorrupt()
    {
        var result = ImageValidator.Validate(Png(0, 10), MaxBytes);
        Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.CorruptMessage));
    }

    // ---- GIF dimensions ------------------------------------------------------------------------

    [Test]
    public void Validate_Gif_ReadsDimensions()
    {
        var result = ImageValidator.Validate(Gif(320, 200), MaxBytes);
        Assert.Multiple(() =>
        {
            Assert.That(result.ContentType, Is.EqualTo(ImageValidator.GifContentType));
            Assert.That(result.Width, Is.EqualTo(320));
            Assert.That(result.Height, Is.EqualTo(200));
        });
    }

    [Test]
    public void Validate_GifTruncatedBeforeDimensions_IsCorrupt()
    {
        var result = ImageValidator.Validate(Gif(length: 8), MaxBytes);
        Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.CorruptMessage));
    }

    [Test]
    public void Validate_GifWithZeroDimensions_IsCorrupt()
    {
        var result = ImageValidator.Validate(Gif(0, 0), MaxBytes);
        Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.CorruptMessage));
    }

    // ---- JPEG dimensions -----------------------------------------------------------------------

    [Test]
    public void Validate_JpegBaseline_ReadsDimensions()
    {
        var result = ImageValidator.Validate(Jpeg(Sof(0xC0, 1024, 768)), MaxBytes);
        Assert.Multiple(() =>
        {
            Assert.That(result.ContentType, Is.EqualTo(ImageValidator.JpegContentType));
            Assert.That(result.Width, Is.EqualTo(1024));
            Assert.That(result.Height, Is.EqualTo(768));
        });
    }

    [Test]
    public void Validate_JpegProgressive_ReadsDimensions()
    {
        var result = ImageValidator.Validate(Jpeg(Segment(0xE0, 14), Sof(0xC2, 800, 600)), MaxBytes);
        Assert.That(result.Width, Is.EqualTo(800));
    }

    [Test]
    public void Validate_JpegSkipsHuffmanTableBeforeFrameHeader()
    {
        // 0xC4 sits inside the SOF marker range but is a Huffman table, not a frame header.
        var result = ImageValidator.Validate(Jpeg(Segment(0xC4, 20), Sof(0xC1, 111, 222)), MaxBytes);
        Assert.Multiple(() =>
        {
            Assert.That(result.Width, Is.EqualTo(111));
            Assert.That(result.Height, Is.EqualTo(222));
        });
    }

    [Test]
    public void Validate_JpegSkipsArithmeticCodingAndJpgMarkers()
    {
        var result = ImageValidator.Validate(Jpeg(Segment(0xC8, 4), Segment(0xCC, 4), Sof(0xC3, 7, 8)), MaxBytes);
        Assert.That(result.Width, Is.EqualTo(7));
    }

    [Test]
    public void Validate_JpegSkipsStandaloneRestartMarkers()
    {
        var result = ImageValidator.Validate(Jpeg([0xFF, 0xD0], [0xFF, 0x01], Sof(0xC0, 20, 30)), MaxBytes);
        Assert.That(result.Height, Is.EqualTo(30));
    }

    [Test]
    public void Validate_JpegSkipsFillBytes()
    {
        var result = ImageValidator.Validate(Jpeg([0xFF, 0xFF], Sof(0xC0, 40, 50)), MaxBytes);
        Assert.That(result.Width, Is.EqualTo(40));
    }

    [Test]
    public void Validate_JpegSkipsNonMarkerPadding()
    {
        // The padding has to come after a segment: a real JPEG's third byte is part of the
        // FF D8 FF magic, so zeros at offset 2 would never reach the marker scan at all.
        var result = ImageValidator.Validate(
            Jpeg(Segment(0xE0, 2), [0x00, 0x00], Sof(0xC0, 60, 70)), MaxBytes);
        Assert.That(result.Width, Is.EqualTo(60));
    }

    [Test]
    public void Validate_JpegWithNoFrameHeader_IsCorrupt()
    {
        var result = ImageValidator.Validate(Jpeg(Segment(0xE0, 10)), MaxBytes);
        Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.CorruptMessage));
    }

    [Test]
    public void Validate_JpegWithInvalidSegmentLength_IsCorrupt()
    {
        var result = ImageValidator.Validate(Jpeg([0xFF, 0xE0, 0x00, 0x01], Sof(0xC0, 10, 10)), MaxBytes);
        Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.CorruptMessage));
    }

    [Test]
    public void Validate_JpegWithFrameHeaderRunningPastEnd_IsCorrupt()
    {
        var truncated = Jpeg(Sof(0xC0, 10, 10)).Take(7).ToArray();
        var result = ImageValidator.Validate(truncated, MaxBytes);
        Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.CorruptMessage));
    }

    [Test]
    public void Validate_JpegWithZeroDimensions_IsCorrupt()
    {
        var result = ImageValidator.Validate(Jpeg(Sof(0xC0, 0, 0)), MaxBytes);
        Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.CorruptMessage));
    }

    // ---- WebP dimensions -----------------------------------------------------------------------

    [Test]
    public void Validate_WebpLossy_ReadsDimensions()
    {
        var result = ImageValidator.Validate(WebpRiff("VP8 ", WebpLossy(11, 13)), MaxBytes);
        Assert.Multiple(() =>
        {
            Assert.That(result.ContentType, Is.EqualTo(ImageValidator.WebpContentType));
            Assert.That(result.Width, Is.EqualTo(11));
            Assert.That(result.Height, Is.EqualTo(13));
        });
    }

    [Test]
    public void Validate_WebpLossyWithoutSyncCode_IsCorrupt()
    {
        var payload = WebpLossy();
        payload[7] = 0x00;
        var result = ImageValidator.Validate(WebpRiff("VP8 ", payload), MaxBytes);
        Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.CorruptMessage));
    }

    [Test]
    public void Validate_WebpLossyTruncated_IsCorrupt()
    {
        var result = ImageValidator.Validate(WebpRiff("VP8 ", WebpLossy(), truncateTo: 28), MaxBytes);
        Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.CorruptMessage));
    }

    [Test]
    public void Validate_WebpLossyWithZeroDimensions_IsCorrupt()
    {
        var result = ImageValidator.Validate(WebpRiff("VP8 ", WebpLossy(0, 0)), MaxBytes);
        Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.CorruptMessage));
    }

    [Test]
    public void Validate_WebpLossless_ReadsDimensions()
    {
        var result = ImageValidator.Validate(WebpRiff("VP8L", WebpLossless(4, 6)), MaxBytes);
        Assert.Multiple(() =>
        {
            Assert.That(result.Width, Is.EqualTo(4));
            Assert.That(result.Height, Is.EqualTo(6));
        });
    }

    [Test]
    public void Validate_WebpLosslessWithoutSignatureByte_IsCorrupt()
    {
        var payload = WebpLossless();
        payload[4] = 0x00;
        var result = ImageValidator.Validate(WebpRiff("VP8L", payload), MaxBytes);
        Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.CorruptMessage));
    }

    [Test]
    public void Validate_WebpLosslessTruncated_IsCorrupt()
    {
        var result = ImageValidator.Validate(WebpRiff("VP8L", WebpLossless(), truncateTo: 23), MaxBytes);
        Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.CorruptMessage));
    }

    [Test]
    public void Validate_WebpExtended_ReadsCanvasDimensions()
    {
        var result = ImageValidator.Validate(WebpRiff("VP8X", WebpExtended(300, 200)), MaxBytes);
        Assert.Multiple(() =>
        {
            Assert.That(result.Width, Is.EqualTo(300));
            Assert.That(result.Height, Is.EqualTo(200));
        });
    }

    [Test]
    public void Validate_WebpExtendedTruncated_IsCorrupt()
    {
        var result = ImageValidator.Validate(WebpRiff("VP8X", WebpExtended(), truncateTo: 29), MaxBytes);
        Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.CorruptMessage));
    }

    [Test]
    public void Validate_WebpWithUnknownChunk_IsCorrupt()
    {
        var result = ImageValidator.Validate(WebpRiff("ANIM", new byte[20]), MaxBytes);
        Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.CorruptMessage));
    }

    [Test]
    public void Validate_WebpTruncatedBeforeChunkHeader_IsCorrupt()
    {
        var result = ImageValidator.Validate(WebpRiff("VP8 ", WebpLossy(), truncateTo: 14), MaxBytes);
        Assert.That(result.ErrorMessage, Is.EqualTo(ImageValidator.CorruptMessage));
    }
}
