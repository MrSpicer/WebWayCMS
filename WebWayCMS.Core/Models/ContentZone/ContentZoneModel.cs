using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.Shared;
using WebWayCMS.Services;

namespace WebWayCMS.Models.ContentZone;

public class ContentZoneModel : AdminCrudModel<ContentZoneDTO>, IContentZoneModel, IAdminCrudHandler
{
    private readonly IContentZoneService _service;
    private readonly IContentStore<ContentZoneDTO> _zoneStore;
    private readonly IContentStore<ContentZoneItemDTO> _itemStore;
    private readonly IWidgetRegistry _registry;
    private readonly ICMSRouteService _routeService;
    private readonly IRouteRegistrationService _routeRegistration;
    private readonly ContentZoneChildHandler _childHandler;
    private readonly ContentZoneRegistryHandler _registryHandler;

    protected override IContentStore<ContentZoneDTO> Store => _zoneStore;

    protected override string VersionHistoryContentType => "contentzones";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/wadmin/contentzones";
    protected override Task<List<ContentZoneDTO>> GetAllVersionsAsync(Guid nodeId, CancellationToken ct)
        => _zoneStore.GetAllVersionsAsync(nodeId, ct);
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct)
        => _zoneStore.DeleteVersionAsync(id, ct);

    public override string ContentType => "contentzones";
    public override string DisplayName => "Content Zone";
    public override string[]? WriteRoles => null;
    public override string IndexViewPath => "~/Views/AdminContentZone/ContentZones.cshtml";
    public override string UpsertViewPath => "~/Views/AdminContentZone/ContentZoneUpsert.cshtml";
    public override IAdminRegistryHandler? RegistryHandler => _registryHandler;
    public override IAdminCrudChildHandler? ChildHandler => _childHandler;

    public ContentZoneModel(
        IContentZoneService service,
        IContentStore<ContentZoneDTO> zoneStore,
        IContentStore<ContentZoneItemDTO> itemStore,
        IWidgetRegistry registry,
        IViewDiscoveryService viewDiscoveryService,
        ICMSRouteService routeService,
        IRouteRegistrationService routeRegistration,
        IChangeSetScope changeSetScope)
        : base(changeSetScope)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _zoneStore = zoneStore ?? throw new ArgumentNullException(nameof(zoneStore));
        _itemStore = itemStore ?? throw new ArgumentNullException(nameof(itemStore));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
        _routeRegistration = routeRegistration ?? throw new ArgumentNullException(nameof(routeRegistration));
        _childHandler = new ContentZoneChildHandler(this);
        _registryHandler = new ContentZoneRegistryHandler(
            registry,
            viewDiscoveryService ?? throw new ArgumentNullException(nameof(viewDiscoveryService)));
    }

    // IContentZoneModel members

    public async Task<ContentZoneViewModel?> GetViewModelAsync(string contentZoneName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(contentZoneName))
            return null;

        var zone = await _service.GetZoneByNameAsync(contentZoneName, ct);
        if (zone == null)
            return new ContentZoneViewModel { Name = contentZoneName };

        return await MapToViewModelAsync(zone, ct);
    }

    public async Task<ContentZoneViewModel> GetOrCreateViewModelAsync(string contentZoneName, CancellationToken ct = default)
    {
        var zone = await _service.GetOrCreateByNameAsync(contentZoneName, ct);
        return await MapToViewModelAsync(zone, ct);
    }

    public async Task<ContentZoneViewModel> GetOrCreateViewModelByPageSlotAsync(Guid pageNodeId, string slotName, CancellationToken ct = default)
    {
        var (zone, _) = await _service.GetOrCreateByPageSlotAsync(pageNodeId, slotName, ct);
        return await MapToViewModelAsync(zone, ct);
    }

    public async Task<ContentZoneViewModel?> GetViewModelByPageSlotAsync(Guid pageNodeId, string slotName, CancellationToken ct = default)
    {
        var assignment = await _service.GetByPageSlotAsync(pageNodeId, slotName, ct);
        if (assignment == null)
            return null;

        var zone = await _service.GetZoneByNodeAsync(assignment.ContentZoneNodeId, ct);
        return zone == null ? null : await MapToViewModelAsync(zone, ct);
    }

    public async Task<ContentZoneViewModel> GetOrCreateViewModelByZoneSlotAsync(Guid parentZoneNodeId, string slotName, CancellationToken ct = default)
    {
        var (zone, _) = await _service.GetOrCreateByZoneSlotAsync(parentZoneNodeId, slotName, ct);
        return await MapToViewModelAsync(zone, ct);
    }

    public async Task<ContentZoneViewModel?> GetViewModelByZoneSlotAsync(Guid parentZoneNodeId, string slotName, CancellationToken ct = default)
    {
        var assignment = await _service.GetByZoneSlotAsync(parentZoneNodeId, slotName, ct);
        if (assignment == null)
            return null;

        var zone = await _service.GetZoneByNodeAsync(assignment.ContentZoneNodeId, ct);
        return zone == null ? null : await MapToViewModelAsync(zone, ct);
    }

    public async Task<ContentZoneViewModel?> GetViewModelByIdAsync(Guid nodeId, CancellationToken ct = default)
    {
        var zone = await _service.GetZoneByNodeAsync(nodeId, ct);
        return zone == null ? null : await MapToViewModelAsync(zone, ct);
    }

    public async Task<ContentZoneDTO?> GetByIdAsync(Guid nodeId, CancellationToken ct = default)
    {
        return await _zoneStore.GetCurrentDraftAsync(nodeId, ct);
    }

    public async Task<ContentZoneItemDTO?> GetItemByNodeIdAsync(Guid itemNodeId, CancellationToken ct = default)
    {
        return await _itemStore.GetCurrentDraftAsync(itemNodeId, ct);
    }

    public async Task<ContentZoneItemDTO?> GetItemVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        return await _itemStore.GetVersionAsync(versionId, ct);
    }

    public async Task<bool> DeleteItemVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        return await _itemStore.DeleteVersionAsync(versionId, ct);
    }

    public override async Task<bool> DeleteAsync(Guid nodeId, CancellationToken ct = default)
    {
        return await _service.DeleteZoneAsync(nodeId, ct);
    }

    public async Task<ContentZoneItemDTO> AddItemAsync(Guid zoneNodeId, ContentZoneItemDTO item, CancellationToken ct = default)
        => await _service.AddItemAsync(zoneNodeId, item, ct);

    public async Task<bool> UpdateItemAsync(ContentZoneItemDTO item, CancellationToken ct = default)
        => await _service.UpdateItemAsync(item, ct);

    public async Task<bool> RemoveItemAsync(Guid itemNodeId, CancellationToken ct = default)
        => await _service.RemoveItemAsync(itemNodeId, ct);

    public async Task<bool> ReorderItemsAsync(Guid zoneNodeId, List<Guid> itemNodeIdsInOrder, CancellationToken ct = default)
        => await _service.ReorderItemsAsync(zoneNodeId, itemNodeIdsInOrder, ct);

    public async Task<List<ContentZoneItemDTO>> GetAllItemVersionsAsync(Guid itemNodeId, CancellationToken ct = default)
        => await _itemStore.GetAllVersionsAsync(itemNodeId, ct);

    public async Task<List<ContentZoneItemDTO>> GetItemsAsync(Guid zoneNodeId, CancellationToken ct = default)
        => await _service.GetItemsAsync(zoneNodeId, ct);

    // IAdminCrudHandler members

    public override async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
    {
        var zones = await _zoneStore.GetAllCurrentDraftsAsync(ct);
        var zoneNodeIds = zones.Select(z => z.Version.Node.Id).ToList();
        var zoneIdsWithChildren = await _service.GetZoneNodeIdsWithChildrenAsync(zoneNodeIds, ct);
        var assignmentCounts = await _service.GetAssignmentCountsByNodeIdAsync(zoneNodeIds, ct);
        return new ContentZoneIndexViewModel
        {
            Zones = zones,
            ZoneIdsWithChildren = zoneIdsWithChildren,
            AssignmentCountsByNodeId = assignmentCounts
        };
    }

    async Task<object> IAdminCrudHandler.GetIndexViewModelAsync(IQueryCollection query, CancellationToken ct)
    {
        List<ContentZoneDTO> zones;
        Guid? filterPageId = null;
        string? filterPageRoute = null;
        Guid? filterParentZoneId = null;
        string? filterParentZoneName = null;

        if (Guid.TryParse(query["pageId"], out var pageId))
        {
            filterPageId = pageId;
            zones = await _service.GetAllByPageAsync(pageId, ct);
            var routes = await _routeService.GetByOwningContentAsync(pageId, ct);
            filterPageRoute = routes.FirstOrDefault()?.Pattern;
        }
        else if (Guid.TryParse(query["zoneId"], out var zoneId))
        {
            filterParentZoneId = zoneId;
            zones = await _service.GetAllByParentZoneAsync(zoneId, ct);
            var parentZone = await _service.GetZoneByNodeAsync(zoneId, ct);
            filterParentZoneName = parentZone?.Name ?? parentZone?.Version.Title;
        }
        else
        {
            zones = await _zoneStore.GetAllCurrentDraftsAsync(ct);
        }

        var zoneNodeIds = zones.Select(z => z.Version.Node.Id).ToList();
        var zoneIdsWithChildren = await _service.GetZoneNodeIdsWithChildrenAsync(zoneNodeIds, ct);
        var assignmentCounts = await _service.GetAssignmentCountsByNodeIdAsync(zoneNodeIds, ct);

        return new ContentZoneIndexViewModel
        {
            Zones = zones,
            FilterPageId = filterPageId,
            FilterPageRoute = filterPageRoute,
            FilterParentZoneId = filterParentZoneId,
            FilterParentZoneName = filterParentZoneName,
            ZoneIdsWithChildren = zoneIdsWithChildren,
            AssignmentCountsByNodeId = assignmentCounts
        };
    }

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
    {
        if (id == null) return new ContentZoneUpsertViewModel();
        var zone = await _zoneStore.GetCurrentDraftAsync(id.Value, ct);
        if (zone == null) return null;
        return new ContentZoneUpsertViewModel
        {
            NodeId = zone.Version.Node.Id,
            ExpectedVersionNumber = zone.Version.VersionNumber,
            Title = zone.Version.Title,
            Slug = zone.Version.Slug,
            Name = zone.Name,
            Description = zone.Description,
            IsPublished = zone.Version.State == ContentVersionState.Published,
        };
    }

    public override object CreateEmptyUpsertViewModel() => new ContentZoneUpsertViewModel();

    protected override async Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        var vm = (ContentZoneUpsertViewModel)model;
        var isEdit = vm.NodeId.HasValue && vm.NodeId != Guid.Empty;

        ContentZoneDTO zone;
        if (isEdit)
        {
            var existing = await _zoneStore.GetCurrentDraftAsync(vm.NodeId!.Value, ct);
            if (existing == null)
                return new AdminSaveResult(false, "Content zone not found.");

            zone = existing with
            {
                Version = existing.Version with
                {
                    Title = vm.Title,
                    Slug = vm.Slug ?? string.Empty,
                },
                Name = vm.Name,
                Description = vm.Description,
            };
        }
        else
        {
            zone = new ContentZoneDTO
            {
                Version = new ContentVersion
                {
                    Title = vm.Title,
                    Slug = vm.Slug ?? string.Empty,
                },
                Name = vm.Name,
                Description = vm.Description,
            };
        }

        var result = await _zoneStore.SaveDraftAsync(zone, vm.ExpectedVersionNumber, ct);
        return result.Success ? new AdminSaveResult(true, NodeId: result.NodeId) : new AdminSaveResult(false, result.ErrorMessage ?? "Update failed.");
    }

    public override async Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default)
    {
        var zones = await _zoneStore.GetAllCurrentDraftsAsync(ct);
        return zones.Select(z => (object)new { id = z.Version.Node.Id, title = !string.IsNullOrEmpty(z.Version.Title) ? z.Version.Title : z.Name });
    }

    public override async Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default)
    {
        var historical = await _zoneStore.GetVersionAsync(historicalId, ct);
        if (historical == null) return null;
        return new ContentZoneUpsertViewModel
        {
            NodeId = historical.Version.Node.Id,
            ExpectedVersionNumber = historical.Version.VersionNumber,
            Title = historical.Version.Title,
            Slug = historical.Version.Slug,
            Name = historical.Name,
            Description = historical.Description,
            IsPublished = historical.Version.State == ContentVersionState.Published,
        };
    }

    internal async Task RegisterWidgetRouteIfRoutableAsync(
        string componentName, Guid itemNodeId, Guid zoneNodeId, bool isActive, CancellationToken ct)
    {
        var pageNodeId = await _service.GetParentPageNodeForZoneAsync(zoneNodeId, ct);
        await _routeRegistration.TryRegisterWidgetRoutesAsync(
            componentName, itemNodeId, pageNodeId, isActive, ct);
    }

    private async Task<ContentZoneViewModel> MapToViewModelAsync(ContentZoneDTO zone, CancellationToken ct)
    {
        var items = await _service.GetItemsAsync(zone.Version.Node.Id, ct);
        var vm = new ContentZoneViewModel
        {
            Id = zone.Version.Node.Id,
            Name = zone.Name,
            ZoneObjects = items
                .Select(i => new ContentZoneObject
                {
                    Id = i.Version.Node.Id,
                    Ordinal = i.Ordinal,
                    ZoneId = i.ContentZoneNodeId,
                    ComponentName = i.ComponentName,
                    ComponentProperties = DeserializePropertiesToConfigType(i.ComponentName, i.ComponentPropertiesJson)
                })
                .ToList()
        };
        return vm;
    }

    /// <summary>
    /// Deserializes properties JSON into the actual configuration type for the component.
    /// Falls back to a dictionary if the component type is not registered.
    /// </summary>
    private object DeserializePropertiesToConfigType(string componentName, string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
        {
            var defaultConfig = _registry.CreateDefaultConfiguration(componentName);
            if (defaultConfig != null)
                return defaultConfig;
            return new { };
        }

        try
        {
            var componentInfo = _registry.GetByName(componentName);
            if (!string.IsNullOrEmpty(componentInfo?.ConfigurationTypeName))
            {
                var configType = ResolveType(componentInfo.ConfigurationTypeName);
                if (configType != null)
                {
                    var config = JsonSerializer.Deserialize(json, configType, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (config != null)
                        return config;
                }
            }

            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            return dict ?? new Dictionary<string, object>();
        }
        catch
        {
            return new { };
        }
    }

    private static Type? ResolveType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        var type = Type.GetType(typeName, throwOnError: false);
        if (type != null)
            return type;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = asm.GetType(typeName, throwOnError: false);
            if (type != null)
                return type;
        }

        return null;
    }
}

