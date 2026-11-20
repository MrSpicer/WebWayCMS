namespace WebWayCMS.Mcp;

/// <summary>
/// Summary of a content type or admin surface, returned by <c>list_content_types</c>. <c>ChildTypes</c> are
/// the content types a containment places under this one (the <c>childType</c> of the child tools).
/// <see cref="DisplayName"/>, <see cref="PluralDisplayName"/>, <see cref="Description"/> and
/// <see cref="Category"/> come from the type's published content type registration when it has one.
/// </summary>
public sealed record McpContentTypeInfo(
    string ContentType,
    string DisplayName,
    bool SupportsVersionHistory,
    bool SupportsPublishing,
    IReadOnlyList<string> ChildTypes,
    bool HasRegistry,
    IReadOnlyList<string> SecondaryApiListKeys)
{
    public string? PluralDisplayName { get; init; }

    public string? Description { get; init; }

    public string? Category { get; init; }

    /// <summary>Whether the admin (and so the content tools) can edit it; false for a store-only content type.</summary>
    public bool HasAdminHandler { get; init; } = true;

    /// <summary>Whether it is a registered content type (false for admin surfaces such as cmsroutes).</summary>
    public bool IsContentType { get; init; } = true;
}
