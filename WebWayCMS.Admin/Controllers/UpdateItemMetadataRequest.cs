namespace WebWayCMS.Controllers.Api;

/// <summary>
/// Request model for updating a content zone item's widget metadata.
/// </summary>
public class UpdateItemMetadataRequest
{
    /// <summary>
    /// JSON-serialized <c>WidgetMetadata</c> (the display mode plus the admin presentation fields).
    /// </summary>
    public string? MetadataJson { get; set; }

    /// <summary>
    /// The item's version number as the client last saw it. A mismatch fails the save with the
    /// "changed by someone else" message instead of silently overwriting the edit.
    /// </summary>
    public int? ExpectedVersionNumber { get; set; }
}
