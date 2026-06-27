using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Server;

namespace WebWayCMS.Mcp;

/// <summary>
/// Registers the WebWayCMS MCP server and its toolsets. Protocol/SDK wiring only; the testable
/// logic lives in the toolset classes, so this is validated by running the app.
/// </summary>
[ExcludeFromCodeCoverage]
public static class McpServiceCollectionExtensions
{
    /// <summary>
    /// Adds the MCP server (HTTP transport) and the WebWayCMS toolsets, binding
    /// <see cref="McpOptions"/> from the "Mcp" configuration section.
    /// </summary>
    public static IServiceCollection AddWebWayCmsMcp(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<McpOptions>(configuration.GetSection(McpOptions.SectionName));

        // Toolsets resolve scoped admin handlers per call.
        services.AddScoped<ContentToolset>();
        services.AddScoped<VersionToolset>();
        services.AddScoped<ChildContentToolset>();

        // The free-form "fields" parameters are typed as JsonElement, which produces an untyped
        // schema; some clients then send the value as a JSON string. Declaring those parameters as
        // objects in the tool schema makes clients send a real object (McpToolHelpers.Merge tolerates
        // either form regardless).
        var toolOptions = new McpServerToolCreateOptions
        {
            SchemaCreateOptions = new AIJsonSchemaCreateOptions
            {
                TransformSchemaNode = DeclareJsonElementParametersAsObjects,
            },
            // Tool results are EF-backed DTO graphs (e.g. a content zone and its items, whose items
            // reference the zone back). Ignore reference cycles so serializing those results doesn't throw.
            SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
            },
        };

        var tools = ToolsFrom<ContentToolset>(toolOptions)
            .Concat(ToolsFrom<VersionToolset>(toolOptions))
            .Concat(ToolsFrom<ChildContentToolset>(toolOptions))
            .ToList();

        services.AddMcpServer()
            .WithHttpTransport()
            .WithTools(tools);

        return services;
    }

    /// <summary>Builds <see cref="McpServerTool"/>s from a toolset's <c>[McpServerTool]</c> methods,
    /// resolving the toolset from the per-request scoped service provider.</summary>
    private static IEnumerable<McpServerTool> ToolsFrom<T>(McpServerToolCreateOptions options) where T : notnull =>
        typeof(T).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null)
            .Select(m => McpServerTool.Create(
                m,
                ctx => ctx.Services!.GetRequiredService<T>(),
                options));

    /// <summary>Gives untyped <see cref="JsonElement"/> parameters (the "fields" payloads) an
    /// explicit object schema so clients send a real JSON object.</summary>
    private static JsonNode DeclareJsonElementParametersAsObjects(AIJsonSchemaCreateContext context, JsonNode node)
    {
        if (context.TypeInfo.Type == typeof(JsonElement) && node is JsonObject obj && !obj.ContainsKey("type"))
            obj["type"] = "object";

        return node;
    }
}