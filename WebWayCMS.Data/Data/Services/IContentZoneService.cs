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
    Task<ContentZoneAssignmentDTO?> GetByZoneSlotAsync(Guid parentZoneNodeId, string slotName, CancellationToken ct = default);
    Task<(ContentZoneDTO Zone, ContentZoneAssignmentDTO Assignment)> GetOrCreateByZoneSlotAsync(Guid parentZoneNodeId, string slotName, CancellationToken ct = default);
    Task<ContentZoneDTO> GetOrCreateByNameAsync(string name, CancellationToken ct = default);
    Task<IEnumerable<ContentZoneAssignmentDTO>> GetAllAssignmentsForPageAsync(Guid pageNodeId, CancellationToken ct = default);
    Task<List<ContentZoneDTO>> GetAllByPageAsync(Guid pageNodeId, CancellationToken ct = default);
    Task<List<ContentZoneDTO>> GetAllByParentZoneAsync(Guid parentZoneNodeId, CancellationToken ct = default);
    Task<HashSet<Guid>> GetZoneNodeIdsWithChildrenAsync(IEnumerable<Guid> zoneNodeIds, CancellationToken ct = default);
    Task<Dictionary<Guid, int>> GetAssignmentCountsByNodeIdAsync(IEnumerable<Guid> nodeIds, CancellationToken ct = default);

    // parent resolution
    Task<Guid?> GetParentPageNodeForZoneAsync(Guid zoneNodeId, CancellationToken ct = default);

    // item writes
    Task<ContentZoneItemDTO?> GetItemByNodeIdAsync(Guid itemNodeId, CancellationToken ct = default);
    Task<ContentZoneItemDTO> AddItemAsync(Guid zoneNodeId, ContentZoneItemDTO item, CancellationToken ct = default);
    Task<bool> UpdateItemAsync(ContentZoneItemDTO item, CancellationToken ct = default);
    Task<bool> RemoveItemAsync(Guid itemNodeId, CancellationToken ct = default);
    Task<bool> ReorderItemsAsync(Guid zoneNodeId, List<Guid> itemNodeIdsInOrder, CancellationToken ct = default);

    // zone deletion (items + assignments + zone)
    Task<bool> DeleteZoneAsync(Guid zoneNodeId, CancellationToken ct = default);
}
