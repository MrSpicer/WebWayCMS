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
    // the slot names of the item's own zones that hold any current-draft widget (removed ones included)
    Task<List<string>> GetOwnedSlotsWithItemsAsync(Guid itemNodeId, CancellationToken ct = default);
    // every widget that has ever sat (any version) in a zone nested under the container, at any depth
    Task<List<Guid>> GetDescendantItemNodeIdsAsync(Guid containerItemNodeId, CancellationToken ct = default);
    // the zones owned by the page's active widgets that hold at least one active current-draft widget
    Task<List<ItemOwnedZone>> GetItemOwnedZonesForPageAsync(Guid pageNodeId, CancellationToken ct = default);

    // parent resolution
    Task<Guid?> GetParentPageNodeForZoneAsync(Guid zoneNodeId, CancellationToken ct = default);

    // item writes. expectedVersionNumber detects concurrent edits (the "changed by someone else"
    // check SaveDraftAsync performs); null keeps the read-then-write behaviour.
    Task<ContentZoneItemDTO?> GetItemByNodeIdAsync(Guid itemNodeId, CancellationToken ct = default);
    Task<ContentZoneItemDTO> AddItemAsync(Guid zoneNodeId, ContentZoneItemDTO item, CancellationToken ct = default);
    Task<bool> UpdateItemAsync(ContentZoneItemDTO item, CancellationToken ct = default, int? expectedVersionNumber = null);
    // an update that changes a container's view: its nested content moves per the plan in the same
    // transaction, recorded as one page-history entry (the plan's note)
    Task<bool> UpdateItemAsync(ContentZoneItemDTO item, SlotRemapPlan remap, int? expectedVersionNumber, CancellationToken ct = default);
    // never a delete: the item gets an inactive version (a draft on a page, published in a global zone),
    // so its history stays and a restore or a page revert can bring it back
    Task<bool> RemoveItemAsync(Guid itemNodeId, CancellationToken ct = default);
    Task<bool> ReorderItemsAsync(Guid zoneNodeId, List<Guid> itemNodeIdsInOrder, CancellationToken ct = default);

    // MoveItemAsync's error message when the item doesn't exist or has been removed (inactive)
    const string ItemNotFoundMessage = "Item not found.";

    // moves an item to a position (0-based, clamped) among the target zone's active items. Cross-zone
    // moves are limited to one page's zone tree and are drafts until the page is published.
    // expectedVersionNumber is the moved item's version as the caller last saw it; a mismatch (someone
    // edited the widget since the page loaded) fails the move instead of silently overwriting.
    Task<(bool Success, string? ErrorMessage)> MoveItemAsync(Guid itemNodeId, Guid targetZoneNodeId, int position, CancellationToken ct = default, int? expectedVersionNumber = null);

    // RestoreItemVersionAsync's error messages
    const string VersionNotFoundMessage = "Version not found.";
    const string RestoreZoneMissingMessage = "The widget's original zone no longer exists on this page; nothing was restored.";

    // one-step restore of an item version: its content, metadata (metadataJson when supplied — the
    // caller's normalized copy — else the version's own), active state, zone and ordinal. It goes back
    // into its old zone at its old ordinal, the siblings renumbered; refused when that zone is gone,
    // belongs to another page, or sits inside the item itself. A draft until the page is published
    // (a global zone's item publishes now). expectedVersionNumber is the item's current version as the
    // caller saw it.
    Task<(bool Success, string? ErrorMessage)> RestoreItemVersionAsync(Guid versionId, string? metadataJson, int? expectedVersionNumber, CancellationToken ct = default);
    // a restore that changes a container's view, moving its nested content per the plan (atomically)
    Task<(bool Success, string? ErrorMessage)> RestoreItemVersionAsync(Guid versionId, string? metadataJson, int? expectedVersionNumber, SlotRemapPlan remap, CancellationToken ct = default);

    // page draft lifecycle. Items in a page's zones (nested included) are drafts until the page is
    // published; these list and publish those drafts for one page (a discard is a page-history revert to
    // what is live: IPageHistoryService.DiscardUnpublishedChangesAsync). Pending changes compare content,
    // so a draft equal to what is live is not pending.
    Task<List<Guid>> GetZoneTreeNodeIdsForPageAsync(Guid pageNodeId, CancellationToken ct = default);
    Task<List<Guid>> GetPendingItemNodeIdsAsync(IEnumerable<Guid> zoneNodeIds, CancellationToken ct = default);
    Task<HashSet<Guid>> GetPagesWithPendingZoneChangesAsync(IEnumerable<Guid> pageNodeIds, CancellationToken ct = default);
    Task<PageZonePublishResult> PublishPageZonesAsync(Guid pageNodeId, CancellationToken ct = default);
    // Publishes only the listed items among the page's pending zone drafts; every other draft stays pending.
    // A listed item that is not pending (already live, or outside the page's zone tree) is left alone.
    Task<PageZonePublishResult> PublishPageZoneItemsAsync(Guid pageNodeId, IReadOnlyCollection<Guid> itemNodeIds, CancellationToken ct = default);

    // zone deletion: the zone's assignments, its items, and the nested zones those items own (at any
    // depth). A nested zone still referenced by another assignment keeps its node.
    Task<bool> DeleteZoneAsync(Guid zoneNodeId, CancellationToken ct = default);

    // page deletion cleanup (removes the page's assigned zones and their nested child zones)
    Task DeletePageZonesAsync(Guid pageNodeId, CancellationToken ct = default);
}
