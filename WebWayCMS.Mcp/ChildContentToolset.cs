using System.ComponentModel;
using System.Text.Json;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Forms;
using WebWayCMS.Models.Shared;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Mcp;

/// <summary>
/// MCP tools for content placed under other content by a containment relation (an article list's
/// articles, a page's child pages, a zone's items). A child is ordinary content of its own type: the tools
/// that need the parent resolve the containment from <c>parentType</c> and <c>childType</c> (and
/// <c>relation</c> when the child type declares several), and the rest act on the child type's handler.
/// </summary>
[McpServerToolType]
public sealed class ChildContentToolset
{
    private readonly IAdminHandlerRegistry _registry;
    private readonly ContentRelationRegistry _relations;

    public ChildContentToolset(IAdminHandlerRegistry registry, ContentRelationRegistry relations)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _relations = relations ?? throw new ArgumentNullException(nameof(relations));
    }

    /// <summary>
    /// The child type's handler and the one containment that places it under <paramref name="parentType"/>
    /// (named by <paramref name="relation"/> when there are several).
    /// </summary>
    private (IAdminCrudHandler Child, string Relation) ResolveContainment(string parentType, string childType, string? relation)
    {
        var parent = McpToolHelpers.ResolveHandler(_registry, parentType);
        var child = McpToolHelpers.ResolveHandler(_registry, childType);
        var candidates = _relations.GetContainmentsTargeting(parent.ContentType)
            .Where(d => string.Equals(d.SourceContentType, child.ContentType, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var matching = string.IsNullOrWhiteSpace(relation)
            ? candidates
            : candidates.Where(d => string.Equals(d.Name, relation, StringComparison.OrdinalIgnoreCase)).ToList();

        return matching.Count switch
        {
            1 => (child, matching[0].Name),
            0 => throw new McpException(candidates.Count == 0
                ? $"Content type '{child.ContentType}' is not placed under '{parent.ContentType}' by any containment."
                : $"Content type '{child.ContentType}' has no containment '{relation}' under '{parent.ContentType}'."),
            _ => throw new McpException(
                $"Content type '{child.ContentType}' is placed under '{parent.ContentType}' by several relations " +
                $"({string.Join(", ", matching.Select(d => d.Name))}); pass relation."),
        };
    }

    private IAdminCrudHandler ResolveVersioned(string childType)
    {
        var child = McpToolHelpers.ResolveHandler(_registry, childType);
        if (!child.SupportsVersionHistory)
            throw new McpException($"Content type '{childType}' does not support version history.");
        return child;
    }

    [McpServerTool(Name = "list_children", ReadOnly = true, OpenWorld = false)]
    [Description("Lists the items placed under a parent by a containment, as { id, title, state, ordinal } (current drafts, by position): e.g. parentType \"articlelists\" + childType \"articles\", \"pages\" + \"pages\" for child pages, or \"contentzones\" + \"contentzoneitems\" for a zone's items. list_content_types shows each type's childTypes.")]
    public async Task<IReadOnlyList<RelatedChild>> ListChildren(
        [Description("The parent's content type, e.g. \"articlelists\".")] string parentType,
        [Description("The parent's node id.")] Guid parentKey,
        [Description("The child content type, e.g. \"articles\".")] string childType,
        [Description("Optional: the containment's name (the child's relation field), needed only when the child type has several to this parent type.")] string? relation = null,
        CancellationToken ct = default)
    {
        var (child, name) = ResolveContainment(parentType, childType, relation);
        return await child.GetRelatedChildrenAsync(name, parentKey, ct);
    }

    [McpServerTool(Name = "get_child", ReadOnly = true, OpenWorld = false)]
    [Description("Gets the editable fields of one child item by its id (the same as get_content on the child's type).")]
    public async Task<object> GetChild(
        [Description("The child content type, e.g. \"contentzoneitems\".")] string childType,
        [Description("The child's node id.")] Guid id,
        CancellationToken ct = default)
    {
        var child = McpToolHelpers.ResolveHandler(_registry, childType);
        return await child.GetUpsertViewModelAsync(id, McpToolHelpers.EmptyQuery, ct)
            ?? throw new McpException($"No '{childType}' item found with id '{id}'.");
    }

    [McpServerTool(Name = "get_child_version", ReadOnly = true, OpenWorld = false)]
    [Description("Gets the editable fields of one historical version of a child item, identified by that version's id.")]
    public async Task<object> GetChildVersion(
        [Description("The child content type.")] string childType,
        [Description("The id of the historical version.")] Guid versionId,
        CancellationToken ct = default)
    {
        var child = ResolveVersioned(childType);
        return await child.GetRestoreVersionViewModelAsync(versionId, ct)
            ?? throw new McpException($"No version found with id '{versionId}'.");
    }

    [McpServerTool(Name = "create_child", Destructive = false, OpenWorld = false)]
    [Description("Creates an item under a parent: create_content on the child type with its containment field set to the parent. Supply the other fields as a JSON object (describe_content_type on the child type lists them). A content zone item (childType \"contentzoneitems\", parentType \"contentzones\") is added last in the zone, as a draft until its page is published (live at once in a global zone).")]
    public async Task<AdminSaveResult> CreateChild(
        [Description("The parent's content type, e.g. \"contentzones\".")] string parentType,
        [Description("The parent's node id.")] Guid parentKey,
        [Description("The child content type, e.g. \"contentzoneitems\".")] string childType,
        [Description("A JSON object of field values.")] JsonElement fields,
        [Description("Optional: the containment's name, needed only when the child type has several to this parent type.")] string? relation = null,
        CancellationToken ct = default)
    {
        var (child, name) = ResolveContainment(parentType, childType, relation);
        var model = McpToolHelpers.Merge(child.CreateEmptyUpsertViewModel(), fields);
        RelationProperties.Write(model, RelationProperties.Read(model)
            .Where(v => v.Name != name)
            .Append(new RelationValue(name, parentKey, 0, string.Empty)));
        return await child.SaveUpsertAsync(model, ct);
    }

    [McpServerTool(Name = "update_child", OpenWorld = false)] // Destructive left at its true default: overwrites existing values
    [Description("Updates an existing child item. Only supplied fields are changed. A content zone item never changes zone this way (use move_child). Changing a container content zone item's view (e.g. a Layout from two columns to three) moves the widgets in its old zones into the new view's zones: same-named zones keep their widgets, the rest pair up in order, and any left over go to the last zone.")]
    public async Task<AdminSaveResult> UpdateChild(
        [Description("The child content type.")] string childType,
        [Description("The id of the child to update.")] Guid id,
        [Description("A JSON object of field values to change.")] JsonElement fields,
        CancellationToken ct = default)
    {
        var child = McpToolHelpers.ResolveHandler(_registry, childType);
        var existing = await child.GetUpsertViewModelAsync(id, McpToolHelpers.EmptyQuery, ct)
            ?? throw new McpException($"No '{childType}' item found with id '{id}'.");
        return await child.SaveUpsertAsync(McpToolHelpers.Merge(existing, fields), ct);
    }

    [McpServerTool(Name = "delete_child", OpenWorld = false)] // Destructive left at its true default
    [Description("Deletes a child item by id (delete_content on its type: what it contains goes with it, and deletedDescendants counts that). A content zone item is not deleted but removed: it gets an inactive version (a draft on a page until the page is published; live at once in a global zone), so restore_child_version or a page history revert can bring it back.")]
    public async Task<McpDeleteResult> DeleteChild(
        [Description("The child content type.")] string childType,
        [Description("The id of the child to delete.")] Guid id,
        CancellationToken ct = default)
    {
        var child = McpToolHelpers.ResolveHandler(_registry, childType);
        var result = await child.TryDeleteAsync(id, ct);
        if (!result.Success && result.ErrorMessage != null)
            throw new McpException(result.ErrorMessage);
        return new McpDeleteResult(result.Success, result.DeletedDescendants);
    }

    [McpServerTool(Name = "reorder_children", Destructive = false, OpenWorld = false)]
    [Description("Reorders the items under a parent to match the given list of ids (every child, once). Only valid for an ordered containment (e.g. a content zone's items); only the items that moved get a new version.")]
    public async Task<McpReorderResult> ReorderChildren(
        [Description("The parent's content type.")] string parentType,
        [Description("The parent's node id.")] Guid parentKey,
        [Description("The child content type.")] string childType,
        [Description("The child ids in the desired order.")] List<Guid> orderedIds,
        [Description("Optional: the containment's name, needed only when the child type has several to this parent type.")] string? relation = null,
        CancellationToken ct = default)
    {
        var (child, name) = ResolveContainment(parentType, childType, relation);
        var (success, errorMessage) = await child.ReorderRelatedAsync(name, parentKey, orderedIds, ct);
        if (!success)
            throw new McpException(errorMessage ?? "The reorder failed.");
        return new McpReorderResult(true);
    }

    [McpServerTool(Name = "move_child", Destructive = false, OpenWorld = false)]
    [Description("Moves a child item under a (possibly different) parent, as a new draft: an article to another list, a page under another page, or a content zone item to a position in another zone of the same page (its current zone to reorder; a draft until the page is published). Pass expectedVersionNumber (the child's version as you last read it) to have the move refused if the child changed since.")]
    public async Task<McpMoveResult> MoveChild(
        [Description("The new parent's content type.")] string parentType,
        [Description("The new parent's node id.")] Guid targetParentKey,
        [Description("The child content type.")] string childType,
        [Description("The id of the child to move.")] Guid childId,
        [Description("Optional, ordered containments only: the 0-based position among the new parent's children, excluding the moved child (default last; out-of-range values are clamped).")] int? position = null,
        [Description("Optional: the child's current version number as you last read it; the move is refused if it has changed since.")] int? expectedVersionNumber = null,
        [Description("Optional: the containment's name, needed only when the child type has several to this parent type.")] string? relation = null,
        CancellationToken ct = default)
    {
        var (child, name) = ResolveContainment(parentType, childType, relation);
        var (success, errorMessage) = await child.MoveRelatedAsync(name, childId, targetParentKey, position, expectedVersionNumber, ct);
        if (!success)
            throw new McpException(errorMessage ?? "The move failed.");
        return new McpMoveResult(true);
    }

    [McpServerTool(Name = "list_child_versions", ReadOnly = true, OpenWorld = false)]
    [Description("Lists the version history of a child item by its node id. For a content zone item that holds nested zones (e.g. a Layout), the list also has every version of every widget inside it, newest first (isCombined true): each row's nodeId and widgetLabel say which widget it belongs to, and isLatest/nodeCurrentVersionNumber are per widget.")]
    public async Task<VersionHistoryViewModel> ListChildVersions(
        [Description("The child content type.")] string childType,
        [Description("The child's node id.")] Guid nodeId,
        CancellationToken ct = default)
    {
        var child = ResolveVersioned(childType);
        return await child.GetVersionHistoryViewModelAsync(nodeId, ct)
            ?? throw new McpException($"No version history found for node '{nodeId}'.");
    }

    [McpServerTool(Name = "restore_child_version", Destructive = false, OpenWorld = false)]
    [Description("Restores a historical version of a child item as its new current version, in one step. For a content zone item that is its widget, configuration, settings, active state, zone and position (it goes back into its old zone at its old position); it is refused when that zone no longer exists on the page. Restoring a container (e.g. a Layout) to a different view moves its nested widgets into that view's zones. On a page it stays a draft until the page is published.")]
    public async Task<AdminSaveResult> RestoreChildVersion(
        [Description("The child content type.")] string childType,
        [Description("The id of the historical version to restore.")] Guid versionId,
        [Description("Optional: the child's current version number as you last read it; the restore is refused if it has changed since.")] int? expectedVersionNumber = null,
        CancellationToken ct = default)
    {
        var child = ResolveVersioned(childType);
        var result = await child.RestoreVersionAsync(versionId, expectedVersionNumber, ct);
        if (!result.Success && result.ErrorMessage == IAdminCrudHandler.VersionNotFoundMessage)
            throw new McpException($"No version found with id '{versionId}'.");
        return result;
    }

    [McpServerTool(Name = "delete_child_version", OpenWorld = false)] // Destructive left at its true default: permanently deletes a child version
    [Description("Permanently deletes a single historical version of a child item by its id. The latest version is never deleted (deleted: false). A version that is part of a page history entry is refused with an error naming the entry; delete that entry first (delete_page_history_entry).")]
    public async Task<McpDeleteResult> DeleteChildVersion(
        [Description("The child content type.")] string childType,
        [Description("The id of the version to delete.")] Guid versionId,
        CancellationToken ct = default)
    {
        var child = ResolveVersioned(childType);
        return McpToolHelpers.DeleteResult(await child.TryDeleteVersionAsync(versionId, ct));
    }
}
