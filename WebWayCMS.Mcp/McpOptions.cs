namespace WebWayCMS.Mcp;

/// <summary>
/// Configuration for the WebWayCMS MCP server, bound from the "Mcp" configuration section.
/// </summary>
/// <remarks>
/// Callers authenticate with a per-user personal access token (Identity → Manage → API tokens), sent as
/// <c>Authorization: Bearer &lt;token&gt;</c>. A token acts as its user with that user's current roles,
/// and only Admins may use the endpoint.
/// </remarks>
public sealed class McpOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Mcp";

    /// <summary>
    /// Whether the MCP endpoint is mapped. Defaults to false so the server is opt-in.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Route the MCP server is mapped to. Defaults to "/mcp".</summary>
    public string Path { get; set; } = "/mcp";

    /// <summary>
    /// Tokens created at startup for local development and automated tests. Applied only when the
    /// host environment is Development (or <see cref="AllowSeedTokensOutsideDevelopment"/> is set);
    /// ignored, with a warning, everywhere else.
    /// </summary>
    public List<McpSeedToken> SeedTokens { get; set; } = [];

    /// <summary>
    /// Applies <see cref="SeedTokens"/> outside Development too. For throwaway test stacks that run as
    /// Production (the TestHost's docker-compose); never set it on a real deployment, since the
    /// plaintext token lives in configuration. Logged as a warning whenever it takes effect.
    /// </summary>
    public bool AllowSeedTokensOutsideDevelopment { get; set; }
}

/// <summary>A development token from <see cref="McpOptions.SeedTokens"/>.</summary>
public sealed class McpSeedToken
{
    /// <summary>Email of the existing user the token acts as.</summary>
    public string UserEmail { get; set; } = string.Empty;

    /// <summary>The token's name in the user's token list.</summary>
    public string Name { get; set; } = "Seeded";

    /// <summary>The plaintext token (at least 32 characters).</summary>
    public string Token { get; set; } = string.Empty;
}
