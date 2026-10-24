using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

using WebWayCMS.Data.Services;

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

    /// <summary>Role allowed to read drafts (see <see cref="DraftRequestItemKey"/>).</summary>
    public const string AdminRole = "Admin";

    /// <summary>Second role allowed to read drafts (see <see cref="DraftRequestItemKey"/>).</summary>
    public const string EditorRole = "Editor";

    /// <summary><c>Cache-Control</c> sent on every draft-read response (see <see cref="MarkDraftRequest"/>).</summary>
    public const string DraftCacheControl = "no-store, max-age=0";

    /// <summary>
    /// Marks this request as a draft read and makes its response uncacheable. The only gate on a draft
    /// is this request's role check, so a cached draft response could otherwise be replayed without it.
    /// Every draft-read mark goes through here so the two never drift apart.
    /// </summary>
    public static void MarkDraftRequest(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        httpContext.Items[DraftRequestItemKey] = true;
        httpContext.Response.Headers[HeaderNames.CacheControl] = DraftCacheControl;
    }

    /// <summary>
    /// Path segment of a page-history preview, <c>/_preview/{pageNodeId}/history/{entryId}</c>, which renders
    /// the page as one page-history entry captured it.
    /// </summary>
    public const string HistorySegment = "history";

    /// <summary>
    /// <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/> key holding the
    /// <see cref="ContentReadSnapshot"/> a page-history preview reads (Admin only).
    /// </summary>
    public const string SnapshotRequestItemKey = "WebWayCMS:ReadSnapshot";

    /// <summary>
    /// Marks this request as a page-history read of <paramref name="snapshot"/> and makes its response
    /// uncacheable, for the same reason as <see cref="MarkDraftRequest"/>.
    /// </summary>
    public static void MarkSnapshotRequest(HttpContext httpContext, ContentReadSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(snapshot);
        httpContext.Items[SnapshotRequestItemKey] = snapshot;
        httpContext.Response.Headers[HeaderNames.CacheControl] = DraftCacheControl;
    }
}
