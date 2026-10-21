using Microsoft.AspNetCore.Http;

using WebWayCMS.Data.Services;

namespace WebWayCMS.Startup;

/// <summary>
/// The admin read context. Serves drafts only when the user is authenticated as an Admin/Editor
/// <em>and</em> the request either carries a valid preview cookie or was marked as a draft read by an
/// admin action (<see cref="PreviewConstants.DraftRequestItemKey"/>); otherwise it serves published content.
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

    public ContentReadMode Mode => IsPreviewRequest() ? ContentReadMode.Draft : ContentReadMode.Published;

    private bool IsPreviewRequest()
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx?.User?.Identity?.IsAuthenticated != true)
            return false;
        if (!(ctx.User.IsInRole("Admin") || ctx.User.IsInRole("Editor")))
            return false;
        if (ctx.Items.TryGetValue(PreviewConstants.DraftRequestItemKey, out var flag) && flag is true)
            return true;
        return ctx.Request.Cookies.TryGetValue(PreviewConstants.CookieName, out var value)
            && value == PreviewConstants.CookieValue;
    }
}
