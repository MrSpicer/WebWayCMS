using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

public sealed class CMSRouteMatchResult
{
    public CMSRouteDTO Route { get; init; } = new();
    public Dictionary<string, string> RouteValues { get; init; } = new();
}
