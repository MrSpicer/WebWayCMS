namespace WebWayCMS;

/// <summary>
/// Shared constants for the preview mechanism. An authorized admin action sets a short-lived cookie
/// carrying <see cref="CookieValue"/>; the preview-aware read context serves drafts when it is present.
/// </summary>
public static class PreviewConstants
{
    public const string CookieName = "wwcms_preview";
    public const string CookieValue = "1";

    /// <summary>Path prefix of the draft preview endpoint (<c>/_preview/{nodeId}</c>).</summary>
    public const string PathPrefix = "/_preview";

    /// <summary>
    /// <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/> key an admin action sets to <c>true</c>
    /// to read drafts for that one request (the page zone editor), without the preview cookie.
    /// </summary>
    public const string DraftRequestItemKey = "WebWayCMS:ReadDrafts";
}
