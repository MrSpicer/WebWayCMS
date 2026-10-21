namespace WebWayCMS.Models.ContentZone;

/// <summary>
/// Model interface for content zone operations used by the presentation layer (view components).
/// </summary>
public interface IContentZoneModel
{
    Task<ContentZoneViewModel?> GetViewModelAsync(string contentZoneName, CancellationToken ct = default);
    Task<ContentZoneViewModel> GetOrCreateViewModelAsync(string contentZoneName, CancellationToken ct = default);
    Task<ContentZoneViewModel> GetOrCreateViewModelByPageSlotAsync(Guid pageNodeId, string slotName, CancellationToken ct = default);
    Task<ContentZoneViewModel?> GetViewModelByPageSlotAsync(Guid pageNodeId, string slotName, CancellationToken ct = default);
    Task<ContentZoneViewModel> GetOrCreateViewModelByItemSlotAsync(Guid parentItemNodeId, string slotName, CancellationToken ct = default);
    Task<ContentZoneViewModel?> GetViewModelByItemSlotAsync(Guid parentItemNodeId, string slotName, CancellationToken ct = default);
    Task<ContentZoneViewModel?> GetViewModelByIdAsync(Guid nodeId, CancellationToken ct = default);

    /// <summary>The zone slots assigned directly to a page, ordered by slot name.</summary>
    Task<IReadOnlyList<PageZoneSlot>> GetPageZoneSlotsAsync(Guid pageNodeId, CancellationToken ct = default);

    /// <summary>The page whose zone tree contains the zone, or null for a global zone.</summary>
    Task<Guid?> GetParentPageNodeForZoneAsync(Guid zoneNodeId, CancellationToken ct = default);
}
