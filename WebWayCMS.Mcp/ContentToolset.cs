using System.ComponentModel;
using System.Text.Json;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using WebWayCMS.Controllers.Admin.Handlers;

namespace WebWayCMS.Mcp;

/// <summary>
/// MCP tools for top-level content types, delegating to the same
/// <see cref="IAdminHandlerRegistry"/> the admin UI uses. Covers every registered content type.
/// </summary>
[McpServerToolType]
public sealed class ContentToolset
{
    private readonly IAdminHandlerRegistry _registry;
    private readonly IEnumerable<IAdminCrudHandler> _handlers;

    public ContentToolset(IAdminHandlerRegistry registry, IEnumerable<IAdminCrudHandler> handlers)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
    }

    [McpServerTool(Name = "list_content_types")]
    [Description("Lists every content type the CMS admin exposes (e.g. contentblocks, pages, articles, contentzones), including whether each supports versioning, publishing, child entities, or a component registry.")]
    public IReadOnlyList<McpContentTypeInfo> ListContentTypes() =>
        _handlers
            .Select(h => new McpContentTypeInfo(
                h.ContentType,
                h.DisplayName,
                h.SupportsVersionHistory,
                h.SupportsPublishing,
                h.ChildHandler != null,
                h.ChildHandler?.ChildType,
                h.RegistryHandler != null))
            .ToList();

    [McpServerTool(Name = "describe_content_type")]
    [Description("Describes the editable fields (name, label, editor type, whether required) for a content type's create/edit form. Call this before create_content or update_content to learn which fields to send.")]
    public McpContentTypeDescription DescribeContentType(
        [Description("The content type, e.g. \"contentblocks\" or \"pages\".")] string contentType)
    {
        var handler = McpToolHelpers.ResolveHandler(_registry, contentType);
        var vm = handler.CreateEmptyUpsertViewModel();
        return new McpContentTypeDescription(
            handler.ContentType,
            handler.DisplayName,
            McpToolHelpers.DescribeFields(vm));
    }

    [McpServerTool(Name = "list_content")]
    [Description("Lists existing items of a content type as { id, title } entries.")]
    public async Task<IEnumerable<object>> ListContent(
        [Description("The content type to list.")] string contentType,
        CancellationToken ct = default)
    {
        var handler = McpToolHelpers.ResolveHandler(_registry, contentType);
        return await handler.GetApiListAsync(ct);
    }

    [McpServerTool(Name = "get_content")]
    [Description("Gets the full editable view model for a single content item by its id.")]
    public async Task<object> GetContent(
        [Description("The content type.")] string contentType,
        [Description("The item id (Guid).")] Guid id,
        CancellationToken ct = default)
    {
        var handler = McpToolHelpers.ResolveHandler(_registry, contentType);
        var vm = await handler.GetUpsertViewModelAsync(id, McpToolHelpers.EmptyQuery, ct);
        if (vm == null)
            throw new McpException($"No '{contentType}' item found with id '{id}'.");
        return vm;
    }

    [McpServerTool(Name = "create_content")]
    [Description("Creates a new content item. Supply the fields described by describe_content_type as a JSON object.")]
    public async Task<AdminSaveResult> CreateContent(
        [Description("The content type to create.")] string contentType,
        [Description("A JSON object of field values, e.g. { \"title\": \"Hello\", \"content\": \"<p>Hi</p>\" }.")] JsonElement fields,
        CancellationToken ct = default)
    {
        var handler = McpToolHelpers.ResolveHandler(_registry, contentType);
        var model = McpToolHelpers.Merge(handler.CreateEmptyUpsertViewModel(), fields);
        return await handler.SaveUpsertAsync(model, ct);
    }

    [McpServerTool(Name = "update_content")]
    [Description("Updates an existing content item. Only the supplied fields are changed; existing values are preserved otherwise.")]
    public async Task<AdminSaveResult> UpdateContent(
        [Description("The content type.")] string contentType,
        [Description("The id of the item to update.")] Guid id,
        [Description("A JSON object of field values to change.")] JsonElement fields,
        CancellationToken ct = default)
    {
        var handler = McpToolHelpers.ResolveHandler(_registry, contentType);
        var existing = await handler.GetUpsertViewModelAsync(id, McpToolHelpers.EmptyQuery, ct);
        if (existing == null)
            throw new McpException($"No '{contentType}' item found with id '{id}'.");
        var model = McpToolHelpers.Merge(existing, fields);
        return await handler.SaveUpsertAsync(model, ct);
    }

    [McpServerTool(Name = "delete_content")]
    [Description("Deletes a content item by id.")]
    public async Task<McpDeleteResult> DeleteContent(
        [Description("The content type.")] string contentType,
        [Description("The id of the item to delete.")] Guid id,
        CancellationToken ct = default)
    {
        var handler = McpToolHelpers.ResolveHandler(_registry, contentType);
        return new McpDeleteResult(await handler.DeleteAsync(id, ct));
    }

    [McpServerTool(Name = "publish_content")]
    [Description("Publishes the current draft of a content item, making it the live published version. The MCP endpoint runs with effective admin authority, so this acts as an authorized publisher.")]
    public async Task<AdminSaveResult> PublishContent(
        [Description("The content type.")] string contentType,
        [Description("The node id of the item to publish.")] Guid nodeId,
        CancellationToken ct = default)
    {
        var handler = McpToolHelpers.ResolveHandler(_registry, contentType);
        if (!handler.SupportsPublishing)
            throw new McpException($"Content type '{contentType}' does not support publishing.");
        return await handler.PublishAsync(nodeId, ct);
    }

    [McpServerTool(Name = "unpublish_content")]
    [Description("Unpublishes the current published version of a content item. If a separate draft exists it is left untouched and the published version is archived.")]
    public async Task<AdminSaveResult> UnpublishContent(
        [Description("The content type.")] string contentType,
        [Description("The node id of the item to unpublish.")] Guid nodeId,
        CancellationToken ct = default)
    {
        var handler = McpToolHelpers.ResolveHandler(_registry, contentType);
        if (!handler.SupportsPublishing)
            throw new McpException($"Content type '{contentType}' does not support publishing.");
        return await handler.UnpublishAsync(nodeId, ct);
    }

    [McpServerTool(Name = "get_content_state")]
    [Description("Gets the current draft/published state of a content item as { isPublished, hasDraft, currentVersionNumber }.")]
    public async Task<object> GetContentState(
        [Description("The content type.")] string contentType,
        [Description("The node id of the item.")] Guid nodeId,
        CancellationToken ct = default)
    {
        var handler = McpToolHelpers.ResolveHandler(_registry, contentType);
        var vm = await handler.GetVersionHistoryViewModelAsync(nodeId, ct);
        if (vm == null)
            throw new McpException($"No '{contentType}' item found with id '{nodeId}'.");
        return new
        {
            isPublished = vm.Versions.Any(v => v.IsPublished),
            hasDraft = vm.Versions.Any(v => v.State == WebWayCMS.Data.Models.ContentVersionState.Draft && v.IsLatest),
            currentVersionNumber = vm.Versions.FirstOrDefault(v => v.IsLatest)?.Version
        };
    }

    [McpServerTool(Name = "list_registry")]
    [Description("Lists the registry entries for a content type that has one (e.g. available page controllers for \"pages\", or view components for \"contentzones\").")]
    public object ListRegistry(
        [Description("The content type whose registry to list.")] string contentType)
    {
        var handler = McpToolHelpers.ResolveHandler(_registry, contentType);
        if (handler.RegistryHandler == null)
            throw new McpException($"Content type '{contentType}' has no registry.");
        return McpToolHelpers.Unwrap(handler.RegistryHandler.GetAll())!;
    }

    [McpServerTool(Name = "get_registry_properties")]
    [Description("Gets the configurable properties for a single registry entry (e.g. a page controller's configuration fields).")]
    public object GetRegistryProperties(
        [Description("The content type whose registry to query.")] string contentType,
        [Description("The registry entry name.")] string name)
    {
        var handler = McpToolHelpers.ResolveHandler(_registry, contentType);
        if (handler.RegistryHandler == null)
            throw new McpException($"Content type '{contentType}' has no registry.");
        return McpToolHelpers.Unwrap(handler.RegistryHandler.GetProperties(name))!;
    }
}