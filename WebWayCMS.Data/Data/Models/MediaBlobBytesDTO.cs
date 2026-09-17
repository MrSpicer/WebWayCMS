namespace WebWayCMS.Data.Models;

/// <summary>
/// The stored bytes for one <see cref="MediaBlobDTO"/>, in their own table so that catalog
/// queries never materialize them. This is the backing store for the default
/// database-backed <c>IMediaBlobStore</c>; a filesystem or cloud store leaves it empty.
/// </summary>
public record MediaBlobBytesDTO
{
    /// <summary>Lowercase hex SHA-256 — primary key, and a foreign key to the catalog row.</summary>
    public string Hash { get; set; } = string.Empty;

    public byte[] Bytes { get; set; } = [];
}
