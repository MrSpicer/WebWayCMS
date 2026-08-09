using WebWayCMS.Data.Models;
using WebWayCMS.Interfaces;

namespace WebWayCMS.Services;

public interface IRouteRegistrationService
{
    Task RegisterContentRoutesAsync(
        IRoutableContent content, string routePattern, string controllerName, object configuration,
        Guid? viewModelId, Guid? viewModelMasterId, bool isPublished, CancellationToken ct = default);

    Task UnregisterContentRoutesAsync(Guid contentMasterId, CancellationToken ct = default);

    Task RegisterWidgetRoutesAsync(
        IRoutableViewComponent widget, Guid contentZoneItemMasterId, string parentRoute,
        string parentDefaultsJson, Guid parentPageMasterId, bool isActive, CancellationToken ct = default);

    Task TryRegisterWidgetRoutesAsync(
        string componentName, Guid contentZoneItemMasterId, Guid? parentPageMasterId,
        bool isActive, CancellationToken ct = default);
}
