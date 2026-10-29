using Microsoft.AspNetCore.Http;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.PageHistory;
using WebWayCMS.Models.Shared;
using WebWayCMS.Security;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Models.ContentZone;

/// <summary>
/// Admin and MCP handler for content zone items (key <c>contentzoneitems</c>): a top-level content type
/// whose items sit in a zone through the ordered containment <see cref="ZoneRelations.Zone"/>. Every
/// write goes through <see cref="ContentZoneModel"/> / <see cref="IContentZoneService"/>, so page-draft
/// semantics, page history, slot remap and config sanitize/extract all apply. Removing an item is never a
/// delete: it saves an inactive version.
/// </summary>
public sealed class ContentZoneItemModel : IAdminCrudHandler
{
    private readonly ContentZoneModel _model;

    public ContentZoneItemModel(ContentZoneModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public string ContentType => ContentRelationRegistry.ContentZoneItemContentType;
    public string DisplayName => "Content Zone Item";
    public string[]? WriteRoles => null;

    /// <summary>An item goes live with its page's publish (or at once in a global zone), never on its own.</summary>
    public bool SupportsPublishing => false;

    public string IndexViewPath => "~/Views/AdminContentZone/ContentZoneItems.cshtml";
    public string UpsertViewPath => "~/Views/AdminContentZone/ContentZoneItemUpsert.cshtml";

    public bool HasSecondaryApiList => false;
    public IAdminRegistryHandler? RegistryHandler => null;
    public bool SupportsVersionHistory => true;

    /// <summary>The query key that names the zone to list (the relation property, as a "New" link sets it).</summary>
    public const string ZoneQueryKey = nameof(ContentZoneItemUpsertViewModel.ContentZoneNodeId);

    /// <summary>With no zone named there is nothing to list: the index asks for one.</summary>
    public Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
        => Task.FromResult<object>(new ContentZoneItemsIndexViewModel());

    public async Task<object> GetIndexViewModelAsync(IQueryCollection query, CancellationToken ct = default)
    {
        var key = query.Keys.FirstOrDefault(k => string.Equals(k, ZoneQueryKey, StringComparison.OrdinalIgnoreCase));
        if (key == null || !Guid.TryParse(query[key].FirstOrDefault(), out var zoneNodeId))
            return new ContentZoneItemsIndexViewModel();

        var zone = await _model.GetByIdAsync(zoneNodeId, ct);
        if (zone == null)
            return new ContentZoneItemsIndexViewModel();

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

    /// <summary>
    /// The item's form, or an empty one for a create (the admin controller preselects the zone from the
    /// query); null when the item does not exist.
    /// </summary>
    public async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
    {
        if (id == null || id == Guid.Empty)
            return new ContentZoneItemUpsertViewModel();

        var item = await _model.GetItemByNodeIdAsync(id.Value, ct);
        if (item == null) return null;
        var zone = await _model.GetByIdAsync(item.ContentZoneNodeId, ct);
        return new ContentZoneItemUpsertViewModel
        {
            NodeId = item.Version.Node.Id,
            ContentZoneNodeId = item.ContentZoneNodeId,
            ZoneName = zone?.Name,
            ExpectedVersionNumber = item.Version.VersionNumber,
            ComponentName = item.ComponentName,
            ComponentPropertiesJson = _model.HydratePropertiesJson(
                item.ComponentName, item.ComponentPropertiesJson, item.Version.EffectiveRelations()),
            MetadataJson = item.MetadataJson,
            IsActive = item.IsActive,
        };
    }

    public object CreateEmptyUpsertViewModel() => new ContentZoneItemUpsertViewModel();

    public async Task<AdminSaveResult> SaveUpsertAsync(object model, CancellationToken ct = default)
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
            if (vm.ContentZoneNodeId == Guid.Empty || await _model.GetByIdAsync(vm.ContentZoneNodeId, ct) == null)
                return new AdminSaveResult(false, "Choose the content zone this item belongs to.", nameof(vm.ContentZoneNodeId));

            var (strippedJson, relations) = _model.ExtractConfigRelations(vm.ComponentName, vm.ComponentPropertiesJson);
            var relationError = await _model.ValidateConfigRelationsAsync(vm.ComponentName, relations, ct);
            if (relationError != null)
                return relationError;

            // Placed at ordinal 0, which AddItemAsync turns into last in the zone.
            var newItem = new ContentZoneItemDTO
            {
                ComponentName = vm.ComponentName,
                ComponentPropertiesJson = _model.SanitizeProperties(vm.ComponentName, strippedJson),
                MetadataJson = metadataJson,
                IsActive = vm.IsActive,
            };
            _model.ApplyConfigRelations(newItem.Version, newItem.ComponentName, relations);
            ZoneRelations.Place(newItem.Version, vm.ContentZoneNodeId, 0);
            var createdItem = await _model.AddItemAsync(vm.ContentZoneNodeId, newItem, ct);
            await _model.RegisterWidgetRouteIfRoutableAsync(
                createdItem.ComponentName, createdItem.Version.Node!.Id, vm.ContentZoneNodeId,
                createdItem.IsActive, ct);
            return new AdminSaveResult(true, NodeId: createdItem.Version.Node!.Id);
        }

        if (existing == null)
            return new AdminSaveResult(false, "Content zone item not found.");

        // The caller's view of the item: an edit made since it was loaded is never silently overwritten.
        if (vm.ExpectedVersionNumber.HasValue && existing.Version.VersionNumber != vm.ExpectedVersionNumber.Value)
            return new AdminSaveResult(false, ContentStore<ContentZoneItemDTO>.StaleVersionMessage);

        var (updateStrippedJson, updateRelations) = _model.ExtractConfigRelations(vm.ComponentName, vm.ComponentPropertiesJson);
        var updateRelationError = await _model.ValidateConfigRelationsAsync(vm.ComponentName, updateRelations, ct);
        if (updateRelationError != null)
            return updateRelationError;

        var updated = existing with
        {
            ComponentName = vm.ComponentName,
            ComponentPropertiesJson = _model.SanitizeProperties(vm.ComponentName, updateStrippedJson),
            // Not supplied: keep the existing metadata so a content-config save never wipes the
            // per-item metadata dialog's values.
            MetadataJson = metadataJson ?? existing.MetadataJson,
            IsActive = vm.IsActive,
        };
        _model.ApplyConfigRelations(updated.Version, updated.ComponentName, updateRelations);
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

    /// <summary>Removes the item: an inactive version (a draft on a page), never a delete.</summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
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

    public async Task<(bool Success, string? ErrorMessage, int DeletedDescendants)> TryDeleteAsync(Guid id, CancellationToken ct = default)
        => await DeleteAsync(id, ct)
            ? (true, null, 0)
            : (false, IContentZoneService.ItemNotFoundMessage, 0);

    public Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default)
        => Task.FromResult(Enumerable.Empty<object>());

