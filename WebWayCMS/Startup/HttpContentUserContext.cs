using System.Security.Claims;

using Microsoft.AspNetCore.Http;

using WebWayCMS.Data.Services;

namespace WebWayCMS.Startup;

/// <summary>
/// Resolves the current user id from the authenticated principal's name identifier claim.
/// </summary>
internal sealed class HttpContentUserContext : IContentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    public Guid? CurrentUserId
    {
        get
        {
            var id = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(id, out var guid) ? guid : null;
        }
    }
}
