using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
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

    public ContentZoneApiController(IContentZoneService service, IRouteRegistrationService routeRegistration)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _routeRegistration = routeRegistration ?? throw new ArgumentNullException(nameof(routeRegistration));
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

            // Create or update the item
            if (request.ItemId.HasValue && request.ItemId.Value != Guid.Empty)
            {
                // Update existing item — service preserves Ordinal and ContentZoneNodeId from existing record
                var item = new ContentZoneItemDTO
                {
                    Version = new ContentVersion { Node = new ContentNode { Id = request.ItemId.Value } },
                    ContentZoneNodeId = zoneNodeId,
                    ComponentName = request.ComponentName,
                    ComponentPropertiesJson = request.ComponentPropertiesJson ?? "{}",
                    IsActive = true
                };

                var updated = await _service.UpdateItemAsync(item, ct);
                if (!updated)
                    return NotFound(new { error = "Item not found." });

                var existingItem = await _service.GetItemByNodeIdAsync(request.ItemId.Value, ct);
                if (existingItem != null)
                {
                    var pageNodeId = request.ParentPageNodeId
                        ?? await _service.GetParentPageNodeForZoneAsync(zoneNodeId, ct);
                    await _routeRegistration.TryRegisterWidgetRoutesAsync(
                        item.ComponentName, existingItem.Version.Node.Id,
                        pageNodeId, item.IsActive, ct);
                }

                return Ok(new { success = true, itemId = request.ItemId.Value, zoneId = zoneNodeId });
            }
            else
            {
                // Create new item - ID is auto-generated
                var item = new ContentZoneItemDTO
                {
                    ContentZoneNodeId = zoneNodeId,
                    ComponentName = request.ComponentName,
                    ComponentPropertiesJson = request.ComponentPropertiesJson ?? "{}",
                    IsActive = true
                };

                var createdItem = await _service.AddItemAsync(zoneNodeId, item, ct);

                var pageNodeId = request.ParentPageNodeId
                    ?? await _service.GetParentPageNodeForZoneAsync(zoneNodeId, ct);
                await _routeRegistration.TryRegisterWidgetRoutesAsync(
                    item.ComponentName, createdItem.Version.Node!.Id,
                    pageNodeId, createdItem.IsActive, ct);

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
                var pageNodeId = await _service.GetParentPageNodeForZoneAsync(item.ContentZoneNodeId, ct);
                await _routeRegistration.TryRegisterWidgetRoutesAsync(
                    item.ComponentName, item.Version.Node.Id, pageNodeId, false, ct);
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
            ordinal = item.Ordinal,
            isActive = item.IsActive
        });
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
}
