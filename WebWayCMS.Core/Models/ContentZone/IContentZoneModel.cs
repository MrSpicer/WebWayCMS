using WebWayCMS.Data.Models;

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

    /// <summary>
    /// The zones owned by the page's active widgets that hold active widgets, labelled "{widget} › {slot}" —
    /// what the page zone editor's Unplaced panel offers when it did not render one (<see cref="ZoneEditRenderLog"/>).
    /// </summary>
    Task<IReadOnlyList<UnplacedZoneCandidate>> GetUnplacedCandidatesAsync(Guid pageNodeId, CancellationToken ct = default);

    /// <summary>
    /// Saves a zone item (see <c>IContentZoneService.UpdateItemAsync</c>). When the save changes a container's
    /// view, its nested content moves into the new view's zones in the same write.
    /// </summary>
    Task<bool> UpdateItemAsync(ContentZoneItemDTO item, CancellationToken ct = default, int? expectedVersionNumber = null);
}
