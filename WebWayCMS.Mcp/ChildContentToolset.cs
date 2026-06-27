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

    [McpServerTool(Name = "list_children")]
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

    [McpServerTool(Name = "get_child")]
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

    [McpServerTool(Name = "create_child")]
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

    [McpServerTool(Name = "update_child")]
    [Description("Updates an existing child entity. Only supplied fields are changed.")]
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

    [McpServerTool(Name = "delete_child")]
    [Description("Deletes a child entity by id.")]
    public async Task<McpDeleteResult> DeleteChild(
        [Description("The parent content type.")] string contentType,
        [Description("The child type.")] string childType,
        [Description("The id of the child to delete.")] Guid id,
        CancellationToken ct = default)
    {
        var child = Resolve(contentType, childType);
        return new McpDeleteResult(await child.DeleteChildAsync(id, ct));
    }

    [McpServerTool(Name = "reorder_children")]
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
        return new McpReorderResult(await child.ReorderAsync(parentKey, orderedIds, ct));
    }

    [McpServerTool(Name = "list_child_versions")]
    [Description("Lists the version history for a child entity, identified by its masterId.")]
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

    [McpServerTool(Name = "restore_child_version")]
    [Description("Restores a historical version of a child entity as its new current version.")]
    public async Task<AdminSaveResult> RestoreChildVersion(
        [Description("The parent content type.")] string contentType,
        [Description("The child type.")] string childType,
        [Description("The parent key (slug or Guid).")] string parentKey,
        [Description("The id of the historical version to restore.")] Guid versionId,
        CancellationToken ct = default)
    {
        var child = ResolveVersioned(contentType, childType);
        var vm = await child.GetChildRestoreVersionViewModelAsync(parentKey, versionId, ct);
        if (vm == null)
            throw new McpException($"No version found with id '{versionId}'.");
        return await child.SaveChildUpsertAsync(parentKey, vm, ct);
    }

    [McpServerTool(Name = "delete_child_version")]
    [Description("Permanently deletes a single historical version of a child entity by its id.")]
    public async Task<McpDeleteResult> DeleteChildVersion(
        [Description("The parent content type.")] string contentType,
        [Description("The child type.")] string childType,
        [Description("The id of the version to delete.")] Guid versionId,
        CancellationToken ct = default)
    {
        var child = ResolveVersioned(contentType, childType);
        return new McpDeleteResult(await child.DeleteChildVersionAsync(versionId, ct));
    }
}