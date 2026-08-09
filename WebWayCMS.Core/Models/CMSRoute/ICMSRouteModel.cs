using WebWayCMS.Models.CMSRoute;

namespace WebWayCMS.Models.CMSRoute;

public interface ICMSRouteModel
{
    Task<CMSRouteIndexViewModel> GetRouteIndexAsync(CancellationToken ct = default);
    Task<CMSRouteUpsertViewModel?> GetRouteUpsertAsync(Guid? id, CancellationToken ct = default);
    Task<(bool Success, string? ErrorMessage)> SaveRouteUpsertAsync(CMSRouteUpsertViewModel model, CancellationToken ct = default);
    Task<bool> DeleteRouteAsync(Guid id, CancellationToken ct = default);
}
