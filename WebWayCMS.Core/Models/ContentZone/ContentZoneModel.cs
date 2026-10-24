using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.PageHistory;
using WebWayCMS.Models.Shared;
using WebWayCMS.Security;
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
    private readonly IWidgetSlotDiscovery _slotDiscovery;
    private readonly Serilog.ILogger _logger = Serilog.Log.ForContext<ContentZoneModel>();

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
        IPageHistoryService pageHistory,
        IWidgetSlotDiscovery slotDiscovery,
        IChangeSetScope changeSetScope)
        : base(changeSetScope)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _slotDiscovery = slotDiscovery ?? throw new ArgumentNullException(nameof(slotDiscovery));
        PageHistory = pageHistory ?? throw new ArgumentNullException(nameof(pageHistory));
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

    /// <summary>The registration for a widget, or null when it is not registered.</summary>
    internal WidgetRegistrationInfo? GetWidget(string componentName) => _registry.GetByName(componentName);

    /// <summary>Page-set history, consulted before an item version may be deleted.</summary>
    internal IPageHistoryService PageHistory { get; }

    /// <summary>
    /// <see cref="IContentZoneService.RestoreItemVersionAsync"/>; a restore that changes a container's view
    /// moves its nested content into the restored view's zones (see <see cref="PlanSlotRemapAsync"/>).
    /// </summary>
    public async Task<(bool Success, string? ErrorMessage)> RestoreItemVersionAsync(Guid versionId, string? metadataJson, int? expectedVersionNumber, CancellationToken ct = default)
    {
        var historical = await _itemStore.GetVersionAsync(versionId, ct);
        var current = historical == null ? null : await _itemStore.GetCurrentDraftAsync(historical.Version.Node.Id, ct);
        if (historical != null && current != null)
        {
            // The same document the service will store, so the remap plan is planned against what is saved.
            var restoredMetadata = metadataJson ?? historical.MetadataJson ?? "{}";
            var restored = current with
            {
                ComponentName = historical.ComponentName,
                ComponentPropertiesJson = historical.ComponentPropertiesJson,
                MetadataJson = restoredMetadata,
                IsActive = historical.IsActive
            };
            var remap = await PlanSlotRemapAsync(current, restored, ct);
            if (remap != null)
                return await _service.RestoreItemVersionAsync(versionId, restoredMetadata, expectedVersionNumber, remap, ct);
        }

        return await _service.RestoreItemVersionAsync(versionId, metadataJson, expectedVersionNumber, ct);
    }

    /// <summary>
    /// When a save changes a container's view, the moves that carry its nested content from the old view's
    /// zones into the new view's (<see cref="SlotRemapPlanner"/>), discovered by rendering both views. Null
    /// when nothing needs to move: the item owns no zone holding widgets (every ordinary widget, without a
    /// render), the item is being removed (its content stays with it, hidden), or either view's slots could
    /// not be discovered — the save then goes ahead unchanged, and stranded content shows in the page zone
    /// editor's Unplaced panel.
    /// </summary>
    internal async Task<SlotRemapPlan?> PlanSlotRemapAsync(ContentZoneItemDTO before, ContentZoneItemDTO after, CancellationToken ct)
    {
        if (!after.IsActive
            || (before.ComponentName == after.ComponentName
                && before.ComponentPropertiesJson == after.ComponentPropertiesJson
                && before.MetadataJson == after.MetadataJson))
            return null;

        var itemNodeId = before.Version.Node.Id;
        var slotsWithContent = await _service.GetOwnedSlotsWithItemsAsync(itemNodeId, ct);
        if (slotsWithContent.Count == 0)
            return null;

        var oldSlots = await _slotDiscovery.DiscoverSlotsAsync(itemNodeId, before.ComponentName, BuildComponentProperties(before), ct);
        var newSlots = await _slotDiscovery.DiscoverSlotsAsync(itemNodeId, after.ComponentName, BuildComponentProperties(after), ct);
        if (oldSlots == null || newSlots == null)
        {
            _logger.Warning("Could not discover the zones of {Component} item {ItemNodeId}; its content was not moved.",
                after.ComponentName, itemNodeId);
            return null;
        }

        var moves = SlotRemapPlanner.Plan(oldSlots, newSlots, slotsWithContent);
        if (moves.Count == 0)
            return null;

        var label = ItemLabel(after);
        var summary = string.Join(", ", moves.Select(m => $"{m.FromSlot} → {m.ToSlot}"));
        return new SlotRemapPlan(moves, $"{label}: layout changed; content moved ({summary})");
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

    public async Task<ContentZoneViewModel> GetOrCreateViewModelByItemSlotAsync(Guid parentItemNodeId, string slotName, CancellationToken ct = default)
    {
        var (zone, _) = await _service.GetOrCreateByItemSlotAsync(parentItemNodeId, slotName, ct);
        return await MapToViewModelAsync(zone, ct);
    }

    public async Task<ContentZoneViewModel?> GetViewModelByItemSlotAsync(Guid parentItemNodeId, string slotName, CancellationToken ct = default)
    {
        var assignment = await _service.GetByItemSlotAsync(parentItemNodeId, slotName, ct);
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

    public async Task<IReadOnlyList<PageZoneSlot>> GetPageZoneSlotsAsync(Guid pageNodeId, CancellationToken ct = default)
    {
        var assignments = await _service.GetAllAssignmentsForPageAsync(pageNodeId, ct);
        return assignments
            .OrderBy(a => a.SlotName, StringComparer.OrdinalIgnoreCase)
            .Select(a => new PageZoneSlot(a.SlotName, a.ContentZoneNodeId))
            .ToList();
    }

    public Task<Guid?> GetParentPageNodeForZoneAsync(Guid zoneNodeId, CancellationToken ct = default)
        => _service.GetParentPageNodeForZoneAsync(zoneNodeId, ct);

    public async Task<IReadOnlyList<UnplacedZoneCandidate>> GetUnplacedCandidatesAsync(Guid pageNodeId, CancellationToken ct = default)
    {
        var zones = await _service.GetItemOwnedZonesForPageAsync(pageNodeId, ct);
        return zones
            .Select(z => new UnplacedZoneCandidate(z.ZoneNodeId, z.Owner.Version.Node.Id, $"{ItemLabel(z.Owner)} › {z.SlotName}"))
            .ToList();
    }

    /// <summary>A widget's admin label, else its component name.</summary>
    internal static string ItemLabel(ContentZoneItemDTO item)
        => WidgetMetadataJson.Parse(item.MetadataJson).AdminLabel is { Length: > 0 } label ? label : item.ComponentName;

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
        await _service.DeleteZoneAsync(nodeId, ct);
        return true;
    }

    public async Task<ContentZoneItemDTO> AddItemAsync(Guid zoneNodeId, ContentZoneItemDTO item, CancellationToken ct = default)
        => await _service.AddItemAsync(zoneNodeId, item, ct);

    /// <summary>
    /// <see cref="IContentZoneService.UpdateItemAsync(ContentZoneItemDTO, CancellationToken, int?)"/>; an update
    /// that changes a container's view moves its nested content into the new view's zones in the same write.
    /// </summary>
    public async Task<bool> UpdateItemAsync(ContentZoneItemDTO item, CancellationToken ct = default, int? expectedVersionNumber = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        var existing = item.Version?.Node == null ? null : await _itemStore.GetCurrentDraftAsync(item.Version.Node.Id, ct);
        var remap = existing == null ? null : await PlanSlotRemapAsync(existing, ContentZoneService.MergeUpdate(existing, item), ct);
        return remap == null
            ? await _service.UpdateItemAsync(item, ct, expectedVersionNumber)
            : await _service.UpdateItemAsync(item, remap, expectedVersionNumber, ct);
    }

    public async Task<bool> RemoveItemAsync(Guid itemNodeId, CancellationToken ct = default)
        => await _service.RemoveItemAsync(itemNodeId, ct);

    public async Task<bool> ReorderItemsAsync(Guid zoneNodeId, List<Guid> itemNodeIdsInOrder, CancellationToken ct = default)
        => await _service.ReorderItemsAsync(zoneNodeId, itemNodeIdsInOrder, ct);

    public async Task<(bool Success, string? ErrorMessage)> MoveItemAsync(Guid itemNodeId, Guid targetZoneNodeId, int position, CancellationToken ct = default)
        => await _service.MoveItemAsync(itemNodeId, targetZoneNodeId, position, ct);

    public async Task<List<ContentZoneItemDTO>> GetAllItemVersionsAsync(Guid itemNodeId, CancellationToken ct = default)
        => await _itemStore.GetAllVersionsAsync(itemNodeId, ct);

    /// <summary><see cref="IContentZoneService.GetDescendantItemNodeIdsAsync"/>.</summary>
    internal Task<List<Guid>> GetDescendantItemNodeIdsAsync(Guid containerItemNodeId, CancellationToken ct = default)
        => _service.GetDescendantItemNodeIdsAsync(containerItemNodeId, ct);

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
        var loaded = await LoadRestoreVersionAsync(historicalId, ct);
        if (loaded == null) return null;
        var historical = loaded.Value.Historical;
        return new ContentZoneUpsertViewModel
        {
            NodeId = historical.Version.Node.Id,
            ExpectedVersionNumber = loaded.Value.CurrentVersionNumber,
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
        // A page's zone items are drafts until the page is published; their routes are registered by
        // the page publish cascade so a draft never changes the live route table.
        var pageNodeId = await _service.GetParentPageNodeForZoneAsync(zoneNodeId, ct);
        if (pageNodeId.HasValue)
            return;

        // Global-zone items have no page route to register under; this only clears an inactive item's rows.
        await _routeRegistration.TryRegisterWidgetRoutesAsync(
            componentName, itemNodeId, null, isActive, metadataJson: null, ct);
    }

    private async Task<ContentZoneViewModel> MapToViewModelAsync(ContentZoneDTO zone, CancellationToken ct)
    {
        var vm = new ContentZoneViewModel
        {
            Id = zone.Version.Node.Id,
            Name = zone.Name,
            ZoneObjects = new List<ContentZoneObject>()
        };

        if (zone.Version.Node.Id == Guid.Empty)
            return vm;

        var items = await _service.GetItemsAsync(zone.Version.Node.Id, ct);
        vm.ZoneObjects = items
            .Select(i =>
            {
                var properties = BuildComponentProperties(i);
                return new ContentZoneObject
                {
                    Id = i.Version.Node.Id,
                    Ordinal = i.Ordinal,
                    ZoneId = i.ContentZoneNodeId,
                    VersionNumber = i.Version.VersionNumber,
                    ComponentName = i.ComponentName,
                    ComponentProperties = properties,
                    Metadata = properties is WidgetConfiguration config ? config.Metadata! : EffectiveMetadata(i)
                };
            })
            .ToList();
        return vm;
    }

    /// <summary>The item's metadata, with a stored List mode downgraded where the widget no longer supports lists.</summary>
    private WidgetMetadata EffectiveMetadata(ContentZoneItemDTO item)
    {
        var metadata = WidgetMetadataJson.Parse(item.MetadataJson);

        // A stored List mode only renders as a list while the widget still supports it
        // (the capability is code-owned and can be removed after the item was saved).
        if (metadata.DisplayMode != WidgetDisplayMode.Single && _registry.GetByName(item.ComponentName)?.SupportsList != true)
            metadata.DisplayMode = WidgetDisplayMode.Single;
        return metadata;
    }

    /// <summary>
    /// The configuration a widget is invoked with: its typed config, with the item's metadata attached (so
    /// widget view resolution picks up the display mode) and the item id (so nested zones the widget renders
    /// are owned by it).
    /// </summary>
    private object BuildComponentProperties(ContentZoneItemDTO item)
    {
        var properties = DeserializePropertiesToConfigType(item.ComponentName, item.ComponentPropertiesJson);
        if (properties is WidgetConfiguration widgetConfig)
        {
            widgetConfig.Metadata = EffectiveMetadata(item);
            widgetConfig.ZoneItemNodeId = item.Version.Node.Id;
        }

        return properties;
    }

    /// <summary>
    /// Sanitizes the rich-text properties of a zone item's configuration JSON before it is stored
    /// (see <see cref="WidgetConfigurationJson.Sanitize"/>).
    /// </summary>
    internal string SanitizeProperties(string componentName, string? json)
        => WidgetConfigurationJson.Sanitize(_registry, componentName, json);

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
            var configType = ResolveType(_registry.GetByName(componentName)?.ConfigurationTypeName);
            if (configType != null)
            {
                var config = JsonSerializer.Deserialize(json, configType, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (config != null)
                    return config;
            }

            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            return dict ?? new Dictionary<string, object>();
        }
        catch
        {
            return new { };
        }
    }

    private static Type? ResolveType(string? typeName) => WidgetConfigurationJson.ResolveType(typeName);
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
        return new ContentZoneItemsIndexViewModel
        {
            ZoneId = zone.Version.Node.Id,
            ZoneName = zone.Name,
            ZoneDescription = zone.Description,
            Items = items.Select(i => new ContentZoneItemViewModel
            {
                Id = i.Version.Node.Id,
                ZoneId = i.ContentZoneNodeId,
                ComponentName = i.ComponentName,
                ComponentPropertiesJson = i.ComponentPropertiesJson,
                MetadataJson = i.MetadataJson,
                Ordinal = i.Ordinal,
                IsActive = i.IsActive
            }).ToList()
        };
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
            MetadataJson = item.MetadataJson,
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

        var validation = ModelValidator.Validate(vm);
        if (validation != null)
            return validation;

        // Loaded up front for the stored display mode; a missing item is reported after metadata
        // validation, keeping validation errors ahead of "not found".
        var isCreate = vm.NodeId == null || vm.NodeId == Guid.Empty;
        var existing = isCreate ? null : await _model.GetItemByNodeIdAsync(vm.NodeId!.Value, ct);

        // Null/empty metadata means "not supplied": a create stores "{}" and an update keeps the
        // existing value. Supplied metadata is validated and stored in canonical form; an item that
        // already stores List keeps it even if its widget has since dropped list support.
        string? metadataJson = null;
        if (!string.IsNullOrWhiteSpace(vm.MetadataJson))
        {
            var metadataError = WidgetMetadataJson.Normalize(
                vm.MetadataJson,
                _model.GetWidget(vm.ComponentName),
                WidgetMetadataJson.StoresList(existing?.MetadataJson),
                out metadataJson);
            if (metadataError != null)
                return metadataError;
        }

        if (isCreate)
        {
            if (!Guid.TryParse(parentKey, out var zoneNodeId))
                return new AdminSaveResult(false, "A valid content zone id is required.");

            var newItem = new ContentZoneItemDTO
            {
                ContentZoneNodeId = zoneNodeId,
                ComponentName = vm.ComponentName,
                ComponentPropertiesJson = _model.SanitizeProperties(vm.ComponentName, vm.ComponentPropertiesJson),
                MetadataJson = metadataJson,
                IsActive = vm.IsActive,
            };
            var createdItem = await _model.AddItemAsync(zoneNodeId, newItem, ct);
            await _model.RegisterWidgetRouteIfRoutableAsync(
                createdItem.ComponentName, createdItem.Version.Node!.Id, zoneNodeId,
                createdItem.IsActive, ct);
            return new AdminSaveResult(true, NodeId: createdItem.Version.Node!.Id);
        }

        if (existing == null)
            return new AdminSaveResult(false, "Content zone item not found.");

        // The caller's view of the item: an edit made since it was loaded is never silently overwritten.
        if (vm.ExpectedVersionNumber.HasValue && existing.Version.VersionNumber != vm.ExpectedVersionNumber.Value)
            return new AdminSaveResult(false, ContentStore<ContentZoneItemDTO>.StaleVersionMessage);

        var updated = existing with
        {
            ComponentName = vm.ComponentName,
            ComponentPropertiesJson = _model.SanitizeProperties(vm.ComponentName, vm.ComponentPropertiesJson),
            // Not supplied: keep the existing metadata so a content-config save never wipes the
            // per-item metadata dialog's values.
            MetadataJson = metadataJson ?? existing.MetadataJson,
            IsActive = vm.IsActive,
        };
        var ok = await _model.UpdateItemAsync(updated, ct, vm.ExpectedVersionNumber);
        if (!ok)
        {
            // With an expected version, a rejection past the pre-check is a concurrent write the store caught.
            return new AdminSaveResult(false, vm.ExpectedVersionNumber.HasValue
                ? ContentStore<ContentZoneItemDTO>.StaleVersionMessage
                : "Update failed.");
        }

        await _model.RegisterWidgetRouteIfRoutableAsync(
            updated.ComponentName, existing.Version.Node.Id, existing.ContentZoneNodeId,
            updated.IsActive, ct);
        return new AdminSaveResult(true, NodeId: existing.Version.Node.Id);
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

    public bool SupportsReorder => true;

    public async Task<(bool Success, string? ErrorMessage)> ReorderAsync(string parentKey, List<Guid> orderedIds, CancellationToken ct = default)
    {
        if (!Guid.TryParse(parentKey, out var zoneNodeId))
            return (false, "The parent key must be a content zone id.");
        return await _model.ReorderItemsAsync(zoneNodeId, orderedIds, ct)
            ? (true, null)
            : (false, "Failed to reorder the items.");
    }

    public bool SupportsMove => true;

    public async Task<(bool Success, string? ErrorMessage)> MoveAsync(Guid childId, string targetParentKey, int position, CancellationToken ct = default)
    {
        if (!Guid.TryParse(targetParentKey, out var targetZoneNodeId))
            return (false, "The target parent key must be a content zone id.");
        return await _model.MoveItemAsync(childId, targetZoneNodeId, position, ct);
    }

    public bool SupportsVersionHistory => true;

    /// <summary>
    /// The item's versions. A container's page also lists every version of every widget inside it (nested
    /// containers included), newest first, each row labelled with its widget and restorable on its own.
    /// </summary>
    public async Task<VersionHistoryViewModel?> GetChildVersionHistoryViewModelAsync(string parentKey, Guid nodeId, CancellationToken ct = default)
    {
        var versions = await _model.GetAllItemVersionsAsync(nodeId, ct);
        if (!versions.Any()) return null;

        var title = ItemTitle(versions.First());
        var rows = VersionRows(versions, $"{title} (this container)");
        foreach (var descendantId in await _model.GetDescendantItemNodeIdsAsync(nodeId, ct))
        {
            var nested = await _model.GetAllItemVersionsAsync(descendantId, ct);
            if (nested.Count > 0)
                rows.AddRange(VersionRows(nested, ItemTitle(nested.First())));
        }

        var isCombined = rows.Count > versions.Count;
        return new VersionHistoryViewModel
        {
            ContentType = "contentzones",
            NodeId = nodeId,
            ItemTitle = title,
            BackUrl = "/wadmin/contentzones",
            CurrentVersionNumber = versions.FirstOrDefault(v => v.Version.IsCurrentDraft)?.Version.VersionNumber,
            ParentKey = parentKey,
            ChildType = "items",
            IsCombined = isCombined,
            Versions = isCombined
                ? rows.OrderByDescending(r => r.ModificationDate).ThenByDescending(r => r.Version).ToList()
                : rows
        };
    }

    /// <summary>One widget's history rows; latest and current version are per widget.</summary>
    private static List<VersionItemViewModel> VersionRows(List<ContentZoneItemDTO> versions, string widgetLabel)
    {
        var maxVersion = versions.Max(v => v.Version.VersionNumber);
        var currentVersion = versions.FirstOrDefault(v => v.Version.IsCurrentDraft)?.Version.VersionNumber;
        return versions.Select(v => new VersionItemViewModel
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
            ChangeSetId = v.Version.ChangeSetId,
            NodeId = v.Version.Node.Id,
            WidgetLabel = widgetLabel,
            NodeCurrentVersionNumber = currentVersion
        }).ToList();
    }

    /// <summary>
    /// The history page's heading: the version title, else (zone items saved by the editor carry none)
    /// the widget's admin label, else its component name.
    /// </summary>
    private static string ItemTitle(ContentZoneItemDTO item)
        => !string.IsNullOrWhiteSpace(item.Version.Title) ? item.Version.Title
         : ContentZoneModel.ItemLabel(item);

    public async Task<object?> GetChildRestoreVersionViewModelAsync(string parentKey, Guid historicalId, CancellationToken ct = default)
    {
        var historical = await _model.GetItemVersionAsync(historicalId, ct);
        if (historical == null) return null;
        var current = await _model.GetItemByNodeIdAsync(historical.Version.Node.Id, ct);
        if (current == null) return null;
        return new ContentZoneItemUpsertViewModel
        {
            NodeId = historical.Version.Node.Id,
            ContentZoneNodeId = historical.ContentZoneNodeId,
            ExpectedVersionNumber = current.Version.VersionNumber,
            ComponentName = historical.ComponentName,
            ComponentPropertiesJson = historical.ComponentPropertiesJson,
            MetadataJson = WidgetMetadataJson.Restorable(historical.MetadataJson, _model.GetWidget(historical.ComponentName), current.MetadataJson),
            IsActive = historical.IsActive,
        };
    }

    /// <summary>
    /// One-step restore: the version's content, metadata (List→Single where the widget no longer
    /// supports lists), active state, zone and ordinal, as a draft until the page publishes. A
    /// global-zone item goes live now, so its widget routes follow.
    /// </summary>
    public async Task<AdminSaveResult> RestoreChildVersionAsync(string parentKey, Guid versionId, int? expectedVersionNumber, CancellationToken ct = default)
    {
        var historical = await _model.GetItemVersionAsync(versionId, ct);
        if (historical == null)
            return new AdminSaveResult(false, IAdminCrudChildHandler.VersionNotFoundMessage);

        var nodeId = historical.Version.Node.Id;
        var current = await _model.GetItemByNodeIdAsync(nodeId, ct);
        if (current == null)
            return new AdminSaveResult(false, IContentZoneService.ItemNotFoundMessage);

        var metadataJson = WidgetMetadataJson.Restorable(historical.MetadataJson, _model.GetWidget(historical.ComponentName), current.MetadataJson);
        var (success, error) = await _model.RestoreItemVersionAsync(versionId, metadataJson, expectedVersionNumber, ct);
        if (!success)
            return new AdminSaveResult(false, error);

        await _model.RegisterWidgetRouteIfRoutableAsync(
            historical.ComponentName, nodeId, historical.ContentZoneNodeId, historical.IsActive, ct);
        return new AdminSaveResult(true, NodeId: nodeId);
    }

    public async Task<bool> DeleteChildVersionAsync(Guid id, CancellationToken ct = default)
        => (await TryDeleteChildVersionAsync(id, ct)).Success;

    /// <summary>A version a page-history entry captured cannot be deleted while that entry exists.</summary>
    public async Task<(bool Success, string? ErrorMessage)> TryDeleteChildVersionAsync(Guid id, CancellationToken ct = default)
    {
        var entries = await _model.PageHistory.GetSnapshotsReferencingVersionAsync(id, ct);
        if (entries.Count > 0)
            return (false, PageHistoryMessages.VersionInHistory(entries[0]));

        return await _model.DeleteItemVersionAsync(id, ct)
            ? (true, null)
            : (false, IAdminCrudHandler.VersionNotDeletedMessage);
    }
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
            category = c.Category,
            supportsList = c.SupportsList
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

            if (p.EditorType == EditorType.ViewPicker)
            {
                // The picker lists only this property's render mode's views. The component name
                // comes from the attribute when set, otherwise from the registered widget.
                var viewComponentName = string.IsNullOrWhiteSpace(p.ViewComponentName) ? component.Name : p.ViewComponentName;
                var views = _viewDiscoveryService.GetAvailableViews(viewComponentName, p.RenderMode);
                if (views.Any())
                    dropdownOptions = views.ToDictionary(v => v, v => v);
                else
                {
                    _logger.Warning("No {Mode} views found for ViewComponent '{ComponentName}'", p.RenderMode, viewComponentName);
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
                renderMode = p.RenderMode.ToString().ToLowerInvariant(),
                displayModes = p.DisplayModes.ToString().ToLowerInvariant(),
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
            supportsList = component.SupportsList,
            properties
        });
    }

    public IActionResult GetForm(string name, string? valuesJson) => GetForm(name, valuesJson, null);

    public IActionResult GetForm(string name, string? valuesJson, WidgetDisplayMode? displayMode)
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

        return WebWayCMS.Forms.DynamicConfigurationForm.Render(instance, name, displayMode);
    }
}
