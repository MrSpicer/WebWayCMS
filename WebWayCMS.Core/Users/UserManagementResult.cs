namespace WebWayCMS.Users;

/// <summary>The outcome of a user-management action, with the message to show (a flash, an MCP result).</summary>
public sealed record UserManagementResult(bool Success, string Message, string? UserId = null);
