using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// Content-zone specific operations: item resolution (read-context aware), assignment management,
/// and item writes. Zone CRUD and version history are handled by <c>IContentStore&lt;ContentZoneDTO&gt;</c>;
/// item history by <c>IContentStore&lt;ContentZoneItemDTO&gt;</c>.
/// </summary>
public interface IContentZoneService
{
    // item resolution (read-context aware)
    Task<List<ContentZoneItemDTO>> GetItemsAsync(Guid zoneNodeId, CancellationToken ct = default);

    // zone reads (read-context aware)
    Task<ContentZoneDTO?> GetZoneByNodeAsync(Guid zoneNodeId, CancellationToken ct = default);
    Task<ContentZoneDTO?> GetZoneByNameAsync(string name, CancellationToken ct = default);

    // assignment management
    Task<ContentZoneAssignmentDTO?> GetByPageSlotAsync(Guid pageNodeId, string slotName, CancellationToken ct = default);
    Task<(ContentZoneDTO Zone, ContentZoneAssignmentDTO Assignment)> GetOrCreateByPageSlotAsync(Guid pageNodeId, string slotName, CancellationToken ct = default);
    // nested zones are owned by the zone item (e.g. a Layout widget) that renders them, so they move
    // with it
    Task<ContentZoneAssignmentDTO?> GetByItemSlotAsync(Guid parentItemNodeId, string slotName, CancellationToken ct = default);
    Task<(ContentZoneDTO Zone, ContentZoneAssignmentDTO Assignment)> GetOrCreateByItemSlotAsync(Guid parentItemNodeId, string slotName, CancellationToken ct = default);
    Task<ContentZoneDTO> GetOrCreateByNameAsync(string name, CancellationToken ct = default);
    Task<IEnumerable<ContentZoneAssignmentDTO>> GetAllAssignmentsForPageAsync(Guid pageNodeId, CancellationToken ct = default);
    Task<List<ContentZoneDTO>> GetAllByPageAsync(Guid pageNodeId, CancellationToken ct = default);
    Task<List<ContentZoneDTO>> GetAllByParentZoneAsync(Guid parentZoneNodeId, CancellationToken ct = default);
    Task<HashSet<Guid>> GetZoneNodeIdsWithChildrenAsync(IEnumerable<Guid> zoneNodeIds, CancellationToken ct = default);
    Task<Dictionary<Guid, int>> GetAssignmentCountsByNodeIdAsync(IEnumerable<Guid> nodeIds, CancellationToken ct = default);

    // parent resolution
    Task<Guid?> GetParentPageNodeForZoneAsync(Guid zoneNodeId, CancellationToken ct = default);

    // item writes. expectedVersionNumber detects concurrent edits (the "changed by someone else"
    // check SaveDraftAsync performs); null keeps the read-then-write behaviour.
    Task<ContentZoneItemDTO?> GetItemByNodeIdAsync(Guid itemNodeId, CancellationToken ct = default);
    Task<ContentZoneItemDTO> AddItemAsync(Guid zoneNodeId, ContentZoneItemDTO item, CancellationToken ct = default);
    Task<bool> UpdateItemAsync(ContentZoneItemDTO item, CancellationToken ct = default, int? expectedVersionNumber = null);
    Task<bool> RemoveItemAsync(Guid itemNodeId, CancellationToken ct = default);
    Task<bool> ReorderItemsAsync(Guid zoneNodeId, List<Guid> itemNodeIdsInOrder, CancellationToken ct = default);

    // MoveItemAsync's error message when the item doesn't exist or has been removed (inactive)
    const string ItemNotFoundMessage = "Item not found.";

    // moves an item to a position (0-based, clamped) among the target zone's active items. Cross-zone
    // moves are limited to one page's zone tree and are drafts until the page is published.
    // expectedVersionNumber is the moved item's version as the caller last saw it; a mismatch (someone
    // edited the widget since the page loaded) fails the move instead of silently overwriting.
    Task<(bool Success, string? ErrorMessage)> MoveItemAsync(Guid itemNodeId, Guid targetZoneNodeId, int position, CancellationToken ct = default, int? expectedVersionNumber = null);

    // page draft lifecycle. Items in a page's zones (nested included) are drafts until the page is
    // published; these list, publish, and discard those drafts for one page.
    Task<List<Guid>> GetZoneTreeNodeIdsForPageAsync(Guid pageNodeId, CancellationToken ct = default);
    Task<List<Guid>> GetPendingItemNodeIdsAsync(IEnumerable<Guid> zoneNodeIds, CancellationToken ct = default);
    Task<HashSet<Guid>> GetPagesWithPendingZoneChangesAsync(IEnumerable<Guid> pageNodeIds, CancellationToken ct = default);
    Task<PageZonePublishResult> PublishPageZonesAsync(Guid pageNodeId, CancellationToken ct = default);
    Task<(bool Success, string? ErrorMessage)> DiscardPageZoneDraftsAsync(Guid pageNodeId, CancellationToken ct = default);

    // zone deletion: the zone's assignments, its items, and the nested zones those items own (at any
    // depth). A nested zone still referenced by another assignment keeps its node.
    Task<bool> DeleteZoneAsync(Guid zoneNodeId, CancellationToken ct = default);

    // page deletion cleanup (removes the page's assigned zones and their nested child zones)
    Task DeletePageZonesAsync(Guid pageNodeId, CancellationToken ct = default);
}
