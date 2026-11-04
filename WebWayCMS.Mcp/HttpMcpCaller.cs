using System.Security.Claims;

using Microsoft.AspNetCore.Http;

namespace WebWayCMS.Mcp;

/// <summary>
/// The default <see cref="IMcpCaller"/>: the current HTTP request's user. The MCP transport is
/// stateless, so every tool call runs inside the HTTP request that authenticated it.
/// </summary>
public sealed class HttpMcpCaller : IMcpCaller
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpMcpCaller(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    public ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;
}
