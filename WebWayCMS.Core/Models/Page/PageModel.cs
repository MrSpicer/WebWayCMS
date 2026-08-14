using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Interfaces;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Shared;
using WebWayCMS.Pages;
using WebWayCMS.Services;

namespace WebWayCMS.Models.Page;

public sealed class PageModel : AdminCrudModel<PageDTO>, IPageModel, IRoutableContent
{
    private readonly IContentStore<PageDTO> _store;
    private readonly IMapper _mapper;
    private readonly PageRegistryHandler _registryHandler;
    private readonly IRouteRegistrationService _routeRegistration;
    private readonly ICMSRouteService _routeService;
    private readonly IPageControllerRegistry _controllerRegistry;

    protected override IContentStore<PageDTO> Store => _store;

    protected override string VersionHistoryContentType => "pages";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/wadmin/pages";
    protected override Task<List<PageDTO>> GetAllVersionsAsync(Guid nodeId, CancellationToken ct) => _store.GetAllVersionsAsync(nodeId, ct);
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct) => _store.DeleteVersionAsync(id, ct);

    public override string ContentType => "pages";
    public override string DisplayName => "Page";
    public override string IndexViewPath => "~/Views/AdminPage/Pages.cshtml";
    public override string UpsertViewPath => "~/Views/AdminPage/PageUpsert.cshtml";
    public override IAdminRegistryHandler? RegistryHandler => _registryHandler;
    public override bool SupportsPreview => true;

    string IRoutableContent.RouteContentType => "Page";

    public PageModel(
        IContentStore<PageDTO> store,
        IMapper mapper,
        IPageControllerRegistry registry,
        IViewDiscoveryService viewDiscovery,
        IRouteRegistrationService routeRegistration,
        ICMSRouteService routeService,
        IChangeSetScope changeSetScope)
        : base(changeSetScope)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _routeRegistration = routeRegistration ?? throw new ArgumentNullException(nameof(routeRegistration));
        _routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
        _controllerRegistry = registry ?? throw new ArgumentNullException(nameof(registry));
        _registryHandler = new PageRegistryHandler(
            registry,
            viewDiscovery ?? throw new ArgumentNullException(nameof(viewDiscovery)));
    }

    async Task<IReadOnlyList<CMSRouteDTO>> IRoutableContent.GetRoutesAsync(Guid contentNodeId, CancellationToken ct)
    {
        var routes = await _routeService.GetByOwningContentAsync(contentNodeId, ct);
        return routes.AsReadOnly();
    }

    public async Task<PageIndexViewModel> GetPageIndexAsync(CancellationToken ct = default)
    {
        var pages = await _store.GetAllCurrentDraftsAsync(ct);
        var activeRoutes = await _routeService.GetAllRoutesAsync(ct);
        var publishedNodeIds = await _store.GetPublishedNodeIdsAsync(ct) ?? [];
        return new PageIndexViewModel { Pages = BuildTree(pages, activeRoutes, publishedNodeIds) };
    }

    public async Task<(bool Success, string? ErrorMessage)> SavePageUpsertAsync(PageUpsertViewModel model, CancellationToken ct = default)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));

        var dto = _mapper.Map<PageDTO>(model);
        var result = await _store.SaveDraftAsync(dto, model.ExpectedVersionNumber, ct);
        if (!result.Success) return (false, result.ErrorMessage ?? "Failed to update page.");
        model.NodeId = result.NodeId;
        return (true, null);
    }

    public async Task<bool> DeletePageAsync(Guid nodeId, CancellationToken ct = default)
    {
        var entity = await _store.GetCurrentDraftAsync(nodeId, ct);
        if (entity != null)
            await _routeRegistration.UnregisterContentRoutesAsync(nodeId, ct);
        return await _store.DeleteAsync(nodeId, softDelete: false, ct);
    }

    public Task<VersionHistoryViewModel?> GetVersionHistoryAsync(Guid nodeId, CancellationToken ct = default)
        => BuildVersionHistoryAsync(nodeId, ct: ct);

    public Task<bool> DeletePageVersionAsync(Guid id, CancellationToken ct = default)
        => DeleteVersionCoreAsync(id, ct);

    public async Task<(bool Success, string? ErrorMessage)> PublishPageAsync(Guid nodeId, CancellationToken ct = default)
    {
        // Validate the draft that will be published *before* flipping any state, so a slug collision
        // fails without leaving the page Published-but-routeless.
        var draft = await _store.GetCurrentDraftAsync(nodeId, ct);
        if (draft == null) return (false, "Page not found.");

        var parentNodeId = draft.Version.Node.ParentNodeId;
        var slug = System.Net.WebUtility.UrlDecode(draft.Version.Slug);

        if (!await IsSlugAvailableAsync(slug, parentNodeId, nodeId, ct))
            return (false, "A page with this slug already exists at this location.");

        var routePattern = await DeriveRoutePatternForPublishAsync(slug, nodeId, parentNodeId, ct);

        var result = await _store.PublishAsync(nodeId, ct);
        if (!result.Success) return (false, result.ErrorMessage);

        // Replace the page's route (a slug rename must drop the old pattern, not accumulate it).
        var previousRoutes = await _routeService.GetByOwningContentAsync(nodeId, ct);
        await _routeRegistration.UnregisterContentRoutesAsync(nodeId, ct);
        var registration = await _routeRegistration.RegisterContentRoutesAsync(this, routePattern, draft.ControllerName, nodeId, ct);
        if (!registration.Success)
        {
            // Roll the state flip back so a failed registration never leaves a published page
            // with no route (or with a stale route).
            await _store.UnpublishAsync(nodeId, ct);

            // Restore the prior route(s) so a republish whose new route collides still resolves.
            foreach (var previous in previousRoutes)
                await _routeService.UpsertAsync(previous, ct);

            return (false, registration.ErrorMessage ?? "Failed to register the page route.");
        }

        return (true, null);
    }

    public async Task<(bool Success, string? ErrorMessage)> UnpublishPageAsync(Guid nodeId, CancellationToken ct = default)
    {
        var result = await _store.UnpublishAsync(nodeId, ct);
        if (!result.Success) return (false, result.ErrorMessage);

        await _routeRegistration.UnregisterContentRoutesAsync(nodeId, ct);
        return (true, null);
    }

    // IAdminCrudHandler members
    public override async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
        => await GetPageIndexAsync(ct);

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
    {
        if (id.HasValue && id != Guid.Empty)
        {
            var dto = await _store.GetCurrentDraftAsync(id.Value, ct);
            if (dto == null) return null;

            var vm = _mapper.Map<PageUpsertViewModel>(dto);
            await PopulateParentageAsync(vm, dto, ct);
            return vm;
        }

        var newVm = new PageUpsertViewModel();
        var parentRoute = query["parentRoute"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(parentRoute))
        {
            parentRoute = parentRoute.TrimEnd('/');
            if (!parentRoute.StartsWith('/'))
                parentRoute = "/" + parentRoute;
            newVm.ParentRoutePrefix = parentRoute;

            var parentNodeId = await ResolveParentNodeIdAsync(parentRoute, ct);
            if (parentNodeId.HasValue)
                newVm.ParentNodeId = parentNodeId.Value;
        }
        return newVm;
    }

    public override object CreateEmptyUpsertViewModel() => new PageUpsertViewModel();

    protected override async Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        var vm = (PageUpsertViewModel)model;

        var excludeNodeId = vm.NodeId.HasValue && vm.NodeId != Guid.Empty ? vm.NodeId : null;
        var effectiveSlug = string.IsNullOrWhiteSpace(vm.Slug) ? vm.Title : vm.Slug;

        var slugAvailable = await IsSlugAvailableAsync(effectiveSlug, vm.ParentNodeId, excludeNodeId, ct);
        if (!slugAvailable)
            return new AdminSaveResult(false, "A page with this slug already exists at this location.", "Slug");

        var validationErrors = _controllerRegistry.ValidateConfiguration(vm.ControllerName, vm.ConfigurationJson);
        if (validationErrors.Count > 0)
            return new AdminSaveResult(false, string.Join(" ", validationErrors), "ConfigurationJson");

        var result = await SavePageUpsertAsync(vm, ct);
        return result.Success
            ? new AdminSaveResult(true, NodeId: vm.NodeId)
            : new AdminSaveResult(false, result.ErrorMessage);
    }

    public override async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        => await DeletePageAsync(id, ct);

    public override async Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default)
    {
        var vm = await GetPageIndexAsync(ct);
        return vm.Pages
            .Where(n => n.PageNodeId.HasValue)
            .Select(n => (object)new { id = n.PageNodeId!.Value, title = n.Title });
    }

    public override async Task<AdminSaveResult> PublishAsync(Guid nodeId, CancellationToken ct = default)
    {
        var result = await PublishPageAsync(nodeId, ct);
        return result.Success
            ? new AdminSaveResult(true)
            : new AdminSaveResult(false, result.ErrorMessage);
    }

    public override async Task<AdminSaveResult> UnpublishAsync(Guid nodeId, CancellationToken ct = default)
    {
        var result = await UnpublishPageAsync(nodeId, ct);
        return result.Success
            ? new AdminSaveResult(true)
            : new AdminSaveResult(false, result.ErrorMessage);
    }

    public override async Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default)
    {
        var loaded = await LoadRestoreVersionAsync(historicalId, ct);
        if (loaded == null) return null;
        var vm = _mapper.Map<PageUpsertViewModel>(loaded.Value.Historical);
        vm.ExpectedVersionNumber = loaded.Value.CurrentVersionNumber;
        await PopulateParentageAsync(vm, loaded.Value.Historical, ct);
        return vm;
    }

    public override Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default)
        => DeletePageVersionAsync(id, ct);

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static List<PageTreeNode> BuildTree(List<PageDTO> pages, List<CMSRouteDTO> activeRoutes, HashSet<Guid> publishedNodeIds)
    {
        var routeMap = activeRoutes
            .Where(r => r.OwningContentType == "Page" && r.OwningContentNodeId.HasValue)
            .GroupBy(r => r.OwningContentNodeId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var pageRouteMap = BuildPageRouteMap(pages, routeMap);

        var sortedPages = pages
            .Where(p => pageRouteMap.ContainsKey(p.Version.Node.Id))
            .OrderBy(p => pageRouteMap[p.Version.Node.Id])
            .ToList();

        var roots = new List<PageTreeNode>();
        var nodeMap = new Dictionary<string, PageTreeNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var page in sortedPages)
        {
            if (!pageRouteMap.TryGetValue(page.Version.Node.Id, out var currentRoute))
                continue;

            if (currentRoute == "/")
            {
                if (!nodeMap.TryGetValue("/", out var rootNode))
                {
                    rootNode = new PageTreeNode
                    {
                        Path = "/",
                        Title = page.Version.Title,
                        PageNodeId = page.Version.Node.Id,
                        IsPublished = IsNodePublished(page.Version.Node.Id, publishedNodeIds),
                        HasPendingChanges = HasPendingChanges(page.Version.Node.Id, page.Version.State, publishedNodeIds),
                        IsHidden = page.Version.Node.IsHidden
                    };
                    nodeMap["/"] = rootNode;
                    roots.Insert(0, rootNode);
                }
                else
                {
                    rootNode.Title = page.Version.Title;
                    rootNode.PageNodeId = page.Version.Node.Id;
                    rootNode.IsPublished = IsNodePublished(page.Version.Node.Id, publishedNodeIds);
                    rootNode.HasPendingChanges = HasPendingChanges(page.Version.Node.Id, page.Version.State, publishedNodeIds);
                    rootNode.IsHidden = page.Version.Node.IsHidden;
                }
                continue;
            }

            var segments = currentRoute.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var currentPath = "";

            for (int i = 0; i < segments.Length; i++)
            {
                currentPath = "/" + string.Join("/", segments.Take(i + 1));
                var isLeaf = i == segments.Length - 1;

                if (!nodeMap.TryGetValue(currentPath, out var node))
                {
                    node = new PageTreeNode
                    {
                        Path = currentPath,
                        Title = isLeaf ? page.Version.Title : segments[i],
                        PageNodeId = isLeaf ? page.Version.Node.Id : null,
                        IsPublished = isLeaf && IsNodePublished(page.Version.Node.Id, publishedNodeIds),
                        HasPendingChanges = isLeaf && HasPendingChanges(page.Version.Node.Id, page.Version.State, publishedNodeIds),
                        IsHidden = isLeaf && page.Version.Node.IsHidden
                    };
                    nodeMap[currentPath] = node;

                    if (i == 0)
                    {
                        roots.Add(node);
                    }
                    else
                    {
                        var parentPath = "/" + string.Join("/", segments.Take(i));
                        if (nodeMap.TryGetValue(parentPath, out var parentNode))
                        {
                            parentNode.Children.Add(node);
                        }
                    }
                }
                else if (isLeaf)
                {
                    node.Title = page.Version.Title;
                    node.PageNodeId = page.Version.Node.Id;
                    node.IsPublished = IsNodePublished(page.Version.Node.Id, publishedNodeIds);
                    node.HasPendingChanges = HasPendingChanges(page.Version.Node.Id, page.Version.State, publishedNodeIds);
                    node.IsHidden = page.Version.Node.IsHidden;
                }
            }
        }

        return roots;
    }

    private static Dictionary<Guid, string> BuildPageRouteMap(List<PageDTO> pages, Dictionary<Guid, CMSRouteDTO> routeMap)
    {
        var pageRouteMap = new Dictionary<Guid, string>();

        // Pages with a route row use its pattern directly.
        foreach (var page in pages)
        {
            if (routeMap.TryGetValue(page.Version.Node.Id, out var route))
                pageRouteMap[page.Version.Node.Id] = route.Pattern;
        }

        // Pages with no route row (unpublished) derive their path by walking ParentNodeId, so
        // unpublished children nest under their parent instead of surfacing at root.
        var unresolved = pages.Where(p => !pageRouteMap.ContainsKey(p.Version.Node.Id)).ToList();
        while (unresolved.Count > 0)
        {
            var progressed = false;
            foreach (var page in unresolved.ToList())
            {
                var parentId = page.Version.Node.ParentNodeId;
                if (parentId.HasValue && pageRouteMap.TryGetValue(parentId.Value, out var parentPath))
                {
                    pageRouteMap[page.Version.Node.Id] = CombinePath(parentPath, System.Net.WebUtility.UrlDecode(page.Version.Slug));
                    unresolved.Remove(page);
                    progressed = true;
                }
            }

            if (!progressed)
                break;
        }

        foreach (var page in unresolved)
            pageRouteMap[page.Version.Node.Id] = DerivePatternFromSlug(page.Version.Slug);

        return pageRouteMap;
    }

    private static string CombinePath(string parentPath, string slug)
    {
        var trimmed = parentPath.TrimEnd('/');
        return (trimmed.Length == 0 ? string.Empty : trimmed) + "/" + slug;
    }

    private static bool IsNodePublished(Guid nodeId, HashSet<Guid> publishedNodeIds)
        => publishedNodeIds.Contains(nodeId);

    private static bool HasPendingChanges(Guid nodeId, ContentVersionState state, HashSet<Guid> publishedNodeIds)
        => publishedNodeIds.Contains(nodeId) && state != ContentVersionState.Published;

    private async Task<string> DeriveRoutePatternForPublishAsync(string slug, Guid nodeId, Guid? parentNodeId, CancellationToken ct)
    {
        var prefix = await GetParentRoutePrefixAsync(parentNodeId, ct);

        if (prefix == null)
        {
            var existingRoutes = await _routeService.GetByOwningContentAsync(nodeId, ct);
            var existing = existingRoutes.FirstOrDefault();
            if (existing != null)
            {
                var lastSlash = existing.Pattern.LastIndexOf('/');
                prefix = lastSlash > 0 ? existing.Pattern[..lastSlash] : null;
            }
        }

        return DeriveRoutePatternFromSlug(System.Net.WebUtility.UrlDecode(slug), prefix);
    }

    private static string DeriveRoutePatternFromSlug(string slug, string? parentRoutePrefix)
    {
        if (!string.IsNullOrWhiteSpace(parentRoutePrefix))
        {
            parentRoutePrefix = parentRoutePrefix.TrimEnd('/');
            return parentRoutePrefix + "/" + slug;
        }
        if (string.Equals(slug, "home", StringComparison.OrdinalIgnoreCase))
            return "/";
        return "/" + slug;
    }

    private static string DerivePatternFromSlug(string slug)
    {
        var decoded = System.Net.WebUtility.UrlDecode(slug);
        if (string.Equals(decoded, "home", StringComparison.OrdinalIgnoreCase))
            return "/";
        return "/" + decoded;
    }

    private async Task<bool> IsSlugAvailableAsync(string? slug, Guid? parentNodeId, Guid? excludeNodeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return false;

        var normalizedSlug = Uri.EscapeDataString(slug);

        var siblings = await _store.GetCurrentDraftChildrenAsync(parentNodeId, ct) ?? [];
        if (siblings.Any(p =>
                (excludeNodeId == null || p.Version.Node.Id != excludeNodeId.Value)
             && p.Version.Slug == normalizedSlug))
            return false;

        var prefix = await GetParentRoutePrefixAsync(parentNodeId, ct);
        var routePattern = DeriveRoutePatternFromSlug(slug, prefix);
        return await _routeService.IsPatternAvailableAsync(routePattern, excludeNodeId, null, ct);
    }

    private async Task<string?> GetParentRoutePrefixAsync(Guid? parentNodeId, CancellationToken ct)
    {
        if (!parentNodeId.HasValue)
            return null;

        var routes = await _routeService.GetByOwningContentAsync(parentNodeId.Value, ct);
        return routes.FirstOrDefault()?.Pattern;
    }

    private async Task<Guid?> ResolveParentNodeIdAsync(string pattern, CancellationToken ct)
    {
        var routes = await _routeService.GetAllRoutesAsync(ct) ?? [];
        var direct = routes.FirstOrDefault(r => r.Pattern == pattern && r.OwningContentNodeId.HasValue);
        if (direct != null)
            return direct.OwningContentNodeId;

        // No route row for this path: the parent is unpublished. Walk the current drafts through the
        // same path-derivation the admin tree uses so a child can still nest under an unpublished parent.
        var pages = await _store.GetAllCurrentDraftsAsync(ct) ?? [];
        var routeMap = routes
            .Where(r => r.OwningContentType == "Page" && r.OwningContentNodeId.HasValue)
            .GroupBy(r => r.OwningContentNodeId!.Value)
            .ToDictionary(g => g.Key, g => g.First());
        var pageRouteMap = BuildPageRouteMap(pages, routeMap);

        var match = pageRouteMap.FirstOrDefault(kv => kv.Value == pattern);
        return match.Key == Guid.Empty ? null : match.Key;
    }

    private async Task PopulateParentageAsync(PageUpsertViewModel vm, PageDTO dto, CancellationToken ct)
    {
        var parentNodeId = dto.Version.Node.ParentNodeId;
        if (!parentNodeId.HasValue)
            return;

        vm.ParentNodeId = parentNodeId.Value;
        var parentRoutes = await _routeService.GetByOwningContentAsync(parentNodeId.Value, ct);
        vm.ParentRoutePrefix = parentRoutes.FirstOrDefault()?.Pattern;
    }
}

