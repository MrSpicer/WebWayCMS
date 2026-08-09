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
    private readonly IPageService _service;
    private readonly IMapper _mapper;
    private readonly PageRegistryHandler _registryHandler;
    private readonly IRouteRegistrationService _routeRegistration;
    private readonly ICMSRouteService _routeService;
    private readonly IPageControllerRegistry _controllerRegistry;

    protected override string VersionHistoryContentType => "pages";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/admin/pages";
    protected override Task<List<PageDTO>> GetAllVersionsAsync(Guid masterId, CancellationToken ct) => _service.GetAllVersionsAsync(masterId, ct);
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct) => _service.DeleteVersionAsync(id, ct);

    public override string ContentType => "pages";
    public override string DisplayName => "Page";
    public override string IndexViewPath => "~/Views/AdminPage/Pages.cshtml";
    public override string UpsertViewPath => "~/Views/AdminPage/PageUpsert.cshtml";
    public override IAdminRegistryHandler? RegistryHandler => _registryHandler;

    string IRoutableContent.RouteContentType => "Page";

    public PageModel(
        IPageService service,
        IMapper mapper,
        IPageControllerRegistry registry,
        IViewDiscoveryService viewDiscovery,
        IRouteRegistrationService routeRegistration,
        ICMSRouteService routeService)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _routeRegistration = routeRegistration ?? throw new ArgumentNullException(nameof(routeRegistration));
        _routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
        _controllerRegistry = registry ?? throw new ArgumentNullException(nameof(registry));
        _registryHandler = new PageRegistryHandler(
            registry ?? throw new ArgumentNullException(nameof(registry)),
            viewDiscovery ?? throw new ArgumentNullException(nameof(viewDiscovery)));
    }

    async Task<IReadOnlyList<CMSRouteDTO>> IRoutableContent.GetRoutesAsync(Guid contentMasterId, CancellationToken ct)
    {
        var routes = await _routeService.GetByOwningContentAsync(contentMasterId, ct);
        return routes.AsReadOnly();
    }

    public async Task<PageIndexViewModel> GetPageIndexAsync(CancellationToken ct = default)
    {
        var pages = await _service.GetAllAsync(ct);
        var activeRoutes = await _routeService.GetActiveRoutesAsync(ct);
        return new PageIndexViewModel { Pages = BuildTree(pages, activeRoutes) };
    }

    public async Task<PageUpsertViewModel?> GetPageUpsertAsync(Guid? id, CancellationToken ct = default)
    {
        if (id == null || id == Guid.Empty)
        {
            return new PageUpsertViewModel();
        }

        var dto = await _service.GetByIdAsync(id.Value, ct);
        if (dto == null)
            return null;

        var vm = _mapper.Map<PageUpsertViewModel>(dto);

        var routes = await _routeService.GetByOwningContentAsync(dto.ContentMeta.MasterId, ct);
        var activeRoute = routes.FirstOrDefault();
        if (activeRoute != null)
        {
            vm.Route = activeRoute.Pattern;
            var defaults = DeserializeDefaults(activeRoute.DefaultsJson);
            if (defaults != null && defaults.TryGetValue("controller", out var controllerName))
                vm.ControllerName = controllerName;
        }

        return vm;
    }

    public async Task<(bool Success, string? ErrorMessage)> SavePageUpsertAsync(PageUpsertViewModel model, CancellationToken ct = default)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));

        var dto = _mapper.Map<PageDTO>(model);

        if (model.Id.HasValue && model.Id != Guid.Empty)
        {
            var ok = await _service.UpdateAsync(dto, ct);
            if (!ok) return (false, "Failed to update page.");
        }
        else
        {
            await _service.CreateAsync(dto, ct);
        }

        // After saving the page DTO, the ContentMeta is populated (MasterId is set).
        // We need to re-read to get the actual MasterId for the CMSRoute.
        var savedDto = await _service.GetByIdAsync(dto.ContentId, ct);
        if (savedDto == null) return (false, "Failed to read saved page.");

        var controllerInfo = _controllerRegistry.GetByName(model.ControllerName);
        var config = controllerInfo?.ConfigurationType != null
            ? TryDeserializeConfig(model.ConfigurationJson, controllerInfo.ConfigurationType)
            : null;

        if (savedDto.ContentMeta.IsPublished)
        {
            await _routeRegistration.RegisterContentRoutesAsync(
                this,
                model.Route,
                model.ControllerName,
                config ?? new { },
                savedDto.ContentMeta.Id,
                savedDto.ContentMeta.MasterId,
                isPublished: true,
                ct: ct);
        }
        else
        {
            await _routeRegistration.UnregisterContentRoutesAsync(
                savedDto.ContentMeta.MasterId, ct);
        }

        return (true, null);
    }

    public async Task<bool> DeletePageAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _service.GetByIdAsync(id, ct);
        if (entity != null)
        {
            await _routeRegistration.UnregisterContentRoutesAsync(entity.ContentMeta.MasterId, ct);
        }
        return await _service.DeleteAsync(id, ct);
    }

    public Task<VersionHistoryViewModel?> GetVersionHistoryAsync(Guid masterId, CancellationToken ct = default)
        => BuildVersionHistoryAsync(masterId, ct: ct);

    public async Task<PageUpsertViewModel?> GetPageUpsertForRestoreAsync(Guid historicalId, CancellationToken ct = default)
    {
        var historical = await _service.GetByIdAsync(historicalId, ct);
        if (historical == null) return null;
        var latest = await _service.GetAllVersionsAsync(historical.ContentMeta.MasterId, ct);
        var latestVersion = latest.FirstOrDefault();
        if (latestVersion == null) return null;
        var vm = _mapper.Map<PageUpsertViewModel>(historical);
        vm.Id = latestVersion.ContentMeta.Id;
        vm.Version = latestVersion.ContentMeta.Version;

        var routes = await _routeService.GetByOwningContentAsync(historical.ContentMeta.MasterId, ct);
        var activeRoute = routes.FirstOrDefault();
        if (activeRoute != null)
        {
            vm.Route = activeRoute.Pattern;
            var defaults = DeserializeDefaults(activeRoute.DefaultsJson);
            if (defaults != null && defaults.TryGetValue("controller", out var controllerName))
                vm.ControllerName = controllerName;
        }

        return vm;
    }

    public Task<bool> DeletePageVersionAsync(Guid id, CancellationToken ct = default)
        => DeleteVersionCoreAsync(id, ct);

    // IAdminCrudHandler members
    public override async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
        => await GetPageIndexAsync(ct);

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
    {
        if (id.HasValue && id != Guid.Empty)
        {
            var existing = await GetPageUpsertAsync(id, ct);
            if (existing == null) return null;
            return existing;
        }

        var vm = new PageUpsertViewModel();
        var parentRoute = query["parentRoute"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(parentRoute))
        {
            parentRoute = parentRoute.TrimEnd('/');
            if (!parentRoute.StartsWith('/'))
                parentRoute = "/" + parentRoute;
            vm.Route = parentRoute == "/" ? "/" : parentRoute + "/";
        }
        return vm;
    }

    public override object CreateEmptyUpsertViewModel() => new PageUpsertViewModel();

    protected override async Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        var vm = (PageUpsertViewModel)model;

        var excludeMasterId = vm.MasterId.HasValue && vm.MasterId != Guid.Empty ? vm.MasterId : null;
        var routeAvailable = await _routeService.IsPatternAvailableAsync(vm.Route, excludeMasterId, ct);
        if (!routeAvailable)
            return new AdminSaveResult(false, "This route is already in use by another page.", "Route");

        var result = await SavePageUpsertAsync(vm, ct);
        return result.Success
            ? new AdminSaveResult(true)
            : new AdminSaveResult(false, result.ErrorMessage);
    }

    public override async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        => await DeletePageAsync(id, ct);

    public override async Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default)
    {
        var vm = await GetPageIndexAsync(ct);
        return vm.Pages
            .Where(n => n.PageId.HasValue)
            .Select(n => (object)new { id = n.PageMasterId!.Value, title = n.Title });
    }

    public override async Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default)
        => await GetPageUpsertForRestoreAsync(historicalId, ct);

    public override Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default)
        => DeletePageVersionAsync(id, ct);

    private static List<PageTreeNode> BuildTree(List<PageDTO> pages, List<CMSRouteDTO> activeRoutes)
    {
        var routeMap = activeRoutes
            .Where(r => r.OwningContentType == "Page" && r.OwningContentMasterId.HasValue)
            .GroupBy(r => r.OwningContentMasterId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var pageRouteMap = new Dictionary<Guid, string>();
        foreach (var page in pages)
        {
            if (routeMap.TryGetValue(page.ContentMeta.MasterId, out var route))
                pageRouteMap[page.ContentMeta.MasterId] = route.Pattern;
        }

        var sortedPages = pages
            .Where(p => pageRouteMap.ContainsKey(p.ContentMeta.MasterId))
            .OrderBy(p => pageRouteMap[p.ContentMeta.MasterId])
            .ToList();

        var roots = new List<PageTreeNode>();
        var nodeMap = new Dictionary<string, PageTreeNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var page in sortedPages)
        {
            if (!pageRouteMap.TryGetValue(page.ContentMeta.MasterId, out var currentRoute))
                continue;

            if (currentRoute == "/")
            {
                if (!nodeMap.TryGetValue("/", out var rootNode))
                {
                    rootNode = new PageTreeNode
                    {
                        Route = "/",
                        Title = page.ContentMeta.Title,
                        PageId = page.ContentMeta.Id,
                        PageMasterId = page.ContentMeta.MasterId,
                        PageVersion = page.ContentMeta.Version,
                        IsPublished = page.ContentMeta.IsPublished,
                        IsHidden = page.ContentMeta.IsHidden
                    };
                    nodeMap["/"] = rootNode;
                    roots.Insert(0, rootNode);
                }
                else
                {
                    rootNode.Title = page.ContentMeta.Title;
                    rootNode.PageId = page.ContentMeta.Id;
                    rootNode.PageMasterId = page.ContentMeta.MasterId;
                    rootNode.PageVersion = page.ContentMeta.Version;
                    rootNode.IsPublished = page.ContentMeta.IsPublished;
                    rootNode.IsHidden = page.ContentMeta.IsHidden;
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
                        Route = currentPath,
                        Title = isLeaf ? page.ContentMeta.Title : segments[i],
                        PageId = isLeaf ? page.ContentMeta.Id : null,
                        PageMasterId = isLeaf ? page.ContentMeta.MasterId : null,
                        PageVersion = isLeaf ? page.ContentMeta.Version : 0,
                        IsPublished = isLeaf && page.ContentMeta.IsPublished,
                        IsHidden = isLeaf && page.ContentMeta.IsHidden
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
                    node.Title = page.ContentMeta.Title;
                    node.PageId = page.ContentMeta.Id;
                    node.PageMasterId = page.ContentMeta.MasterId;
                    node.PageVersion = page.ContentMeta.Version;
                    node.IsPublished = page.ContentMeta.IsPublished;
                    node.IsHidden = page.ContentMeta.IsHidden;
                }
            }
        }

        return roots;
    }

    private static Dictionary<string, string>? DeserializeDefaults(string defaultsJson)
    {
        if (string.IsNullOrWhiteSpace(defaultsJson) || defaultsJson == "{}")
            return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(defaultsJson);
        }
        catch
        {
            return null;
        }
    }

    private static object? TryDeserializeConfig(string json, Type configType)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize(json, configType,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
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
}