/// <summary>Manages items within a content zone (child entities).</summary>
internal sealed class ContentZoneChildHandler : IAdminCrudChildHandler
{
    private readonly ContentZoneModel _model;

    public ContentZoneChildHandler(ContentZoneModel model)
    {
        _model = model;
    }

    public string ChildType => "items";
    public string ChildDisplayName => "Content Zone Item";
    public string[]? WriteRoles => null;

    public string ChildIndexViewPath => "~/Views/AdminContentZone/ContentZoneItems.cshtml";
    public string ChildUpsertViewPath => "~/Views/AdminContentZone/ContentZoneItemUpsert.cshtml";

    public async Task<object?> GetChildIndexViewModelAsync(string parentKey, CancellationToken ct = default)
    {
        if (!Guid.TryParse(parentKey, out var zoneNodeId)) return null;
        var zone = await _model.GetByIdAsync(zoneNodeId, ct);
        if (zone == null) return null;
        var items = await _model.GetItemsAsync(zoneNodeId, ct);
        return new ContentZoneItemsIndexViewModel { Zone = zone, Items = items };
    }

    public async Task<object?> GetChildUpsertViewModelAsync(string parentKey, Guid? id, CancellationToken ct = default)
    {
        if (id == null || id == Guid.Empty) return null;
        var item = await _model.GetItemByNodeIdAsync(id.Value, ct);
        if (item == null) return null;
        return new ContentZoneItemUpsertViewModel
        {
            NodeId = item.Version.Node.Id,
            ContentZoneNodeId = item.ContentZoneNodeId,
            ExpectedVersionNumber = item.Version.VersionNumber,
            ComponentName = item.ComponentName,
            ComponentPropertiesJson = item.ComponentPropertiesJson,
            IsActive = item.IsActive,
        };
    }

