namespace WebWayCMS.Mcp;

/// <summary>
/// Configuration for the WebWayCMS MCP server, bound from the "Mcp" configuration section.
/// </summary>
public sealed class McpOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Mcp";

    /// <summary>
    /// Whether the MCP endpoint is mapped. Defaults to false so the server is opt-in.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Static bearer token required on the <c>Authorization</c> header of every MCP request.
    /// The MCP endpoint executes with effective admin authority, so this token is the security
    /// boundary; supply it via user-secrets or environment variables, never source control.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Route the MCP server is mapped to. Defaults to "/mcp".</summary>
    public string Path { get; set; } = "/mcp";
}
