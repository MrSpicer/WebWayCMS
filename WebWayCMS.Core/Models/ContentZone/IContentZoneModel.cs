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
    Task<ContentZoneViewModel> GetOrCreateViewModelByZoneSlotAsync(Guid parentZoneNodeId, string slotName, CancellationToken ct = default);
    Task<ContentZoneViewModel?> GetViewModelByZoneSlotAsync(Guid parentZoneNodeId, string slotName, CancellationToken ct = default);
    Task<ContentZoneViewModel?> GetViewModelByIdAsync(Guid nodeId, CancellationToken ct = default);
}