    public Task<IEnumerable<object>> GetSecondaryApiListAsync(string key, CancellationToken ct = default)
        => Task.FromResult(Enumerable.Empty<object>());

    // ─── containment: the items of a zone ──────────────────────────────────────

    public async Task<IReadOnlyList<RelatedChild>> GetRelatedChildrenAsync(string relationName, Guid parentNodeId, CancellationToken ct = default)
    {
        if (relationName != ZoneRelations.Zone)
            return [];

        var items = await _model.GetCurrentDraftItemsAsync(parentNodeId, ct);
        return items
            .Select(i => new RelatedChild(i.Version.Node.Id, ContentZoneModel.ItemLabel(i), i.Version.State, i.Ordinal))
            .ToList();
    }

    public async Task<(bool Success, string? ErrorMessage)> ReorderRelatedAsync(
        string relationName, Guid parentNodeId, IReadOnlyList<Guid> orderedNodeIds, CancellationToken ct = default)
    {
        if (relationName != ZoneRelations.Zone)
            return (false, IAdminCrudHandler.ReorderNotSupportedMessage);
        return await _model.ReorderItemsAsync(parentNodeId, orderedNodeIds.ToList(), ct)
            ? (true, null)
            : (false, "Failed to reorder the items.");
    }

    /// <summary>
    /// A move to another zone of the same page (or a reorder within the zone), checked against the item's
    /// version as the caller read it; a draft until the page is published.
    /// </summary>
    public async Task<(bool Success, string? ErrorMessage)> MoveRelatedAsync(
        string relationName, Guid childNodeId, Guid targetParentNodeId, int? position, int? expectedVersionNumber,
        CancellationToken ct = default)
    {
        if (relationName != ZoneRelations.Zone)
            return (false, IAdminCrudHandler.MoveNotSupportedMessage);
        return await _model.MoveItemAsync(childNodeId, targetParentNodeId, position ?? int.MaxValue, ct, expectedVersionNumber);
    }

