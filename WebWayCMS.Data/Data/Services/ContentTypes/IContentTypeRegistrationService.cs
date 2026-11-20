using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services.ContentTypes;

/// <summary>
/// Reads the published content type registrations: the names, description and category admins gave each content
/// type. A content type with no published registration has none here, so callers keep their own fallback.
/// </summary>
public interface IContentTypeRegistrationService
{
    /// <summary>The published registration of every content type, keyed by content type key (case-insensitive).</summary>
    Task<IReadOnlyDictionary<string, ContentTypeRegistrationDTO>> GetPublishedByKeyAsync(CancellationToken ct = default);

    /// <summary>
    /// <see cref="GetPublishedByKeyAsync"/> for a synchronous caller (an MCP tool that is synchronous by contract).
    /// </summary>
    IReadOnlyDictionary<string, ContentTypeRegistrationDTO> GetPublishedByKey();
}
