using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace WebWayCMS.Mcp;

/// <summary>
/// Maps the WebWayCMS MCP endpoint, gated by the configured API key. Protocol/SDK and middleware
/// wiring only — validated by running the app.
/// </summary>
[ExcludeFromCodeCoverage]
public static class McpApplicationBuilderExtensions
{
    /// <summary>
    /// Maps the MCP server at the configured path when enabled, behind a bearer-token gate.
    /// No-ops when <see cref="McpOptions.Enabled"/> is false.
    /// </summary>
    public static WebApplication MapWebWayCmsMcp(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<McpOptions>>().Value;
        if (!options.Enabled)
            return app;

        app.MapMcp(options.Path)
            .AddEndpointFilter(new McpApiKeyEndpointFilter(options.ApiKey));

        return app;
    }
}

/// <summary>
/// Rejects MCP requests whose <c>Authorization: Bearer</c> token does not match the configured key.
/// The MCP endpoint executes with effective admin authority, so this token is the security boundary.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class McpApiKeyEndpointFilter : IEndpointFilter
{
    private readonly string? _apiKey;

    public McpApiKeyEndpointFilter(string? apiKey) => _apiKey = apiKey;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (string.IsNullOrEmpty(_apiKey) || !IsAuthorized(context.HttpContext.Request))
            return Results.Unauthorized();

        return await next(context);
    }

    private bool IsAuthorized(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        var token = header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..]
            : header;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token),
            Encoding.UTF8.GetBytes(_apiKey!));
    }
}