internal sealed class PageRegistryHandler : IAdminRegistryHandler
{
    private readonly IPageControllerRegistry _registry;
    private readonly IViewDiscoveryService _viewDiscovery;

    public PageRegistryHandler(IPageControllerRegistry registry, IViewDiscoveryService viewDiscovery)
    {
        _registry = registry;
        _viewDiscovery = viewDiscovery ?? throw new ArgumentNullException(nameof(viewDiscovery));
    }

    public IActionResult GetAll()
    {
        var controllers = _registry.GetAllControllers().Select(c => new
        {
            name = c.Name,
            displayName = c.DisplayName,
            description = c.Description,
            category = c.Category
        }).ToList();

        return new JsonResult(controllers);
    }

    public IActionResult GetProperties(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new BadRequestObjectResult(new { error = "Controller name is required." });

        var controller = _registry.GetByName(name);
        if (controller == null)
            return new NotFoundObjectResult(new { error = $"Controller '{name}' not found." });

        var properties = controller.Properties.Select(p => new
        {
            name = p.Name,
            label = p.Label,
            helpText = p.HelpText,
            placeholder = p.Placeholder,
            editorType = p.EditorType.ToString().ToLowerInvariant(),
            isRequired = p.IsRequired,
            defaultValue = p.DefaultValue,
            order = p.Order,
            group = p.Group,
            entityType = p.EntityType,
            dropdownOptions = p.DropdownOptions,
            viewComponentName = p.ViewComponentName,
            min = p.Min,
            max = p.Max,
            maxLength = p.MaxLength
        }).OrderBy(p => p.order).ToList();

        var availableViews = _viewDiscovery.GetControllerViews(name);

        return new JsonResult(new
        {
            controllerName = controller.Name,
            displayName = controller.DisplayName,
            category = controller.Category,
            availableViews,
            properties
        });
    }

    public IActionResult GetForm(string name, string? valuesJson)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new BadRequestObjectResult(new { error = "Controller name is required." });

        var controller = _registry.GetByName(name);
        if (controller == null)
            return new NotFoundObjectResult(new { error = $"Controller '{name}' not found." });

        object? instance = null;
        if (controller.ConfigurationType != null)
            instance = WebWayCMS.Forms.DynamicConfigurationForm.Materialize(controller.ConfigurationType, valuesJson);

        return WebWayCMS.Forms.DynamicConfigurationForm.Render(instance);
    }
}