    public async Task SetChildUpsertViewDataAsync(ViewDataDictionary viewData, string parentKey, CancellationToken ct = default)
    {
        if (!Guid.TryParse(parentKey, out var zoneNodeId)) return;
        var zone = await _model.GetByIdAsync(zoneNodeId, ct);
        viewData["ZoneName"] = zone?.Name ?? zone?.Version.Title ?? parentKey;
        viewData["ZoneId"] = parentKey;
    }

    public object CreateEmptyChildUpsertViewModel() => new ContentZoneItemUpsertViewModel();

    public async Task<AdminSaveResult> SaveChildUpsertAsync(string parentKey, object model, CancellationToken ct = default)
    {
        var vm = (ContentZoneItemUpsertViewModel)model;
        if (vm.NodeId == null || vm.NodeId == Guid.Empty)
        {
            if (!Guid.TryParse(parentKey, out var zoneNodeId))
                return new AdminSaveResult(false, "A valid content zone id is required.");

            var newItem = new ContentZoneItemDTO
            {
                ContentZoneNodeId = zoneNodeId,
                ComponentName = vm.ComponentName,
                ComponentPropertiesJson = string.IsNullOrWhiteSpace(vm.ComponentPropertiesJson) ? "{}" : vm.ComponentPropertiesJson,
                IsActive = vm.IsActive,
            };
            var createdItem = await _model.AddItemAsync(zoneNodeId, newItem, ct);
            await _model.RegisterWidgetRouteIfRoutableAsync(
                createdItem.ComponentName, createdItem.Version.Node!.Id, zoneNodeId,
                createdItem.IsActive, ct);
            return new AdminSaveResult(true);
        }

        var existing = await _model.GetItemByNodeIdAsync(vm.NodeId.Value, ct);
        if (existing == null)
            return new AdminSaveResult(false, "Content zone item not found.");

        var updated = existing with
        {
            ComponentName = vm.ComponentName,
            ComponentPropertiesJson = vm.ComponentPropertiesJson,
            IsActive = vm.IsActive,
        };
        var ok = await _model.UpdateItemAsync(updated, ct);
        if (ok)
        {
            await _model.RegisterWidgetRouteIfRoutableAsync(
                updated.ComponentName, existing.Version.Node.Id, existing.ContentZoneNodeId,
                updated.IsActive, ct);
        }
        return ok ? new AdminSaveResult(true) : new AdminSaveResult(false, "Update failed.");
    }

