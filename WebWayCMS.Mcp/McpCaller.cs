using System.Security.Claims;

using Microsoft.AspNetCore.Http;

namespace WebWayCMS.Mcp;

/// <summary>
/// The user an MCP tool call acts as: the principal the personal-access-token scheme built for the
/// request. Abstracted so the toolsets' role checks are unit-testable.
/// </summary>
public interface IMcpCaller
{
    /// <summary>The calling user, or null when the call carries none (every write is then refused).</summary>
    ClaimsPrincipal? User { get; }
}

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
