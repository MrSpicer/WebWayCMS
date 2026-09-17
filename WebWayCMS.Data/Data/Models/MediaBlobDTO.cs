namespace WebWayCMS.Data.Models;

/// <summary>
/// Catalog metadata for one stored media file, keyed by the SHA-256 hash of its bytes.
/// Not versioned — a blob is immutable by construction, so a change produces a different
/// <see cref="Hash"/> and therefore a different row.
/// </summary>
/// <remarks>
/// The bytes themselves live in <see cref="MediaBlobBytesDTO"/>, not here. EF materializes every
/// scalar property of a tracked entity, so keeping them on this record would pull megabytes into
/// every media-library listing query. The split also lets a non-database
/// <c>IMediaBlobStore</c> leave the bytes table empty while this catalog keeps working.
/// </remarks>
public record MediaBlobDTO
{
    /// <summary>Lowercase hex SHA-256 of the file's bytes. Primary key and public URL segment.</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>The sniffed content type (never the client-declared one).</summary>
    public string ContentType { get; set; } = string.Empty;

    public long ByteLength { get; set; }

    /// <summary>Pixel width, parsed from the file header at upload.</summary>
    public int Width { get; set; }

    /// <summary>Pixel height, parsed from the file header at upload.</summary>
    public int Height { get; set; }

    /// <summary>The uploaded file name, kept for display and for building a readable URL tail.</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }
}
