using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// Service interface for ContentZone-specific operations beyond generic CRUD.
/// </summary>
public interface IContentZoneService
{
    /// <summary>
    /// Gets a content zone by its unique name, including all active items.
    /// </summary>
    Task<ContentZoneDTO?> GetByNameAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Gets a content zone by ID, including all items.
    /// </summary>
    Task<ContentZoneDTO?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Gets all content zones.
    /// </summary>
    Task<List<ContentZoneDTO>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates a new content zone.
    /// </summary>
    Task<ContentZoneDTO> CreateAsync(ContentZoneDTO zone, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing content zone.
    /// </summary>
    Task<bool> UpdateAsync(ContentZoneDTO zone, CancellationToken ct = default);

    /// <summary>
    /// Deletes a content zone and all its items.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Adds an item to a content zone.
    /// </summary>
    Task<ContentZoneItemDTO> AddItemAsync(Guid zoneId, ContentZoneItemDTO item, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing zone item.
    /// </summary>
    Task<bool> UpdateItemAsync(ContentZoneItemDTO item, CancellationToken ct = default);

    /// <summary>
    /// Removes an item from a content zone.
    /// </summary>
    Task<bool> RemoveItemAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>
    /// Gets an item by ID.
    /// </summary>
    Task<ContentZoneItemDTO?> GetItemByIdAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>
    /// Reorders items within a zone.
    /// </summary>
    Task<bool> ReorderItemsAsync(Guid zoneId, List<Guid> itemIdsInOrder, CancellationToken ct = default);

    /// <summary>
    /// Gets the assignment for a page's named slot, if it exists.
    /// </summary>
    Task<ContentZoneAssignmentDTO?> GetByPageSlotAsync(Guid pageMasterId, string slotName, CancellationToken ct = default);

    /// <summary>
    /// Gets or creates a content zone for a page's named slot. Transaction-safe against concurrent first renders.
    /// </summary>
    Task<(ContentZoneDTO Zone, ContentZoneAssignmentDTO Assignment)> GetOrCreateByPageSlotAsync(Guid pageMasterId, string slotName, CancellationToken ct = default);

    /// <summary>
    /// Gets the assignment for a parent zone's named slot, if it exists.
    /// </summary>
    Task<ContentZoneAssignmentDTO?> GetByZoneSlotAsync(Guid parentZoneId, string slotName, CancellationToken ct = default);

    /// <summary>
    /// Gets or creates a content zone for a parent zone's named slot. Transaction-safe against concurrent first renders.
    /// </summary>
    Task<(ContentZoneDTO Zone, ContentZoneAssignmentDTO Assignment)> GetOrCreateByZoneSlotAsync(Guid parentZoneId, string slotName, CancellationToken ct = default);

    /// <summary>
    /// Gets or creates a global content zone by name. Transaction-safe against concurrent first renders.
    /// </summary>
    Task<ContentZoneDTO> GetOrCreateByNameAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Gets all assignments for a page's slots.
    /// </summary>
    Task<IEnumerable<ContentZoneAssignmentDTO>> GetAllAssignmentsForPageAsync(Guid pageMasterId, CancellationToken ct = default);

    /// <summary>
    /// Gets all content zones assigned to a specific page.
    /// </summary>
    Task<List<ContentZoneDTO>> GetAllByPageAsync(Guid pageMasterId, CancellationToken ct = default);

    /// <summary>
    /// Gets all content zones assigned as children of a specific parent zone.
    /// </summary>
    Task<List<ContentZoneDTO>> GetAllByParentZoneAsync(Guid parentZoneId, CancellationToken ct = default);

    /// <summary>
    /// Returns the set of zone IDs (from the provided list) that have at least one child zone assigned.
    /// </summary>
    Task<HashSet<Guid>> GetZoneIdsWithChildrenAsync(IEnumerable<Guid> zoneIds, CancellationToken ct = default);

    /// <summary>
    /// Gets all versions of a content zone by master ID.
    /// </summary>
    Task<List<ContentZoneDTO>> GetAllVersionsAsync(Guid masterId, CancellationToken ct = default);

    /// <summary>
    /// Gets all versions of a content zone item by master ID.
    /// </summary>
    Task<List<ContentZoneItemDTO>> GetAllItemVersionsAsync(Guid itemMasterId, CancellationToken ct = default);

    /// <summary>
    /// Returns the number of assignments per zone MasterId.
    /// </summary>
    Task<Dictionary<Guid, int>> GetAssignmentCountsByMasterIdAsync(IEnumerable<Guid> masterIds, CancellationToken ct = default);
}