namespace WebWayCMS.Mcp;

/// <summary>The outcome of an MCP user-management write: what happened, and the affected user.</summary>
public sealed record McpUserActionResult(string Message, string? UserId);
