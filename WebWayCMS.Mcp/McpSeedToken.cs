namespace WebWayCMS.Mcp;

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
