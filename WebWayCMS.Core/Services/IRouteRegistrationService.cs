using WebWayCMS.Data.Models;
using WebWayCMS.Interfaces;

namespace WebWayCMS.Services;

public interface IRouteRegistrationService
{
    Task RegisterContentRoutesAsync(
        IRoutableContent content, string routePattern, string controllerName,
        Guid contentNodeId, CancellationToken ct = default);

    Task UnregisterContentRoutesAsync(Guid contentNodeId, CancellationToken ct = default);

    Task RegisterWidgetRoutesAsync(
        IRoutableViewComponent widget, Guid contentZoneItemNodeId, string parentRoute,
        string parentDefaultsJson, Guid parentPageNodeId, CancellationToken ct = default);

    Task TryRegisterWidgetRoutesAsync(
        string componentName, Guid contentZoneItemNodeId, Guid? parentPageNodeId,
        bool isActive, CancellationToken ct = default);
}
