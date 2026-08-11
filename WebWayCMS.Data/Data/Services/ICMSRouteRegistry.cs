using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

public interface ICMSRouteRegistry
{
    IReadOnlyList<CMSRouteDTO> GetActiveRoutes();
    void Invalidate();
}
