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

/// <summary>Describes a single editable field of a content type's upsert form.</summary>
public sealed record McpFieldInfo(
    string Name,
    string Label,
    string EditorType,
    bool Required,
    string? HelpText,
    int Order,
    McpRelationDescription? Relation = null);

/// <summary>
/// The relation a field declares (<c>[ContentRelation]</c>): its kind (<c>containment</c>,
/// <c>reference</c> or <c>owned</c>), the target content type, whether it holds many ids, and whether
/// a target is required. The field's value is the target's node id (or an array of them).
/// </summary>
public sealed record McpRelationDescription(string Kind, string Target, bool Many, bool Required);

/// <summary>The full field contract for a content type, returned by <c>describe_content_type</c>.</summary>
public sealed record McpContentTypeDescription(
    string ContentType,
    string DisplayName,
    IReadOnlyList<McpFieldInfo> Fields);

/// <summary>
/// Result of a delete operation. <paramref name="DeletedDescendants"/> counts the child items deleted
/// with it (e.g. an article list's articles).
/// </summary>
public sealed record McpDeleteResult(bool Deleted, int DeletedDescendants = 0);

/// <summary>Result of a reorder operation.</summary>
public sealed record McpReorderResult(bool Reordered);

/// <summary>Result of a move operation.</summary>
public sealed record McpMoveResult(bool Moved);

/// <summary>Result of a page history revert.</summary>
public sealed record McpRevertResult(bool Reverted);

/// <summary>One stored media blob, as reported by <c>list_media</c>.</summary>
public sealed record McpMediaInfo(
    string Hash,
    string ContentType,
    long ByteLength,
    int Width,
    int Height,
    string OriginalFileName,
    DateTime CreatedUtc,
    string Url);
