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

    Task<CMSRouteDTO> UpsertAsync(CMSRouteDTO route, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<bool> DeleteByOwningContentAsync(Guid owningContentNodeId, CancellationToken ct = default);
}

public sealed class CMSRouteMatchResult
{
    public CMSRouteDTO Route { get; init; } = new();
    public Dictionary<string, string> RouteValues { get; init; } = new();
}
