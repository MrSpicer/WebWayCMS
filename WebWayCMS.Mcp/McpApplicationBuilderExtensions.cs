using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace WebWayCMS.Mcp;

/// <summary>
/// Maps the WebWayCMS MCP endpoint behind per-user personal access tokens. Protocol/SDK and
/// middleware wiring only — validated by running the app.
/// </summary>
[ExcludeFromCodeCoverage]
public static class McpApplicationBuilderExtensions
{
    /// <summary>
    /// Maps the MCP server at the configured path when enabled. Every request must authenticate with a
    /// personal access token (<see cref="McpTokenAuthenticationHandler"/>, the only scheme the policy
    /// accepts) whose user is an Admin, mirroring the Admin-only admin UI; each write tool also checks
    /// its content type's roles. No-ops when <see cref="McpOptions.Enabled"/> is false.
    /// </summary>
    public static WebApplication MapWebWayCmsMcp(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<McpOptions>>().Value;
        if (!options.Enabled)
            return app;

        var policy = new AuthorizationPolicyBuilder(McpTokenAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser()
            .RequireRole(PreviewConstants.AdminRole)
            .Build();

        app.MapMcp(options.Path).RequireAuthorization(policy);

        return app;
    }
}
