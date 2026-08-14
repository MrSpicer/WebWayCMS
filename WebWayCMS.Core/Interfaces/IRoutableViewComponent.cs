using WebWayCMS.Data.Models;

namespace WebWayCMS.Interfaces;

public interface IRoutableViewComponent
{
    string ComponentName { get; }

    Task<IReadOnlyList<CMSRouteDTO>> GenerateRoutesAsync(
        string parentRoute, Guid contentZoneItemNodeId, CancellationToken ct);
}
