using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using WebWayCMS.ContentZones;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Services;

namespace WebWayCMS.Controllers.Api;

/// <summary>
/// API controller for content zone operations.
/// Used by the inline edit mode to add/update/delete zone items.
/// </summary>
[ApiController]
[Route("api/contentzones")]
[Authorize(Roles = "Admin")]
public class ContentZoneApiController : ControllerBase
{
    private static readonly Serilog.ILogger Logger = Serilog.Log.ForContext<ContentZoneApiController>();

    private readonly IContentZoneService _service;
    private readonly IRouteRegistrationService _routeRegistration;
    private readonly IWidgetRegistry _widgetRegistry;

    public ContentZoneApiController(IContentZoneService service, IRouteRegistrationService routeRegistration, IWidgetRegistry widgetRegistry)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _routeRegistration = routeRegistration ?? throw new ArgumentNullException(nameof(routeRegistration));
        _widgetRegistry = widgetRegistry ?? throw new ArgumentNullException(nameof(widgetRegistry));
    }

    /// <summary>
    /// Add or update a content zone item.
    /// If the zone doesn't exist, it will be created automatically.
    /// </summary>
    [HttpPost("items")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveItem([FromBody] SaveItemRequest request, CancellationToken ct)
    {
        if (request == null)
            return BadRequest(new { error = "Request body is required." });

        if (string.IsNullOrWhiteSpace(request.ComponentName))
            return BadRequest(new { error = "Component name is required." });

        if (string.IsNullOrWhiteSpace(request.ZoneName))
            return BadRequest(new { error = "Zone name is required." });

        // Metadata is only taken on create (the Add Widget modal's Single/List choice); updates
        // keep the stored metadata, which the settings dialog edits through its own endpoint.
        string? createMetadataJson = null;
        if (!string.IsNullOrWhiteSpace(request.MetadataJson))
        {
            var metadataError = WidgetMetadataJson.Normalize(request.MetadataJson, _widgetRegistry.GetByName(request.ComponentName), out createMetadataJson);
            if (metadataError != null)
                return BadRequest(new { error = metadataError.ErrorMessage });
        }

        // Rich-text config fields are rendered with @Html.Raw, so the payload is sanitized here like
        // every other zone-item write path (the child handler does the same).
        var propertiesJson = WidgetConfigurationJson.Sanitize(_widgetRegistry, request.ComponentName, request.ComponentPropertiesJson);

        try
        {
            // Get or create the zone
            Guid zoneNodeId;
            if (request.ZoneId.HasValue && request.ZoneId.Value != Guid.Empty)
            {
                zoneNodeId = request.ZoneId.Value;
            }
            else
            {
                // Prefer assignment-based lookup if context is provided
                if (request.ParentPageNodeId.HasValue && !string.IsNullOrWhiteSpace(request.SlotName))
                {
                    var (zone, _) = await _service.GetOrCreateByPageSlotAsync(request.ParentPageNodeId.Value, request.SlotName, ct);
                    zoneNodeId = zone.Version.Node.Id;
                }
                else
                {
                    // Fallback: name-based lookup or create
                    var existingZone = await _service.GetZoneByNameAsync(request.ZoneName, ct);
                    if (existingZone != null)
                    {
                        zoneNodeId = existingZone.Version.Node.Id;
                    }
                    else
                    {
                        var createdZone = await _service.GetOrCreateByNameAsync(request.ZoneName, ct);
                        zoneNodeId = createdZone.Version.Node.Id;
                    }
                }
            }

            if (zoneNodeId == Guid.Empty)
                return BadRequest(new { error = "Could not resolve the content zone." });

            // Create or update the item
            if (request.ItemId.HasValue && request.ItemId.Value != Guid.Empty)
            {
                var existingItem = await _service.GetItemByNodeIdAsync(request.ItemId.Value, ct);
                if (existingItem == null)
                    return NotFound(new { error = "Item not found." });

                // Reject up front so the client gets the friendly message, not "item not found".
                if (request.ExpectedVersionNumber.HasValue
                    && existingItem.Version.VersionNumber != request.ExpectedVersionNumber.Value)
                    return Conflict(new { error = ContentStore<ContentZoneItemDTO>.StaleVersionMessage });

                // Update existing item — service preserves Ordinal and ContentZoneNodeId from existing record
                var item = new ContentZoneItemDTO
                {
                    Version = new ContentVersion { Node = new ContentNode { Id = request.ItemId.Value } },
                    ContentZoneNodeId = zoneNodeId,
                    ComponentName = request.ComponentName,
                    ComponentPropertiesJson = propertiesJson,
                    IsActive = true
                };

                var updated = await _service.UpdateItemAsync(item, ct, request.ExpectedVersionNumber);
                if (!updated)
                    return ItemWriteFailed(request.ExpectedVersionNumber);

                var routeError = await RegisterGlobalWidgetRoutesAsync(
                    item.ComponentName, existingItem.Version.Node.Id, zoneNodeId, request.ParentPageNodeId, item.IsActive, ct);
                if (routeError != null)
                    return routeError;

                return Ok(new { success = true, itemId = request.ItemId.Value, zoneId = zoneNodeId });
            }
            else
            {
                // Create new item - ID is auto-generated
                var item = new ContentZoneItemDTO
                {
                    ContentZoneNodeId = zoneNodeId,
                    ComponentName = request.ComponentName,
                    ComponentPropertiesJson = propertiesJson,
                    MetadataJson = createMetadataJson,
                    IsActive = true
                };

                var createdItem = await _service.AddItemAsync(zoneNodeId, item, ct);

                var routeError = await RegisterGlobalWidgetRoutesAsync(
                    item.ComponentName, createdItem.Version.Node!.Id, zoneNodeId, request.ParentPageNodeId, createdItem.IsActive, ct);
                if (routeError != null)
                    return routeError;

                return Ok(new { success = true, itemId = createdItem.Version.Node.Id, zoneId = zoneNodeId });
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to save content zone item.");
            return StatusCode(500, new { error = "Failed to save item." });
        }
    }

    /// <summary>
    /// Delete a content zone item.
    /// </summary>
    [HttpDelete("items/{itemId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteItem(Guid itemId, CancellationToken ct)
    {
        try
        {
            var item = await _service.GetItemByNodeIdAsync(itemId, ct);
            if (item != null)
            {
                var routeError = await RegisterGlobalWidgetRoutesAsync(
                    item.ComponentName, item.Version.Node.Id, item.ContentZoneNodeId, null, false, ct);
                if (routeError != null)
                    return routeError;
            }

            var deleted = await _service.RemoveItemAsync(itemId, ct);
            if (!deleted)
                return NotFound(new { error = "Item not found." });

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to delete content zone item.");
            return StatusCode(500, new { error = "Failed to delete item." });
        }
    }

    /// <summary>
    /// Moves an item to a 0-based position in a zone: a reorder within its own zone, or a move to
    /// another zone of the same page (a draft until the page is published). Used by the zone editor's
    /// drag-and-drop and keyboard reordering.
    /// </summary>
    [HttpPost("items/{itemId:guid}/move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveItem(Guid itemId, [FromBody] MoveItemRequest request, CancellationToken ct)
    {
        if (request == null)
            return BadRequest(new { error = "Request body is required." });

        if (request.TargetZoneId == Guid.Empty)
            return BadRequest(new { error = "Target zone is required." });

        try
        {
            var (success, errorMessage) = await _service.MoveItemAsync(itemId, request.TargetZoneId, request.Position, ct, request.ExpectedVersionNumber);
            if (!success)
            {
                if (errorMessage == IContentZoneService.ItemNotFoundMessage)
                    return NotFound(new { error = errorMessage });

                if (errorMessage == ContentStore<ContentZoneItemDTO>.StaleVersionMessage)
                    return Conflict(new { error = errorMessage });

                return BadRequest(new { error = errorMessage ?? "Failed to move item." });
            }

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to move content zone item.");
            return StatusCode(500, new { error = "Failed to move item." });
        }
    }

    /// <summary>
    /// Get a specific item for editing.
    /// </summary>
    [HttpGet("items/{itemId:guid}")]
    public async Task<IActionResult> GetItem(Guid itemId, CancellationToken ct)
    {
        var item = await _service.GetItemByNodeIdAsync(itemId, ct);

        if (item == null)
            return NotFound(new { error = "Item not found." });

        return Ok(new
        {
            id = item.Version.Node.Id,
            zoneId = item.ContentZoneNodeId,
            componentName = item.ComponentName,
            componentPropertiesJson = item.ComponentPropertiesJson,
            metadataJson = item.MetadataJson,
            ordinal = item.Ordinal,
            isActive = item.IsActive
        });
    }

    /// <summary>
    /// Updates the per-item widget metadata (the cog editor's values). Validated as a
    /// <see cref="WidgetMetadata"/> and saved through the zone service, which auto-publishes like
    /// every other item write.
    /// </summary>
    [HttpPut("items/{itemId:guid}/metadata")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateItemMetadata(Guid itemId, [FromBody] UpdateItemMetadataRequest request, CancellationToken ct)
    {
        if (request == null)
            return BadRequest(new { error = "Request body is required." });

        try
        {
            var existing = await _service.GetItemByNodeIdAsync(itemId, ct);
            if (existing == null)
                return NotFound(new { error = "Item not found." });

            // Reject up front so the client gets the friendly message, not "item not found".
            if (request.ExpectedVersionNumber.HasValue && existing.Version.VersionNumber != request.ExpectedVersionNumber.Value)
                return Conflict(new { error = ContentStore<ContentZoneItemDTO>.StaleVersionMessage });

            // The dialog always saves the whole document, so an empty body clears the metadata. It
            // still goes through Normalize, so clearing keeps a grandfathered List (the dialog of a
            // widget that lost list support hides the field and may serialize nothing at all).
            var source = string.IsNullOrWhiteSpace(request.MetadataJson) ? "{}" : request.MetadataJson;
            var validation = WidgetMetadataJson.Normalize(
                source,
                _widgetRegistry.GetByName(existing.ComponentName),
                WidgetMetadataJson.StoresList(existing.MetadataJson),
                out var metadataJson);
            if (validation != null)
                return BadRequest(new { error = validation.ErrorMessage });

            // Only the metadata column changes; everything else is preserved from the existing item.
            var updated = existing with { MetadataJson = metadataJson };
            var ok = await _service.UpdateItemAsync(updated, ct, request.ExpectedVersionNumber);
            if (!ok)
                return ItemWriteFailed(request.ExpectedVersionNumber);

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to update content zone item metadata.");
            return StatusCode(500, new { error = "Failed to update metadata." });
        }
    }

    /// <summary>
    /// The response for an item update the service rejected after the controller's own version
    /// pre-check passed. With an expected version, that is a concurrent write landing between the
    /// pre-check and the save (the store re-checks the version), so it gets the same 409 as the
    /// pre-check rather than a misleading "not found".
    /// </summary>
    private IActionResult ItemWriteFailed(int? expectedVersionNumber)
        => expectedVersionNumber.HasValue
            ? Conflict(new { error = ContentStore<ContentZoneItemDTO>.StaleVersionMessage })
            : NotFound(new { error = "Item not found." });

    /// <summary>
    /// Registers a widget's routes for an item in a global zone. An item in a page's zone is a draft
    /// until the page is published, so its routes are left to the page publish cascade. Returns the
    /// error response when registration fails, otherwise null.
    /// </summary>
    private async Task<IActionResult?> RegisterGlobalWidgetRoutesAsync(
        string componentName, Guid itemNodeId, Guid zoneNodeId, Guid? parentPageNodeId, bool isActive, CancellationToken ct)
    {
        var pageNodeId = parentPageNodeId ?? await _service.GetParentPageNodeForZoneAsync(zoneNodeId, ct);
        if (pageNodeId.HasValue)
            return null;

        var routeResult = await _routeRegistration.TryRegisterWidgetRoutesAsync(
            componentName, itemNodeId, null, isActive, metadataJson: null, ct);
        return routeResult.Success
            ? null
            : StatusCode(500, new { error = routeResult.ErrorMessage ?? "Failed to register widget route." });
    }
}

/// <summary>
/// Request model for saving a content zone item.
/// </summary>
public class SaveItemRequest
{
    /// <summary>
    /// The zone slot name (used as fallback when ZoneId is not provided).
    /// </summary>
    public string ZoneName { get; set; } = string.Empty;

    /// <summary>
    /// The human-readable slot name (e.g. "Main"). Used with ParentPageNodeId for assignment lookup.
    /// </summary>
    public string? SlotName { get; set; }

    /// <summary>
    /// The page node ID to scope zone creation via assignment lookup.
    /// </summary>
    public Guid? ParentPageNodeId { get; set; }

    /// <summary>
    /// The zone node ID if known (optional, zone will be looked up by assignment or name if not provided).
    /// This is set automatically and never displayed to users.
    /// </summary>
    public Guid? ZoneId { get; set; }

    /// <summary>
    /// The item node ID if updating an existing item.
    /// This is set automatically and never displayed to users.
    /// </summary>
    public Guid? ItemId { get; set; }

    /// <summary>
    /// The name of the ViewComponent to render.
    /// </summary>
    public string ComponentName { get; set; } = string.Empty;

    /// <summary>
    /// JSON-serialized configuration properties for the component.
    /// </summary>
    public string? ComponentPropertiesJson { get; set; }

    /// <summary>
    /// JSON-serialized <c>WidgetMetadata</c> for a new item (e.g. the Add Widget modal's display
    /// mode). Ignored on update.
    /// </summary>
    public string? MetadataJson { get; set; }

    /// <summary>
    /// The item's version number as the client last saw it (updates only). A mismatch fails the
    /// save with the "changed by someone else" message instead of silently overwriting the edit.
    /// </summary>
    public int? ExpectedVersionNumber { get; set; }
}

/// <summary>
/// Request model for updating a content zone item's widget metadata.
/// </summary>
public class UpdateItemMetadataRequest
{
    /// <summary>
    /// JSON-serialized <c>WidgetMetadata</c> (the display mode plus the admin presentation fields).
    /// </summary>
    public string? MetadataJson { get; set; }

    /// <summary>
    /// The item's version number as the client last saw it. A mismatch fails the save with the
    /// "changed by someone else" message instead of silently overwriting the edit.
    /// </summary>
    public int? ExpectedVersionNumber { get; set; }
}

/// <summary>
/// Request model for moving a content zone item.
/// </summary>
public class MoveItemRequest
{
    /// <summary>
    /// The node id of the zone to move the item into (its own zone for a reorder).
    /// </summary>
    public Guid TargetZoneId { get; set; }

    /// <summary>
    /// The 0-based position among the target zone's items, excluding the moved item. Clamped.
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// The moved item's version number as the client last saw it. A mismatch fails the move with
    /// the "changed by someone else" message instead of silently overwriting a concurrent edit.
    /// </summary>
    public int? ExpectedVersionNumber { get; set; }
}