    public async Task<bool> DeleteChildAsync(Guid id, CancellationToken ct = default)
    {
        var item = await _model.GetItemByNodeIdAsync(id, ct);
        if (item != null)
        {
            await _model.RegisterWidgetRouteIfRoutableAsync(
                item.ComponentName, item.Version.Node.Id, item.ContentZoneNodeId,
                false, ct);
        }
        return await _model.RemoveItemAsync(id, ct);
    }

    public bool SupportsReorder => false;

    public Task<bool> ReorderAsync(string parentKey, List<Guid> orderedIds, CancellationToken ct = default)
        => Task.FromResult(false);

    public bool SupportsVersionHistory => true;

    public async Task<VersionHistoryViewModel?> GetChildVersionHistoryViewModelAsync(string parentKey, Guid nodeId, CancellationToken ct = default)
    {
        var versions = await _model.GetAllItemVersionsAsync(nodeId, ct);
        if (!versions.Any()) return null;
        var maxVersion = versions.Max(v => v.Version.VersionNumber);
        return new VersionHistoryViewModel
        {
            ContentType = "contentzones",
            NodeId = nodeId,
            ItemTitle = versions.First().Version.Title ?? versions.First().ComponentName,
            BackUrl = "/wadmin/contentzones",
            ParentKey = parentKey,
            ChildType = "items",
            Versions = versions.Select(v => new VersionItemViewModel
            {
                Id = v.VersionId,
                Version = v.Version.VersionNumber,
                Title = v.ComponentName,
                CreationDate = v.Version.Node.CreatedUtc,
                ModificationDate = v.Version.CreatedUtc,
                IsPublished = v.Version.State == ContentVersionState.Published,
                IsDeleted = v.Version.Node.IsDeleted,
                IsLatest = v.Version.VersionNumber == maxVersion,
                CreatedBy = v.Version.CreatedBy,
                ChangeNote = v.Version.ChangeNote,
                State = v.Version.State,
                ChangeSetId = v.Version.ChangeSetId
            }).ToList()
        };
    }

