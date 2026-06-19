using WebWayCMS.Data.Models;

namespace WebWayCMS.Models.ContentZone;

/// <summary>
/// Model interface for content zone operations.
/// </summary>
public interface IContentZoneModel
{
    /// <summary>
    /// Gets the view model for a content zone by name (used for global/fallback zones).
    /// </summary>
    Task<ContentZoneViewModel?> GetViewModelAsync(string contentZoneName, CancellationToken ct = default);

    /// <summary>
    /// Gets or creates the view model for a global content zone by name.
    /// </summary>
    Task<ContentZoneViewModel> GetOrCreateViewModelAsync(string contentZoneName, CancellationToken ct = default);

    /// <summary>
    /// Gets or creates the view model for a page's named slot.
    /// </summary>
    Task<ContentZoneViewModel> GetOrCreateViewModelByPageSlotAsync(Guid pageMasterId, string slotName, CancellationToken ct = default);

    /// <summary>
    /// Gets the view model for a page's named slot without creating it (returns null if not found).
    /// </summary>
    Task<ContentZoneViewModel?> GetViewModelByPageSlotAsync(Guid pageMasterId, string slotName, CancellationToken ct = default);

    /// <summary>
    /// Gets or creates the view model for a parent zone's named slot.
    /// </summary>
    Task<ContentZoneViewModel> GetOrCreateViewModelByZoneSlotAsync(Guid parentZoneId, string slotName, CancellationToken ct = default);

    /// <summary>
    /// Gets the view model for a parent zone's named slot without creating it (returns null if not found).
    /// </summary>
    Task<ContentZoneViewModel?> GetViewModelByZoneSlotAsync(Guid parentZoneId, string slotName, CancellationToken ct = default);

    /// <summary>
    /// Gets a fully-mapped view model for a zone by its database ID.
    /// </summary>
    Task<ContentZoneViewModel?> GetViewModelByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Gets a content zone by ID.
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
    /// Deletes a content zone.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Adds an item to a content zone.
    /// </summary>
    Task<ContentZoneItemDTO> AddItemAsync(Guid zoneId, ContentZoneItemDTO item, CancellationToken ct = default);

    /// <summary>
    /// Updates a zone item.
    /// </summary>
    Task<bool> UpdateItemAsync(ContentZoneItemDTO item, CancellationToken ct = default);

    /// <summary>
    /// Removes an item from a content zone.
    /// </summary>
    Task<bool> RemoveItemAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>
    /// Gets a zone item by ID.
    /// </summary>
    Task<ContentZoneItemDTO?> GetItemByIdAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>
    /// Reorders items within a zone.
    /// </summary>
    Task<bool> ReorderItemsAsync(Guid zoneId, List<Guid> itemIdsInOrder, CancellationToken ct = default);

    /// <summary>
    /// Gets all versions of a content zone by master ID.
    /// </summary>
    Task<List<ContentZoneDTO>> GetAllVersionsAsync(Guid masterId, CancellationToken ct = default);

    /// <summary>
    /// Gets all versions of a content zone item by master ID.
    /// </summary>
    Task<List<ContentZoneItemDTO>> GetAllItemVersionsAsync(Guid itemMasterId, CancellationToken ct = default);
}