    // ─── version history ────────────────────────────────────────────────────────

    /// <summary>
    /// The item's versions. A container's page also lists every version of every widget inside it (nested
    /// containers included), newest first, each row labelled with its widget and restorable on its own.
    /// </summary>
    public async Task<VersionHistoryViewModel?> GetVersionHistoryViewModelAsync(Guid nodeId, CancellationToken ct = default)
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

        var current = versions.FirstOrDefault(v => v.Version.IsCurrentDraft) ?? versions.First();
        var isCombined = rows.Count > versions.Count;
        return new VersionHistoryViewModel
        {
            ContentType = ContentType,
            NodeId = nodeId,
            ItemTitle = title,
            BackUrl = $"/wadmin/{ContentType}?{ZoneQueryKey}={current.ContentZoneNodeId}",
            CurrentVersionNumber = versions.FirstOrDefault(v => v.Version.IsCurrentDraft)?.Version.VersionNumber,
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

    public async Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default)
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
            ComponentPropertiesJson = _model.HydratePropertiesJson(
                historical.ComponentName, historical.ComponentPropertiesJson, historical.Version.EffectiveRelations()),
            MetadataJson = WidgetMetadataJson.Restorable(historical.MetadataJson, _model.GetWidget(historical.ComponentName), current.MetadataJson),
            IsActive = historical.IsActive,
        };
    }

    public Task<AdminSaveResult> RestoreVersionAsync(Guid versionId, CancellationToken ct = default)
        => RestoreVersionAsync(versionId, null, ct);

    /// <summary>
    /// One-step restore: the version's content, metadata (List→Single where the widget no longer
    /// supports lists), active state, zone and ordinal, as a draft until the page publishes. A
    /// global-zone item goes live now, so its widget routes follow.
    /// </summary>
    public async Task<AdminSaveResult> RestoreVersionAsync(Guid versionId, int? expectedVersionNumber, CancellationToken ct = default)
    {
        var historical = await _model.GetItemVersionAsync(versionId, ct);
        if (historical == null)
            return new AdminSaveResult(false, IAdminCrudHandler.VersionNotFoundMessage);

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

    public async Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default)
        => (await TryDeleteVersionAsync(id, ct)).Success;

    /// <summary>A version a page-history entry captured cannot be deleted while that entry exists.</summary>
    public async Task<(bool Success, string? ErrorMessage)> TryDeleteVersionAsync(Guid id, CancellationToken ct = default)
    {
        var entries = await _model.PageHistory.GetSnapshotsReferencingVersionAsync(id, ct);
        if (entries.Count > 0)
            return (false, PageHistoryMessages.VersionInHistory(entries[0]));

        return await _model.DeleteItemVersionAsync(id, ct)
            ? (true, null)
            : (false, IAdminCrudHandler.VersionNotDeletedMessage);
    }
}
