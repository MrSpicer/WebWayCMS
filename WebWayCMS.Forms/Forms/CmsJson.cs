using System.Text.Json;

namespace WebWayCMS.Forms;

/// <summary>Shared <see cref="JsonSerializerOptions"/>, so callers don't allocate (and re-cache) options per call.</summary>
public static class CmsJson
{
    /// <summary>Default options with case-insensitive property names (configuration and metadata JSON).</summary>
    public static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };
}
