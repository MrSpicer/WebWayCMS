using WebWayCMS.Data.Models;
using WebWayCMS.Interfaces;

namespace WebWayCMS.Services;

public interface IRouteRegistrationService
{
    Task<(bool Success, string? ErrorMessage)> RegisterContentRoutesAsync(
        IRoutableContent content, string routePattern, string controllerName,
        Guid contentNodeId, string? navigationName = null, CancellationToken ct = default);

    Task UnregisterContentRoutesAsync(Guid contentNodeId, CancellationToken ct = default);

    Task<(bool Success, string? ErrorMessage)> RegisterWidgetRoutesAsync(
        IRoutableViewComponent widget, Guid contentZoneItemNodeId, string parentRoute,
        string parentDefaultsJson, Guid parentPageNodeId, CancellationToken ct = default);

    Task<(bool Success, string? ErrorMessage)> TryRegisterWidgetRoutesAsync(
        string componentName, Guid contentZoneItemNodeId, Guid? parentPageNodeId,
        bool isActive, CancellationToken ct = default);
}
