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

    [McpServerTool(Name = "list_versions", ReadOnly = true, OpenWorld = false)]
    [Description("Lists the version history for a content item, identified by its nodeId (shared across all versions).")]
    public async Task<VersionHistoryViewModel> ListVersions(
        [Description("The content type.")] string contentType,
        [Description("The nodeId shared by every version of the item.")] Guid nodeId,
        CancellationToken ct = default)
    {
        var handler = ResolveVersioned(contentType);
        var vm = await handler.GetVersionHistoryViewModelAsync(nodeId, ct);
        if (vm == null)
            throw new McpException($"No version history found for nodeId '{nodeId}'.");
        return vm;
    }

    [McpServerTool(Name = "get_version", ReadOnly = true, OpenWorld = false)]
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

    [McpServerTool(Name = "restore_version", Destructive = false, OpenWorld = false)]
    [Description("Restores a historical version as a new draft of the item in a single step. The MCP endpoint runs with effective admin authority, so this acts as the current user.")]
    public async Task<AdminSaveResult> RestoreVersion(
        [Description("The content type.")] string contentType,
        [Description("The id of the historical version to restore.")] Guid versionId,
        CancellationToken ct = default)
    {
        var handler = ResolveVersioned(contentType);
        return await handler.RestoreVersionAsync(versionId, ct);
    }

    [McpServerTool(Name = "delete_version", OpenWorld = false)] // Destructive left at its true default: permanently deletes a version
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
