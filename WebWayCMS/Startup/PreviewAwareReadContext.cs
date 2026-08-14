using Microsoft.AspNetCore.Http;

using WebWayCMS.Data.Services;

namespace WebWayCMS.Startup;

/// <summary>
/// The admin read context. Serves drafts only when the request carries a valid preview cookie
/// <em>and</em> the user is authenticated as an Admin/Editor; otherwise it serves published content.
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
        return ctx.Request.Cookies.TryGetValue(PreviewConstants.CookieName, out var value)
            && value == PreviewConstants.CookieValue;
    }
}
