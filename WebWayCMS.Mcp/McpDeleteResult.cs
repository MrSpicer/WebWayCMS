namespace WebWayCMS.Mcp;

/// <summary>
/// Result of a delete operation. <paramref name="DeletedDescendants"/> counts the child items deleted
/// with it (e.g. an article list's articles).
/// </summary>
public sealed record McpDeleteResult(bool Deleted, int DeletedDescendants = 0);
