using WebWayCMS.Data.Models;

namespace WebWayCMS.Interfaces;

public interface IRoutableContent
{
    string RouteContentType { get; }

    Task<IReadOnlyList<CMSRouteDTO>> GetRoutesAsync(Guid contentNodeId, CancellationToken ct);
}
