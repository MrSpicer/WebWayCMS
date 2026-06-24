using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
    private readonly IPageService _pageService;
    private readonly IContentZoneComponentRegistry _registry;
    private readonly ContentZoneChildHandler _childHandler;
    private readonly ContentZoneRegistryHandler _registryHandler;

    public ContentZoneModel(
        IContentZoneService service,
        IPageService pageService,
        IContentZoneComponentRegistry registry)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _pageService = pageService ?? throw new ArgumentNullException(nameof(pageService));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _childHandler = new ContentZoneChildHandler(this);
        _registryHandler = new ContentZoneRegistryHandler(registry);
    }

    // IContentZoneModel members

    public async Task<ContentZoneViewModel?> GetViewModelAsync(string contentZoneName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(contentZoneName))
            return null;

        var zone = await _service.GetByNameAsync(contentZoneName, ct);
        if (zone == null)
            return new ContentZoneViewModel { Name = contentZoneName };

        return MapToViewModel(zone);
    }

    public async Task<ContentZoneViewModel> GetOrCreateViewModelAsync(string contentZoneName, CancellationToken ct = default)
    {
        var zone = await _service.GetOrCreateByNameAsync(contentZoneName, ct);
        return MapToViewModel(zone);
    }

    public async Task<ContentZoneViewModel> GetOrCreateViewModelByPageSlotAsync(Guid pageMasterId, string slotName, CancellationToken ct = default)
    {
        var (zone, _) = await _service.GetOrCreateByPageSlotAsync(pageMasterId, slotName, ct);
        return MapToViewModel(zone);
    }

    public async Task<ContentZoneViewModel?> GetViewModelByPageSlotAsync(Guid pageMasterId, string slotName, CancellationToken ct = default)
    {
        var assignment = await _service.GetByPageSlotAsync(pageMasterId, slotName, ct);
        if (assignment == null)
            return null;

        var zone = await _service.GetByIdAsync(assignment.ContentZoneId, ct);
        return zone == null ? null : MapToViewModel(zone);
    }

    public async Task<ContentZoneViewModel> GetOrCreateViewModelByZoneSlotAsync(Guid parentZoneId, string slotName, CancellationToken ct = default)
    {
        var (zone, _) = await _service.GetOrCreateByZoneSlotAsync(parentZoneId, slotName, ct);
        return MapToViewModel(zone);
    }

    public async Task<ContentZoneViewModel?> GetViewModelByZoneSlotAsync(Guid parentZoneId, string slotName, CancellationToken ct = default)
    {
        var assignment = await _service.GetByZoneSlotAsync(parentZoneId, slotName, ct);
        if (assignment == null)
            return null;

        var zone = await _service.GetByIdAsync(assignment.ContentZoneId, ct);
        return zone == null ? null : MapToViewModel(zone);
    }

    public async Task<ContentZoneViewModel?> GetViewModelByIdAsync(Guid id, CancellationToken ct = default)
    {
        var zone = await _service.GetByIdAsync(id, ct);
        return zone == null ? null : MapToViewModel(zone);
    }

    public async Task<ContentZoneDTO?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _service.GetByIdAsync(id, ct);
    }

    public async Task<List<ContentZoneDTO>> GetAllAsync(CancellationToken ct = default)
    {
        return await _service.GetAllAsync(ct);
    }

    public async Task<ContentZoneDTO> CreateAsync(ContentZoneDTO zone, CancellationToken ct = default)
    {
        return await _service.CreateAsync(zone, ct);
    }

    public async Task<bool> UpdateAsync(ContentZoneDTO zone, CancellationToken ct = default)
    {
        return await _service.UpdateAsync(zone, ct);
    }

    public override async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        return await _service.DeleteAsync(id, ct);
    }

    public async Task<ContentZoneItemDTO> AddItemAsync(Guid zoneId, ContentZoneItemDTO item, CancellationToken ct = default)
    {
        return await _service.AddItemAsync(zoneId, item, ct);
    }

    public async Task<bool> UpdateItemAsync(ContentZoneItemDTO item, CancellationToken ct = default)
    {
        return await _service.UpdateItemAsync(item, ct);
    }

    public async Task<bool> RemoveItemAsync(Guid itemId, CancellationToken ct = default)
    {
        return await _service.RemoveItemAsync(itemId, ct);
    }

    public async Task<ContentZoneItemDTO?> GetItemByIdAsync(Guid itemId, CancellationToken ct = default)
    {
        return await _service.GetItemByIdAsync(itemId, ct);
    }

    public async Task<bool> ReorderItemsAsync(Guid zoneId, List<Guid> itemIdsInOrder, CancellationToken ct = default)
    {
        return await _service.ReorderItemsAsync(zoneId, itemIdsInOrder, ct);
    }

    // Explicit interface implementation to avoid conflict with VersionedModel protected abstract
    async Task<List<ContentZoneDTO>> IContentZoneModel.GetAllVersionsAsync(Guid masterId, CancellationToken ct)
    {
        return await _service.GetAllVersionsAsync(masterId, ct);
    }

    public async Task<List<ContentZoneItemDTO>> GetAllItemVersionsAsync(Guid itemMasterId, CancellationToken ct = default)
    {
        return await _service.GetAllItemVersionsAsync(itemMasterId, ct);
    }

    // IAdminCrudHandler members

    public override string ContentType => "contentzones";
    public override string DisplayName => "Content Zone";
    public override string[]? WriteRoles => null;

    public override async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
    {
        var zones = await _service.GetAllAsync(ct);
        var zoneIdsWithChildren = await _service.GetZoneIdsWithChildrenAsync(zones.Select(z => z.ContentId), ct);
        var assignmentCounts = await _service.GetAssignmentCountsByMasterIdAsync(zones.Select(z => z.ContentMeta.MasterId), ct);
        return new ContentZoneIndexViewModel
        {
            Zones = zones,
            ZoneIdsWithChildren = zoneIdsWithChildren,
            AssignmentCountsByMasterId = assignmentCounts
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
            var pages = await _pageService.GetAllVersionsAsync(pageId, ct);
            filterPageRoute = pages.FirstOrDefault()?.Route;
        }
        else if (Guid.TryParse(query["zoneId"], out var zoneId))
        {
            filterParentZoneId = zoneId;
            zones = await _service.GetAllByParentZoneAsync(zoneId, ct);
            var parentZone = await _service.GetByIdAsync(zoneId, ct);
            filterParentZoneName = parentZone?.Name ?? parentZone?.ContentMeta.Title;
        }
        else
        {
            zones = await _service.GetAllAsync(ct);
        }

        var zoneIdsWithChildren = await _service.GetZoneIdsWithChildrenAsync(zones.Select(z => z.ContentId), ct);
        var assignmentCounts = await _service.GetAssignmentCountsByMasterIdAsync(zones.Select(z => z.ContentMeta.MasterId), ct);

        return new ContentZoneIndexViewModel
        {
            Zones = zones,
            FilterPageId = filterPageId,
            FilterPageRoute = filterPageRoute,
            FilterParentZoneId = filterParentZoneId,
            FilterParentZoneName = filterParentZoneName,
            ZoneIdsWithChildren = zoneIdsWithChildren,
            AssignmentCountsByMasterId = assignmentCounts
        };
    }

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
    {
        if (id == null) return new ContentZoneUpsertViewModel();
        var zone = await _service.GetByIdAsync(id.Value, ct);
        if (zone == null) return null;
        return new ContentZoneUpsertViewModel
        {
            Id = zone.ContentId,
            MasterId = zone.ContentMeta.MasterId,
            Version = zone.ContentMeta.Version,
            Title = zone.ContentMeta.Title,
            Slug = zone.ContentMeta.Slug,
            IsPublished = zone.ContentMeta.IsPublished,
            Name = zone.Name,
            Description = zone.Description,
        };
    }

    public override object CreateEmptyUpsertViewModel() => new ContentZoneUpsertViewModel();

    public override async Task<AdminSaveResult> SaveUpsertAsync(object model, CancellationToken ct = default)
    {
        var vm = (ContentZoneUpsertViewModel)model;
        var isEdit = vm.Id.HasValue && vm.Id != Guid.Empty;

        if (isEdit)
        {
            var existing = await _service.GetByIdAsync(vm.Id!.Value, ct);
            if (existing == null)
                return new AdminSaveResult(false, "Content zone not found.");

            var updated = existing with
            {
                ContentMeta = existing.ContentMeta with
                {
                    Title = vm.Title,
                    Slug = vm.Slug ?? string.Empty,
                    IsPublished = vm.IsPublished,
                },
                Name = vm.Name,
                Description = vm.Description,
            };
            var ok = await _service.UpdateAsync(updated, ct);
            return ok ? new AdminSaveResult(true) : new AdminSaveResult(false, "Update failed.");
        }
        else
        {
            var zone = new ContentZoneDTO
            {
                ContentMeta = new ContentDTO
                {
                    Title = vm.Title,
                    Slug = vm.Slug ?? string.Empty,
                    IsPublished = vm.IsPublished,
                },
                Name = vm.Name,
                Description = vm.Description,
            };
            await _service.CreateAsync(zone, ct);
            return new AdminSaveResult(true);
        }
    }

    public override async Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default)
    {
        var zones = await _service.GetAllAsync(ct);
        return zones.Select(z => (object)new { id = z.ContentId, title = !string.IsNullOrEmpty(z.ContentMeta.Title) ? z.ContentMeta.Title : z.Name });
    }

    public override bool HasSecondaryApiList => false;

    public override Task<IEnumerable<object>> GetSecondaryApiListAsync(string key, CancellationToken ct = default)
        => Task.FromResult(Enumerable.Empty<object>());

    public override IAdminRegistryHandler? RegistryHandler => _registryHandler;
    public override IAdminCrudChildHandler? ChildHandler => _childHandler;

    // VersionedModel abstract implementations

    protected override string VersionHistoryContentType => "contentzones";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/admin/contentzones";
    protected override Task<List<ContentZoneDTO>> GetAllVersionsAsync(Guid masterId, CancellationToken ct)
        => _service.GetAllVersionsAsync(masterId, ct);
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct)
        => _service.DeleteAsync(id, ct);

    private ContentZoneViewModel MapToViewModel(ContentZoneDTO zone)
    {
        var vm = new ContentZoneViewModel
        {
            Id = zone.ContentId,
            Name = zone.Name,
            ZoneObjects = zone.Items
                .OrderBy(i => i.Ordinal)
                .Select(i => new ContentZoneObject
                {
                    Id = i.ContentId,
                    Ordinal = i.Ordinal,
                    ZoneId = i.ContentZoneId,
                    ComponentName = i.ComponentName,
                    ViewName = i.ViewName,
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
            // Try to create a default configuration instance
            var defaultConfig = _registry.CreateDefaultConfiguration(componentName);
            if (defaultConfig != null)
                return defaultConfig;
            return new { };
        }

        try
        {
            // Get the component info to find the configuration type
            var componentInfo = _registry.GetByName(componentName);
            if (componentInfo?.ConfigurationType != null)
            {
                // Deserialize to the actual configuration type
                var config = JsonSerializer.Deserialize(json, componentInfo.ConfigurationType, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (config != null)
                    return config;
            }

            // Fallback: deserialize to dictionary
            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            return dict ?? new Dictionary<string, object>();
        }
        catch
        {
            return new { };
        }
    }
}

/// <summary>Manages items within a content zone (child entities).</summary>
internal sealed class ContentZoneChildHandler : IAdminCrudChildHandler
{
    private readonly IContentZoneModel _model;

    public ContentZoneChildHandler(IContentZoneModel model)
    {
        _model = model;
    }

    public string ChildType => "items";
    public string ChildDisplayName => "Content Zone Item";
    public string[]? WriteRoles => null;

    public async Task<object?> GetChildIndexViewModelAsync(string parentKey, CancellationToken ct = default)
    {
        if (!Guid.TryParse(parentKey, out var zoneId)) return null;
        return await _model.GetByIdAsync(zoneId, ct);
    }

    public async Task<object?> GetChildUpsertViewModelAsync(string parentKey, Guid? id, CancellationToken ct = default)
    {
        if (id == null || id == Guid.Empty) return null;
        var item = await _model.GetItemByIdAsync(id.Value, ct);
        if (item == null) return null;
        return new ContentZoneItemUpsertViewModel
        {
            Id = item.ContentId,
            ContentZoneId = item.ContentZoneId,
            MasterId = item.ContentMeta.MasterId,
            Version = item.ContentMeta.Version,
            ComponentName = item.ComponentName,
            ComponentPropertiesJson = item.ComponentPropertiesJson,
            IsActive = item.IsActive,
        };
    }

    public object CreateEmptyChildUpsertViewModel() => new ContentZoneItemUpsertViewModel();

    public async Task<AdminSaveResult> SaveChildUpsertAsync(string parentKey, object model, CancellationToken ct = default)
    {
        var vm = (ContentZoneItemUpsertViewModel)model;
        if (vm.Id == null || vm.Id == Guid.Empty)
            return new AdminSaveResult(false, "Item ID is required for editing.");

        var existing = await _model.GetItemByIdAsync(vm.Id.Value, ct);
        if (existing == null)
            return new AdminSaveResult(false, "Content zone item not found.");

        var updated = existing with
        {
            ComponentName = vm.ComponentName,
            ComponentPropertiesJson = vm.ComponentPropertiesJson,
            IsActive = vm.IsActive,
        };
        var ok = await _model.UpdateItemAsync(updated, ct);
        return ok ? new AdminSaveResult(true) : new AdminSaveResult(false, "Update failed.");
    }

    public Task<bool> DeleteChildAsync(Guid id, CancellationToken ct = default)
        => _model.RemoveItemAsync(id, ct);

    public bool SupportsReorder => false;

    public Task<bool> ReorderAsync(string parentKey, List<Guid> orderedIds, CancellationToken ct = default)
        => Task.FromResult(false);
}

/// <summary>Exposes the content zone component registry as admin JSON endpoints.</summary>
internal sealed class ContentZoneRegistryHandler : IAdminRegistryHandler
{
    private readonly IContentZoneComponentRegistry _registry;

    public ContentZoneRegistryHandler(IContentZoneComponentRegistry registry)
    {
        _registry = registry;
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

        var properties = component.Properties.Select(p => new
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
            componentName = component.Name,
            displayName = component.DisplayName,
            category = component.Category,
            properties
        });
    }
}
