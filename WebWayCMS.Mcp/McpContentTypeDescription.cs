namespace WebWayCMS.Mcp;

/// <summary>The full field contract for a content type, returned by <c>describe_content_type</c>.</summary>
public sealed record McpContentTypeDescription(
    string ContentType,
    string DisplayName,
    IReadOnlyList<McpFieldInfo> Fields);
