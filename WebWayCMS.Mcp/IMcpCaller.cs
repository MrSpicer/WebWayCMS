using System.Security.Claims;

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
