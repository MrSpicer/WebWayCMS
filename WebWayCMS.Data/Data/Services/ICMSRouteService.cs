using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

public interface ICMSRouteService
{
    Task<CMSRouteMatchResult?> MatchRouteAsync(string path, CancellationToken ct = default);

    Task<List<CMSRouteDTO>> GetActiveRoutesAsync(CancellationToken ct = default);

    Task<List<CMSRouteDTO>> GetAllRoutesAsync(CancellationToken ct = default);

    Task<List<CMSRouteDTO>> GetByOwningContentAsync(Guid owningContentNodeId, CancellationToken ct = default);

    Task<CMSRouteDTO?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<bool> IsPatternAvailableAsync(string pattern, Guid? excludeNodeId = null, Guid? excludeRouteId = null, CancellationToken ct = default);

    Task<(bool Success, string? ErrorMessage, CMSRouteDTO? Route)> UpsertAsync(CMSRouteDTO route, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<bool> DeleteByOwningContentAsync(Guid owningContentNodeId, CancellationToken ct = default);

    /// <summary>
    /// Removes every row owned by any of <paramref name="owningContentNodeIds"/> in one query (and one save
    /// when there is something to remove); returns how many rows went.
    /// </summary>
    Task<int> DeleteByOwningContentsAsync(IReadOnlyCollection<Guid> owningContentNodeIds, CancellationToken ct = default);

    /// <summary>Removes the projected rows of one route item.</summary>
    Task<bool> DeleteByRouteNodeAsync(Guid routeNodeId, CancellationToken ct = default);
}

public sealed class CMSRouteMatchResult
{
    public CMSRouteDTO Route { get; init; } = new();
    public Dictionary<string, string> RouteValues { get; init; } = new();
}
