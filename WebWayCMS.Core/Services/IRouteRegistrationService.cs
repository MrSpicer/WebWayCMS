using WebWayCMS.Attributes;
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
        string parentDefaultsJson, Guid parentPageNodeId, WidgetDisplayMode displayMode, CancellationToken ct = default);

    /// <summary>
    /// (Re)registers the routes of a routable widget's zone item under its page, or removes them when the
    /// item is inactive. <paramref name="metadataJson"/> is the item's stored metadata; its display mode
    /// (honoured only for list-capable widgets) is passed to the widget's route generation.
    /// </summary>
    Task<(bool Success, string? ErrorMessage)> TryRegisterWidgetRoutesAsync(
        string componentName, Guid contentZoneItemNodeId, Guid? parentPageNodeId,
        bool isActive, string? metadataJson, CancellationToken ct = default);
}