    public async Task<object?> GetChildRestoreVersionViewModelAsync(string parentKey, Guid historicalId, CancellationToken ct = default)
    {
        var historical = await _model.GetItemVersionAsync(historicalId, ct);
        if (historical == null) return null;
        return new ContentZoneItemUpsertViewModel
        {
            NodeId = historical.Version.Node.Id,
            ContentZoneNodeId = historical.ContentZoneNodeId,
            ExpectedVersionNumber = historical.Version.VersionNumber,
            ComponentName = historical.ComponentName,
            ComponentPropertiesJson = historical.ComponentPropertiesJson,
            IsActive = historical.IsActive,
        };
    }

    public async Task<bool> DeleteChildVersionAsync(Guid id, CancellationToken ct = default)
        => await _model.DeleteItemVersionAsync(id, ct);
}

/// <summary>Exposes the content zone component registry as admin JSON endpoints.</summary>
internal sealed class ContentZoneRegistryHandler : IAdminRegistryHandler
{
    private readonly IWidgetRegistry _registry;
    private readonly IViewDiscoveryService _viewDiscoveryService;
    private readonly Serilog.ILogger _logger =
        Serilog.Log.ForContext<ContentZoneRegistryHandler>();

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static Type? ResolveConfigurationType(string? configurationTypeName)
    {
        if (string.IsNullOrWhiteSpace(configurationTypeName))
            return null;

        try
        {
            var type = Type.GetType(configurationTypeName, throwOnError: false);
            if (type != null)
                return type;

            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(configurationTypeName, throwOnError: false);
                if (type != null)
                    return type;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public ContentZoneRegistryHandler(
        IWidgetRegistry registry,
        IViewDiscoveryService viewDiscoveryService)
    {
        _registry = registry;
        _viewDiscoveryService = viewDiscoveryService;
    }

    public IActionResult GetAll()
    {
        var components = _registry.GetAllComponents().Select(c => new
        {
            name = c.Name,
            displayName = c.DisplayName,
            description = c.Description,
            category = c.Category
        }).ToList();

        return new JsonResult(components);
    }

    public IActionResult GetProperties(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new BadRequestObjectResult(new { error = "Component name is required." });

        var component = _registry.GetByName(name);
        if (component == null)
            return new NotFoundObjectResult(new { error = $"Component '{name}' not found." });

        var properties = component.Properties.Select(p =>
        {
            Dictionary<string, string> dropdownOptions = p.DropdownOptions;

            if (p.EditorType == EditorType.ViewPicker && !string.IsNullOrWhiteSpace(p.ViewComponentName))
            {
                var views = _viewDiscoveryService.GetAvailableViews(p.ViewComponentName);
                if (views.Any())
                    dropdownOptions = views.ToDictionary(v => v, v => v);
                else
                {
                    _logger.Warning("No views found for ViewComponent '{ComponentName}'", p.ViewComponentName);
                    dropdownOptions = new Dictionary<string, string>();
                }
            }

            return new
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
                dropdownOptions,
                viewComponentName = p.ViewComponentName,
                min = p.Min,
                max = p.Max,
                maxLength = p.MaxLength
            };
        }).OrderBy(p => p.order).ToList();

        return new JsonResult(new
        {
            componentName = component.Name,
            displayName = component.DisplayName,
            category = component.Category,
            properties
        });
    }

    public IActionResult GetForm(string name, string? valuesJson)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new BadRequestObjectResult(new { error = "Component name is required." });

        var component = _registry.GetByName(name);
        if (component == null)
            return new NotFoundObjectResult(new { error = $"Component '{name}' not found." });

        object? instance = null;
        if (!string.IsNullOrWhiteSpace(component.ConfigurationTypeName))
        {
            var configType = ResolveConfigurationType(component.ConfigurationTypeName);
            if (configType != null)
                instance = WebWayCMS.Forms.DynamicConfigurationForm.Materialize(configType, valuesJson);
        }

        return WebWayCMS.Forms.DynamicConfigurationForm.Render(instance);
    }
}
