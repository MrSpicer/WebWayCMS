namespace WebWayCMS;

/// <summary>
/// Shared constants for the preview mechanism. An authorized admin action sets a short-lived cookie
/// carrying <see cref="CookieValue"/>; the preview-aware read context serves drafts when it is present.
/// </summary>
public static class PreviewConstants
{
    public const string CookieName = "wwcms_preview";
    public const string CookieValue = "1";
}
