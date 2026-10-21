namespace WebWayCMS;

/// <summary>
/// Shared constants for the draft preview. Drafts are read per request, never per browser: the route
/// transformer marks a resolved <c>/_preview/{nodeId}</c> request (and an admin action such as the page zone
/// editor marks its own request) with <see cref="DraftRequestItemKey"/>, and the preview-aware read context
/// serves drafts only to an Admin/Editor whose request carries that mark.
/// </summary>
public static class PreviewConstants
{
    /// <summary>Path prefix of the draft preview endpoint (<c>/_preview/{nodeId}</c>).</summary>
    public const string PathPrefix = "/_preview";

    /// <summary>
    /// <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/> key set to <c>true</c> to read drafts for
    /// that one request (the <c>/_preview</c> path and the page zone editor).
    /// </summary>
    public const string DraftRequestItemKey = "WebWayCMS:ReadDrafts";
}
