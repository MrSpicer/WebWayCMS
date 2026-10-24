using System.ComponentModel;
using System.Text.Json;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Models.Shared;

namespace WebWayCMS.Mcp;

/// <summary>
/// MCP tools for child entities of a content type (e.g. articles within an article list, items
/// within a content zone). Mirrors the child routes on <c>AdminContentController</c>, including the
/// child-type match, reorder, and version-history guards.
/// </summary>
[McpServerToolType]
public sealed class ChildContentToolset
{
    private readonly IAdminHandlerRegistry _registry;

    public ChildContentToolset(IAdminHandlerRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    private IAdminCrudChildHandler Resolve(string contentType, string childType)
    {
        var handler = McpToolHelpers.ResolveHandler(_registry, contentType);
        return McpToolHelpers.ResolveChild(handler, childType);
    }

    private IAdminCrudChildHandler ResolveVersioned(string contentType, string childType)
    {
        var child = Resolve(contentType, childType);
        if (!child.SupportsVersionHistory)
            throw new McpException($"Child type '{childType}' does not support version history.");
        return child;
    }

    [McpServerTool(Name = "list_children", ReadOnly = true, OpenWorld = false)]
    [Description("Lists the child entities under a parent. parentKey is the parent's slug or Guid (e.g. an article list slug, or a content zone id).")]
    public async Task<object> ListChildren(
        [Description("The parent content type, e.g. \"articles\" or \"contentzones\".")] string contentType,
        [Description("The child type, e.g. \"articles\" or \"items\".")] string childType,
        [Description("The parent key (slug or Guid).")] string parentKey,
        CancellationToken ct = default)
    {
        var child = Resolve(contentType, childType);
        var vm = await child.GetChildIndexViewModelAsync(parentKey, ct);
        if (vm == null)
            throw new McpException($"No parent found for key '{parentKey}'.");
        return vm;
    }

    [McpServerTool(Name = "get_child", ReadOnly = true, OpenWorld = false)]
    [Description("Gets the editable view model for a single child entity by its id.")]
    public async Task<object> GetChild(
        [Description("The parent content type.")] string contentType,
        [Description("The child type.")] string childType,
        [Description("The parent key (slug or Guid).")] string parentKey,
        [Description("The child entity id.")] Guid id,
        CancellationToken ct = default)
    {
        var child = Resolve(contentType, childType);
        var vm = await child.GetChildUpsertViewModelAsync(parentKey, id, ct);
        if (vm == null)
            throw new McpException($"No '{childType}' child found with id '{id}'.");
        return vm;
    }

    [McpServerTool(Name = "get_child_version", ReadOnly = true, OpenWorld = false)]
    [Description("Gets the editable view model for a single historical version of a child entity, identified by that version's id.")]
    public async Task<object> GetChildVersion(
        [Description("The parent content type.")] string contentType,
        [Description("The child type.")] string childType,
        [Description("The parent key (slug or Guid).")] string parentKey,
        [Description("The id of the historical version.")] Guid versionId,
        CancellationToken ct = default)
    {
        var child = ResolveVersioned(contentType, childType);
        var vm = await child.GetChildRestoreVersionViewModelAsync(parentKey, versionId, ct);
        if (vm == null)
            throw new McpException($"No version found with id '{versionId}'.");
        return vm;
    }

    [McpServerTool(Name = "create_child", Destructive = false, OpenWorld = false)]
    [Description("Creates a new child entity under a parent. Supply the fields as a JSON object.")]
    public async Task<AdminSaveResult> CreateChild(
        [Description("The parent content type.")] string contentType,
        [Description("The child type.")] string childType,
        [Description("The parent key (slug or Guid) the child belongs to.")] string parentKey,
        [Description("A JSON object of field values.")] JsonElement fields,
        CancellationToken ct = default)
    {
        var child = Resolve(contentType, childType);
        var model = McpToolHelpers.Merge(child.CreateEmptyChildUpsertViewModel(), fields);
        return await child.SaveChildUpsertAsync(parentKey, model, ct);
    }

    [McpServerTool(Name = "update_child", OpenWorld = false)] // Destructive left at its true default: overwrites existing values
    [Description("Updates an existing child entity. Only supplied fields are changed. Changing a container content zone item's view (e.g. a Layout from two columns to three) moves the widgets in its old zones into the new view's zones: same-named zones keep their widgets, the rest pair up in order, and any left over go to the last zone.")]
    public async Task<AdminSaveResult> UpdateChild(
        [Description("The parent content type.")] string contentType,
        [Description("The child type.")] string childType,
        [Description("The parent key (slug or Guid).")] string parentKey,
        [Description("The id of the child to update.")] Guid id,
        [Description("A JSON object of field values to change.")] JsonElement fields,
        CancellationToken ct = default)
    {
        var child = Resolve(contentType, childType);
        var existing = await child.GetChildUpsertViewModelAsync(parentKey, id, ct);
        if (existing == null)
            throw new McpException($"No '{childType}' child found with id '{id}'.");
        var model = McpToolHelpers.Merge(existing, fields);
        return await child.SaveChildUpsertAsync(parentKey, model, ct);
    }

    [McpServerTool(Name = "delete_child", OpenWorld = false)] // Destructive left at its true default
    [Description("Deletes a child entity by id. A content zone item is not deleted but removed: it gets an inactive version (a draft on a page until the page is published; live at once in a global zone), so restore_child_version or a page history revert can bring it back.")]
    public async Task<McpDeleteResult> DeleteChild(
        [Description("The parent content type.")] string contentType,
        [Description("The child type.")] string childType,
        [Description("The id of the child to delete.")] Guid id,
        CancellationToken ct = default)
    {
        var child = Resolve(contentType, childType);
        return new McpDeleteResult(await child.DeleteChildAsync(id, ct));
    }

    [McpServerTool(Name = "reorder_children", Destructive = false, OpenWorld = false)]
    [Description("Reorders the children under a parent to match the given list of ids. Only valid for child types that support reordering (e.g. content zone items).")]
    public async Task<McpReorderResult> ReorderChildren(
        [Description("The parent content type.")] string contentType,
        [Description("The child type.")] string childType,
        [Description("The parent key (slug or Guid).")] string parentKey,
        [Description("The child ids in the desired order.")] List<Guid> orderedIds,
        CancellationToken ct = default)
    {
        var child = Resolve(contentType, childType);
        if (!child.SupportsReorder)
            throw new McpException($"Child type '{childType}' does not support reordering.");
        var (success, errorMessage) = await child.ReorderAsync(parentKey, orderedIds, ct);
        if (!success)
            throw new McpException(errorMessage ?? "The reorder failed.");
        return new McpReorderResult(true);
    }

    [McpServerTool(Name = "move_child", Destructive = false, OpenWorld = false)]
    [Description("Moves a child to a position under a (possibly different) parent. Only valid for child types that support moving (e.g. content zone items, which can move between the zones of one page; the move is a draft until the page is published).")]
    public async Task<McpMoveResult> MoveChild(
        [Description("The parent content type.")] string contentType,
        [Description("The child type.")] string childType,
        [Description("The id of the child to move.")] Guid childId,
        [Description("The key of the parent to move the child under (for content zone items, the target zone's node id; its current zone to reorder).")] string targetParentKey,
        [Description("The 0-based position among the target parent's children, excluding the moved child. Out-of-range values are clamped.")] int position,
        CancellationToken ct = default)
    {
        var child = Resolve(contentType, childType);
        if (!child.SupportsMove)
            throw new McpException($"Child type '{childType}' does not support moving.");
        var (success, errorMessage) = await child.MoveAsync(childId, targetParentKey, position, ct);
        if (!success)
            throw new McpException(errorMessage ?? "The move failed.");
        return new McpMoveResult(true);
    }

    [McpServerTool(Name = "list_child_versions", ReadOnly = true, OpenWorld = false)]
    [Description("Lists the version history for a child entity, identified by its masterId. For a content zone item that holds nested zones (e.g. a Layout), the list also has every version of every widget inside it, newest first (isCombined true): each row's nodeId and widgetLabel say which widget it belongs to, and isLatest/nodeCurrentVersionNumber are per widget.")]
    public async Task<VersionHistoryViewModel> ListChildVersions(
        [Description("The parent content type.")] string contentType,
        [Description("The child type.")] string childType,
        [Description("The parent key (slug or Guid).")] string parentKey,
        [Description("The masterId shared by every version of the child.")] Guid masterId,
        CancellationToken ct = default)
    {
        var child = ResolveVersioned(contentType, childType);
        var vm = await child.GetChildVersionHistoryViewModelAsync(parentKey, masterId, ct);
        if (vm == null)
            throw new McpException($"No version history found for masterId '{masterId}'.");
        return vm;
    }

    [McpServerTool(Name = "restore_child_version", Destructive = false, OpenWorld = false)]
    [Description("Restores a historical version of a child entity as its new current version, in one step. For a content zone item that is its widget, configuration, settings, active state, zone and position (it goes back into its old zone at its old position); it is refused when that zone no longer exists on the page. Restoring a container (e.g. a Layout) to a different view moves its nested widgets into that view's zones. On a page it stays a draft until the page is published.")]
    public async Task<AdminSaveResult> RestoreChildVersion(
        [Description("The parent content type.")] string contentType,
        [Description("The child type.")] string childType,
        [Description("The parent key (slug or Guid).")] string parentKey,
        [Description("The id of the historical version to restore.")] Guid versionId,
        [Description("Optional: the child's current version number as you last read it; the restore is refused if it has changed since.")] int? expectedVersionNumber = null,
        CancellationToken ct = default)
    {
        var child = ResolveVersioned(contentType, childType);
        var result = await child.RestoreChildVersionAsync(parentKey, versionId, expectedVersionNumber, ct);
        if (!result.Success && result.ErrorMessage == IAdminCrudChildHandler.VersionNotFoundMessage)
            throw new McpException($"No version found with id '{versionId}'.");
        return result;
    }

    [McpServerTool(Name = "delete_child_version", OpenWorld = false)] // Destructive left at its true default: permanently deletes a child version
    [Description("Permanently deletes a single historical version of a child entity by its id. The latest version is never deleted (deleted: false). A version that is part of a page history entry is refused with an error naming the entry; delete that entry first (delete_page_history_entry).")]
    public async Task<McpDeleteResult> DeleteChildVersion(
        [Description("The parent content type.")] string contentType,
        [Description("The child type.")] string childType,
        [Description("The id of the version to delete.")] Guid versionId,
        CancellationToken ct = default)
    {
        var child = ResolveVersioned(contentType, childType);
        return McpToolHelpers.DeleteResult(await child.TryDeleteChildVersionAsync(versionId, ct));
    }
}
