namespace WebWayCMS.Mcp;

/// <summary>Summary of a registered admin content type, returned by <c>list_content_types</c>.</summary>
public sealed record McpContentTypeInfo(
    string ContentType,
    string DisplayName,
    bool SupportsVersionHistory,
    bool HasChildren,
    string? ChildType,
    bool HasRegistry);

/// <summary>Describes a single editable field of a content type's upsert form.</summary>
public sealed record McpFieldInfo(
    string Name,
    string Label,
    string EditorType,
    bool Required,
    string? HelpText,
    int Order);

/// <summary>The full field contract for a content type, returned by <c>describe_content_type</c>.</summary>
public sealed record McpContentTypeDescription(
    string ContentType,
    string DisplayName,
    IReadOnlyList<McpFieldInfo> Fields);

/// <summary>Result of a delete operation.</summary>
public sealed record McpDeleteResult(bool Deleted);

/// <summary>Result of a reorder operation.</summary>
public sealed record McpReorderResult(bool Reordered);
