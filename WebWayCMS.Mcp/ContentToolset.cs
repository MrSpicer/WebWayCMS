using System.ComponentModel;
using System.Text.Json;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services.ContentTypes;
using WebWayCMS.Services.Relations;

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
    private readonly IMcpCaller _caller;
    private readonly ContentRelationRegistry? _relations;
    private readonly IContentTypeRegistry? _contentTypes;
    private readonly IContentTypeRegistrationService? _registrations;

    public ContentToolset(
        IAdminHandlerRegistry registry,
        IEnumerable<IAdminCrudHandler> handlers,
        IMcpCaller caller,
        ContentRelationRegistry? relations = null,
        IContentTypeRegistry? contentTypes = null,
        IContentTypeRegistrationService? registrations = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        _caller = caller ?? throw new ArgumentNullException(nameof(caller));
        _relations = relations;
        _contentTypes = contentTypes;
        _registrations = registrations;
    }

    [McpServerTool(Name = "list_content_types", ReadOnly = true, OpenWorld = false)]
    [Description("Lists every content type the CMS knows (e.g. contentblocks, pages, articles, contentzones) plus the admin surfaces over other tables (isContentType false, e.g. cmsroutes): whether each supports versioning, publishing or a component registry, its childTypes (the content types that can be placed under it: list_children / create_child), and the names, description and category admins gave it. A content type with hasAdminHandler false is registered in code without an admin screen, so the content tools can't edit it.")]
    public IReadOnlyList<McpContentTypeInfo> ListContentTypes()
    {
        var names = _registrations?.GetPublishedByKey() ?? new Dictionary<string, ContentTypeRegistrationDTO>();
        var listed = new List<McpContentTypeInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var handler in _handlers)
        {
            seen.Add(handler.ContentType);
            listed.Add(WithRegistration(
                new McpContentTypeInfo(
                    handler.ContentType,
                    names.DisplayNameOr(handler.ContentType, handler.DisplayName),
                    handler.SupportsVersionHistory,
                    handler.SupportsPublishing,
                    ChildTypes(handler.ContentType),
                    handler.RegistryHandler != null,
                    handler.SecondaryApiListKeys)
                {
                    IsContentType = handler.IsContentType,
                },
                names));
        }

        foreach (var descriptor in _contentTypes?.All ?? [])
        {
            if (!seen.Add(descriptor.Key))
                continue;
            listed.Add(WithRegistration(
                new McpContentTypeInfo(
                    descriptor.Key,
                    names.DisplayNameOr(descriptor.Key, descriptor.DisplayName ?? ContentTypeDescriptor.DeriveDisplayName(descriptor.ClrType)),
                    SupportsVersionHistory: false,
                    SupportsPublishing: false,
                    ChildTypes(descriptor.Key),
                    HasRegistry: false,
                    SecondaryApiListKeys: [])
                {
                    HasAdminHandler = false,
                },
                names));
        }

        return listed;
    }

    private IReadOnlyList<string> ChildTypes(string contentType)
        => _relations?.GetContainmentsTargeting(contentType)
               .Select(d => d.SourceContentType)
               .Distinct(StringComparer.OrdinalIgnoreCase)
               .ToList()
           ?? [];

    private static McpContentTypeInfo WithRegistration(
        McpContentTypeInfo info, IReadOnlyDictionary<string, ContentTypeRegistrationDTO> names)
        => names.TryGetValue(info.ContentType, out var registration)
            ? info with
            {
                PluralDisplayName = registration.PluralDisplayName,
                Description = registration.Description,
                Category = registration.Category,
            }
            : info;

    [McpServerTool(Name = "describe_content_type", ReadOnly = true, OpenWorld = false)]
    [Description("Describes the editable fields (name, label, editor type, whether required) for a content type's create/edit form. A field that links to other content carries a relation block { kind, target, many, required }: send the target item's id (or an array of ids when many). Call this before create_content or update_content to learn which fields to send.")]
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

    [McpServerTool(Name = "list_content", ReadOnly = true, OpenWorld = false)]
    [Description("Lists existing items of a content type as { id, title } entries. Articles and the article lists they belong to are separate types: list \"articles\" for articles and \"articlelists\" for lists.")]
    public async Task<IEnumerable<object>> ListContent(
        [Description("The content type to list.")] string contentType,
        CancellationToken ct = default)
    {
        var handler = McpToolHelpers.ResolveHandler(_registry, contentType);
        return await handler.GetApiListAsync(ct);
    }

    [McpServerTool(Name = "list_secondary_content", ReadOnly = true, OpenWorld = false)]
    [Description("Lists a content type's additional named list as { id, title } entries. Use list_content_types to discover which keys a content type exposes (secondaryApiListKeys).")]
    public async Task<IEnumerable<object>> ListSecondaryContent(
        [Description("The content type that owns the secondary list.")] string contentType,
        [Description("The secondary list key, e.g. \"articlelists\".")] string key,
        CancellationToken ct = default)
    {
        var handler = McpToolHelpers.ResolveHandler(_registry, contentType);
        if (!handler.SecondaryApiListKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            var message = handler.SecondaryApiListKeys.Count == 0
                ? $"Content type '{contentType}' exposes no secondary lists."
                : $"Content type '{contentType}' has no secondary list '{key}'. Valid keys: {string.Join(", ", handler.SecondaryApiListKeys)}.";
            throw new McpException(message);
        }
        return await handler.GetSecondaryApiListAsync(key, ct);
    }

    [McpServerTool(Name = "get_content", ReadOnly = true, OpenWorld = false)]
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

    [McpServerTool(Name = "create_content", Destructive = false, OpenWorld = false)]
    [Description("Creates a new content item. Supply the fields described by describe_content_type as a JSON object.")]
    public async Task<AdminSaveResult> CreateContent(
        [Description("The content type to create.")] string contentType,
        [Description("A JSON object of field values, e.g. { \"title\": \"Hello\", \"content\": \"<p>Hi</p>\" }.")] JsonElement fields,
        CancellationToken ct = default)
    {
        var handler = McpToolHelpers.ResolveHandler(_registry, contentType);
        McpToolHelpers.RequireWrite(handler, _caller);
        var model = McpToolHelpers.Merge(handler.CreateEmptyUpsertViewModel(), fields);
        return await handler.SaveUpsertAsync(model, ct);
    }

    [McpServerTool(Name = "update_content", OpenWorld = false)] // Destructive left at its true default: overwrites existing values
    [Description("Updates an existing content item. Only the supplied fields are changed; existing values are preserved otherwise.")]
    public async Task<AdminSaveResult> UpdateContent(
        [Description("The content type.")] string contentType,
        [Description("The id of the item to update.")] Guid id,
        [Description("A JSON object of field values to change.")] JsonElement fields,
        CancellationToken ct = default)
    {
        var handler = McpToolHelpers.ResolveHandler(_registry, contentType);
        McpToolHelpers.RequireWrite(handler, _caller);
        var existing = await handler.GetUpsertViewModelAsync(id, McpToolHelpers.EmptyQuery, ct);
        if (existing == null)
            throw new McpException($"No '{contentType}' item found with id '{id}'.");
        var model = McpToolHelpers.Merge(existing, fields);
        return await handler.SaveUpsertAsync(model, ct);
    }

    [McpServerTool(Name = "delete_content", OpenWorld = false)] // Destructive left at its true default: deletes an item permanently
    [Description("Deletes a content item by id, together with the items it contains (an article list's articles; a page's whole subtree of child pages, with their zones and routes); deletedDescendants counts them, leaving out zones and widgets. A delete is refused, with the reason, while another item still references it.")]
    public async Task<McpDeleteResult> DeleteContent(
        [Description("The content type.")] string contentType,
        [Description("The id of the item to delete.")] Guid id,
        CancellationToken ct = default)
    {
        var handler = McpToolHelpers.ResolveHandler(_registry, contentType);
        McpToolHelpers.RequireWrite(handler, _caller);
        var result = await handler.TryDeleteAsync(id, ct);
        if (!result.Success && result.ErrorMessage != null)
            throw new McpException(result.ErrorMessage);
        return new McpDeleteResult(result.Success, result.DeletedDescendants);
    }

    [McpServerTool(Name = "publish_content", Idempotent = true, OpenWorld = false)] // Destructive left at its true default: swaps what the live site serves
    [Description("Publishes the current draft of a content item, making it the live published version. It acts as your token's user and needs that type's publish role.")]
    public async Task<AdminSaveResult> PublishContent(
        [Description("The content type.")] string contentType,
        [Description("The node id of the item to publish.")] Guid nodeId,
        CancellationToken ct = default)
    {
        var handler = McpToolHelpers.ResolveHandler(_registry, contentType);
        McpToolHelpers.RequirePublish(handler, _caller);
        if (!handler.SupportsPublishing)
            throw new McpException($"Content type '{contentType}' does not support publishing.");
        return await handler.PublishAsync(nodeId, ct);
    }

    [McpServerTool(Name = "unpublish_content", OpenWorld = false)] // Destructive left at its true default: removes content from the live site
    [Description("Unpublishes the current published version of a content item. If a separate draft exists it is left untouched and the published version is archived.")]
    public async Task<AdminSaveResult> UnpublishContent(
        [Description("The content type.")] string contentType,
        [Description("The node id of the item to unpublish.")] Guid nodeId,
        CancellationToken ct = default)
    {
        var handler = McpToolHelpers.ResolveHandler(_registry, contentType);
        McpToolHelpers.RequirePublish(handler, _caller);
        if (!handler.SupportsPublishing)
            throw new McpException($"Content type '{contentType}' does not support publishing.");
        return await handler.UnpublishAsync(nodeId, ct);
    }

    [McpServerTool(Name = "get_content_state", ReadOnly = true, OpenWorld = false)]
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

    [McpServerTool(Name = "list_registry", ReadOnly = true, OpenWorld = false)]
    [Description("Lists the registry entries for a content type that has one (e.g. available page controllers for \"pages\", or view components for \"contentzones\").")]
    public object ListRegistry(
        [Description("The content type whose registry to list.")] string contentType)
    {
        var handler = McpToolHelpers.ResolveHandler(_registry, contentType);
        if (handler.RegistryHandler == null)
            throw new McpException($"Content type '{contentType}' has no registry.");
        return McpToolHelpers.Unwrap(handler.RegistryHandler.GetAll())!;
    }

    [McpServerTool(Name = "get_registry_properties", ReadOnly = true, OpenWorld = false)]
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