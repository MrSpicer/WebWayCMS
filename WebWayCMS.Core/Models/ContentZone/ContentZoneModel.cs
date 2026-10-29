using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Forms;
using WebWayCMS.Models.PageHistory;
using WebWayCMS.Models.Shared;
using WebWayCMS.Security;
using WebWayCMS.Services;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Models.ContentZone;

public class ContentZoneModel : AdminCrudModel<ContentZoneDTO>, IContentZoneModel, IAdminCrudHandler
{
    private readonly IContentZoneService _service;
    private readonly IContentStore<ContentZoneDTO> _zoneStore;
    private readonly IContentStore<ContentZoneItemDTO> _itemStore;
    private readonly IWidgetRegistry _registry;
    private readonly ICMSRouteService _routeService;
    private readonly IRouteRegistrationService _routeRegistration;
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
        IChangeSetScope changeSetScope,
        IRelationBinder relationBinder,
        IContentDeleteService deletes)
        : base(changeSetScope, relationBinder, deletes)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _slotDiscovery = slotDiscovery ?? throw new ArgumentNullException(nameof(slotDiscovery));
        PageHistory = pageHistory ?? throw new ArgumentNullException(nameof(pageHistory));
        _zoneStore = zoneStore ?? throw new ArgumentNullException(nameof(zoneStore));
        _itemStore = itemStore ?? throw new ArgumentNullException(nameof(itemStore));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
        _routeRegistration = routeRegistration ?? throw new ArgumentNullException(nameof(routeRegistration));
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
        var owned = await _service.GetByPageSlotAsync(pageNodeId, slotName, ct);
        if (owned == null)
            return null;

        var zone = await _service.GetZoneByNodeAsync(owned.ZoneNodeId, ct);
        return zone == null ? null : await MapToViewModelAsync(zone, ct);
    }

    public async Task<ContentZoneViewModel> GetOrCreateViewModelByItemSlotAsync(Guid parentItemNodeId, string slotName, CancellationToken ct = default)
    {
        var (zone, _) = await _service.GetOrCreateByItemSlotAsync(parentItemNodeId, slotName, ct);
        return await MapToViewModelAsync(zone, ct);
    }

    public async Task<ContentZoneViewModel?> GetViewModelByItemSlotAsync(Guid parentItemNodeId, string slotName, CancellationToken ct = default)
    {
        var owned = await _service.GetByItemSlotAsync(parentItemNodeId, slotName, ct);
        if (owned == null)
            return null;

        var zone = await _service.GetZoneByNodeAsync(owned.ZoneNodeId, ct);
        return zone == null ? null : await MapToViewModelAsync(zone, ct);
    }

    public async Task<ContentZoneViewModel?> GetViewModelByIdAsync(Guid nodeId, CancellationToken ct = default)
    {
        var zone = await _service.GetZoneByNodeAsync(nodeId, ct);
        return zone == null ? null : await MapToViewModelAsync(zone, ct);
    }

    public async Task<IReadOnlyList<PageZoneSlot>> GetPageZoneSlotsAsync(Guid pageNodeId, CancellationToken ct = default)
    {
        var owned = await _service.GetOwnedZonesForPageAsync(pageNodeId, ct);
        return owned
            .OrderBy(a => a.SlotName, StringComparer.OrdinalIgnoreCase)
            .Select(a => new PageZoneSlot(a.SlotName, a.ZoneNodeId))
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

    protected override async Task<bool> DeleteSelfAsync(Guid nodeId, CancellationToken ct = default)
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

    public async Task<(bool Success, string? ErrorMessage)> MoveItemAsync(
        Guid itemNodeId, Guid targetZoneNodeId, int position, CancellationToken ct = default, int? expectedVersionNumber = null)
        => await _service.MoveItemAsync(itemNodeId, targetZoneNodeId, position, ct, expectedVersionNumber);

    public async Task<List<ContentZoneItemDTO>> GetAllItemVersionsAsync(Guid itemNodeId, CancellationToken ct = default)
        => await _itemStore.GetAllVersionsAsync(itemNodeId, ct);

    /// <summary><see cref="IContentZoneService.GetDescendantItemNodeIdsAsync"/>.</summary>
    internal Task<List<Guid>> GetDescendantItemNodeIdsAsync(Guid containerItemNodeId, CancellationToken ct = default)
        => _service.GetDescendantItemNodeIdsAsync(containerItemNodeId, ct);

    /// <summary>The zone's active items as current drafts (what the editor and the next publish see), by position.</summary>
    internal async Task<List<ContentZoneItemDTO>> GetCurrentDraftItemsAsync(Guid zoneNodeId, CancellationToken ct = default)
        => (await _itemStore.FindCurrentDraftsRelatedAsync(ZoneRelations.Zone, zoneNodeId, ct)).Where(i => i.IsActive).ToList();

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
        var properties = DeserializePropertiesToConfigType(
            item.ComponentName, item.ComponentPropertiesJson, item.Version.EffectiveRelations());
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

    /// <summary>Lifts the configuration type's <c>[ContentRelation]</c> ids out of the stored JSON.</summary>
    internal (string StrippedJson, IReadOnlyList<RelationValue> Relations) ExtractConfigRelations(
        string componentName, string? json)
        => WidgetConfigurationJson.ExtractRelations(_registry, componentName, json);

    /// <summary>Validates extracted config relations (target existence, type and required) as a field error.</summary>
    internal Task<AdminSaveResult?> ValidateConfigRelationsAsync(
        string componentName, IReadOnlyList<RelationValue> relations, CancellationToken ct)
    {
        var configType = WidgetConfigurationJson.ResolveConfigurationType(_registry, componentName);
        if (configType == null)
            return Task.FromResult<AdminSaveResult?>(null);

        var config = Activator.CreateInstance(configType)!;
        RelationProperties.Write(config, relations, ContentRelationRegistry.ConfigPrefix);
        var displayName = _registry.GetByName(componentName)?.DisplayName ?? componentName;
        return RelationBinder.ValidateAsync(config, displayName, ct);
    }

    /// <summary>Stores extracted config relations as <c>config.*</c> rows on the item's new version.</summary>
    internal void ApplyConfigRelations(ContentVersion version, string componentName, IReadOnlyList<RelationValue> relations)
        => WidgetConfigurationJson.ApplyRelations(
            version, WidgetConfigurationJson.ResolveConfigurationType(_registry, componentName), relations);

    /// <summary>The stored configuration JSON with the item's relation values written back in.</summary>
    internal string HydratePropertiesJson(
        string componentName, string? json, IEnumerable<ContentRelation> relations)
        => WidgetConfigurationJson.HydrateRelations(
            WidgetConfigurationJson.ResolveConfigurationType(_registry, componentName), json, relations);

    /// <summary>
    /// Deserializes properties JSON into the actual configuration type for the component.
    /// Falls back to a dictionary if the component type is not registered.
    /// </summary>
    private object DeserializePropertiesToConfigType(
        string componentName, string json, IEnumerable<ContentRelation> relations)
    {
        object config;
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
        {
            var defaultConfig = _registry.CreateDefaultConfiguration(componentName);
            if (defaultConfig == null)
                return new { };
            config = defaultConfig;
        }
        else
        {
            try
            {
                var configType = ResolveType(_registry.GetByName(componentName)?.ConfigurationTypeName);
                if (configType != null)
                {
                    var deserialized = JsonSerializer.Deserialize(json, configType, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (deserialized != null)
                    {
                        config = deserialized;
                        WriteRelationValues(config, relations);
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

        WriteRelationValues(config, relations);
        return config;
    }

    private static void WriteRelationValues(object config, IEnumerable<ContentRelation> relations)
    {
        RelationProperties.Write(
            config,
            relations.Select(r => new RelationValue(r.Name, r.TargetNodeId, r.Ordinal, r.Qualifier)),
            ContentRelationRegistry.ConfigPrefix);
    }

    private static Type? ResolveType(string? typeName) => WidgetConfigurationJson.ResolveType(typeName);
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
