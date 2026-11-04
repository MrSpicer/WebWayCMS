namespace WebWayCMS.Mcp;

/// <summary>
/// Summary of a registered admin content type, returned by <c>list_content_types</c>. <c>ChildTypes</c> are
/// the content types a containment places under this one (the <c>childType</c> of the child tools).
/// </summary>
public sealed record McpContentTypeInfo(
    string ContentType,
    string DisplayName,
    bool SupportsVersionHistory,
    bool SupportsPublishing,
    IReadOnlyList<string> ChildTypes,
    bool HasRegistry,
    IReadOnlyList<string> SecondaryApiListKeys);
