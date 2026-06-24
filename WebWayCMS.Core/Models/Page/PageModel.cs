using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WebWayCMS.Mapping;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.Shared;
using WebWayCMS.Pages;
using WebWayCMS.Services;

namespace WebWayCMS.Models.Page;

public sealed class PageModel : AdminCrudModel<PageDTO>, IPageModel
{
    private readonly IPageService _service;
    private readonly IMapper _mapper;
    private readonly PageRegistryHandler _registryHandler;

    protected override string VersionHistoryContentType => "pages";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/admin/pages";
    protected override Task<List<PageDTO>> GetAllVersionsAsync(Guid masterId, CancellationToken ct) => _service.GetAllVersionsAsync(masterId, ct);
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct) => _service.DeleteVersionAsync(id, ct);

    public override string ContentType => "pages";
    public override string DisplayName => "Page";
    public override IAdminRegistryHandler? RegistryHandler => _registryHandler;

    public PageModel(IPageService service, IMapper mapper, IPageControllerRegistry registry)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _registryHandler = new PageRegistryHandler(
            registry ?? throw new ArgumentNullException(nameof(registry)));
    }

    public async Task<PageDTO?> GetByRouteAsync(string route, CancellationToken ct = default)
    {
        return await _service.GetByRouteAsync(route, ct);
    }

    public async Task<PageIndexViewModel> GetPageIndexAsync(CancellationToken ct = default)
    {
        var pages = await _service.GetAllAsync(ct);
        return new PageIndexViewModel { Pages = BuildTree(pages) };
    }

    public async Task<PageUpsertViewModel?> GetPageUpsertAsync(Guid? id, CancellationToken ct = default)
    {
        if (id == null || id == Guid.Empty)
        {
            return new PageUpsertViewModel();
        }

        var dto = await _service.GetByIdAsync(id.Value, ct);
        if (dto == null)
        {
            return null;
        }

        return _mapper.Map<PageUpsertViewModel>(dto);
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

        return (true, null);
    }

    public async Task<bool> DeletePageAsync(Guid id, CancellationToken ct = default)
    {
        return await _service.DeleteAsync(id, ct);
    }

    public async Task<bool> IsRouteAvailableAsync(string route, Guid? excludeMasterId = null, CancellationToken ct = default)
    {
        return await _service.IsRouteAvailableAsync(route, excludeMasterId, ct);
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

        // Create — optionally pre-fill Route from parentRoute query parameter
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

    public override async Task<AdminSaveResult> SaveUpsertAsync(object model, CancellationToken ct = default)
    {
        var vm = (PageUpsertViewModel)model;

        // Page-specific: route uniqueness validation
        var excludeMasterId = vm.MasterId.HasValue && vm.MasterId != Guid.Empty ? vm.MasterId : null;
        var routeAvailable = await IsRouteAvailableAsync(vm.Route, excludeMasterId, ct);
        if (!routeAvailable)
            return new AdminSaveResult(false, "This route is already in use by another page.", "Route");

        var result = await SavePageUpsertAsync(vm, ct);
        return result.Success
            ? new AdminSaveResult(true)
            : new AdminSaveResult(false, result.ErrorMessage);
    }

    public override Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        => DeletePageAsync(id, ct);

    public override Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default)
        => Task.FromResult(Enumerable.Empty<object>());

    public override async Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default)
        => await GetPageUpsertForRestoreAsync(historicalId, ct);

    public override Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default)
        => DeletePageVersionAsync(id, ct);

    private static List<PageTreeNode> BuildTree(List<PageDTO> pages)
    {
        var roots = new List<PageTreeNode>();
        var nodeMap = new Dictionary<string, PageTreeNode>(StringComparer.OrdinalIgnoreCase);

        var sortedPages = pages.OrderBy(p => p.Route).ToList();

        foreach (var page in sortedPages)
        {
            // Handle root "/" page directly — Trim('/').Split(...) produces no segments
            if (page.Route == "/")
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
                        ControllerName = page.ControllerName,
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
                    rootNode.ControllerName = page.ControllerName;
                    rootNode.IsPublished = page.ContentMeta.IsPublished;
                    rootNode.IsHidden = page.ContentMeta.IsHidden;
                }
                continue;
            }

            var segments = page.Route.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
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
                        ControllerName = isLeaf ? page.ControllerName : string.Empty,
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
                    node.ControllerName = page.ControllerName;
                    node.IsPublished = page.ContentMeta.IsPublished;
                    node.IsHidden = page.ContentMeta.IsHidden;
                }
            }
        }

        return roots;
    }
}

internal sealed class PageRegistryHandler : IAdminRegistryHandler
{
    private readonly IPageControllerRegistry _registry;

    public PageRegistryHandler(IPageControllerRegistry registry)
    {
        _registry = registry;
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

        return new JsonResult(new
        {
            controllerName = controller.Name,
            displayName = controller.DisplayName,
            category = controller.Category,
            properties
        });
    }
}
