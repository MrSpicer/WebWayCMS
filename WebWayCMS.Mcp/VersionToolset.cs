using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Models.Shared;

namespace WebWayCMS.Mcp;

/// <summary>
/// MCP tools for the version history of top-level content types. Mirrors the version routes on
/// <c>AdminContentController</c>, including the <c>SupportsVersionHistory</c> guard.
/// </summary>
[McpServerToolType]
public sealed class VersionToolset
{
    private readonly IAdminHandlerRegistry _registry;

    public VersionToolset(IAdminHandlerRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    private IAdminCrudHandler ResolveVersioned(string contentType)
    {
        var handler = McpToolHelpers.ResolveHandler(_registry, contentType);
        if (!handler.SupportsVersionHistory)
            throw new McpException($"Content type '{contentType}' does not support version history.");
        return handler;
    }

    [McpServerTool(Name = "list_versions")]
    [Description("Lists the version history for a content item, identified by its masterId (shared across all versions).")]
    public async Task<VersionHistoryViewModel> ListVersions(
        [Description("The content type.")] string contentType,
        [Description("The masterId shared by every version of the item.")] Guid masterId,
        CancellationToken ct = default)
    {
        var handler = ResolveVersioned(contentType);
        var vm = await handler.GetVersionHistoryViewModelAsync(masterId, ct);
        if (vm == null)
            throw new McpException($"No version history found for masterId '{masterId}'.");
        return vm;
    }

    [McpServerTool(Name = "get_version")]
    [Description("Gets the editable view model for a single historical version, identified by that version's id.")]
    public async Task<object> GetVersion(
        [Description("The content type.")] string contentType,
        [Description("The id of the historical version.")] Guid versionId,
        CancellationToken ct = default)
    {
        var handler = ResolveVersioned(contentType);
        var vm = await handler.GetRestoreVersionViewModelAsync(versionId, ct);
        if (vm == null)
            throw new McpException($"No version found with id '{versionId}'.");
        return vm;
    }

    [McpServerTool(Name = "restore_version")]
    [Description("Restores a historical version by saving it as the new current version of the item.")]
    public async Task<AdminSaveResult> RestoreVersion(
        [Description("The content type.")] string contentType,
        [Description("The id of the historical version to restore.")] Guid versionId,
        CancellationToken ct = default)
    {
        var handler = ResolveVersioned(contentType);
        var vm = await handler.GetRestoreVersionViewModelAsync(versionId, ct);
        if (vm == null)
            throw new McpException($"No version found with id '{versionId}'.");
        return await handler.SaveUpsertAsync(vm, ct);
    }

    [McpServerTool(Name = "delete_version")]
    [Description("Permanently deletes a single historical version by its id.")]
    public async Task<McpDeleteResult> DeleteVersion(
        [Description("The content type.")] string contentType,
        [Description("The id of the version to delete.")] Guid versionId,
        CancellationToken ct = default)
    {
        var handler = ResolveVersioned(contentType);
        return new McpDeleteResult(await handler.DeleteVersionAsync(versionId, ct));
    }
}
