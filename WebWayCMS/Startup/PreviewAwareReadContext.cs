using Microsoft.AspNetCore.Http;

using WebWayCMS.Data.Services;

namespace WebWayCMS.Startup;

/// <summary>
/// The admin read context. Serves drafts only when the user is authenticated as an Admin/Editor
/// <em>and</em> the request was marked as a draft read (<see cref="PreviewConstants.DraftRequestItemKey"/>) —
/// by the <c>/_preview</c> route resolution or an admin action; otherwise it serves published content.
/// A page-history preview (<see cref="PreviewConstants.SnapshotRequestItemKey"/>, Admin only) reads the
/// captured versions instead, and wins over a draft mark. The marks are per request, so previewing one
/// page never leaks drafts onto the live site.
/// </summary>
internal sealed class PreviewAwareReadContext : IContentReadContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PreviewAwareReadContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    public string Culture => string.Empty;

    public string Segment => string.Empty;

    public ContentReadMode Mode
        => ReadSnapshot() != null ? ContentReadMode.Snapshot
        : IsDraftRequest() ? ContentReadMode.Draft
        : ContentReadMode.Published;

    public ContentReadSnapshot? Snapshot => ReadSnapshot();

    private ContentReadSnapshot? ReadSnapshot()
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx?.User?.Identity?.IsAuthenticated != true || !ctx.User.IsInRole(PreviewConstants.AdminRole))
            return null;
        return ctx.Items.TryGetValue(PreviewConstants.SnapshotRequestItemKey, out var snapshot)
            ? snapshot as ContentReadSnapshot
            : null;
    }

    private bool IsDraftRequest()
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx?.User?.Identity?.IsAuthenticated != true)
            return false;
        if (!(ctx.User.IsInRole(PreviewConstants.AdminRole) || ctx.User.IsInRole(PreviewConstants.EditorRole)))
            return false;
        return ctx.Items.TryGetValue(PreviewConstants.DraftRequestItemKey, out var flag) && flag is true;
    }
}
