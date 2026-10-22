using WebWayCMS.Attributes;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Interfaces;

public interface IRoutableViewComponent
{
    string ComponentName { get; }

    /// <summary>
    /// The routes a zone item of this widget owns under its page's route. <paramref name="displayMode"/>
    /// is the item's effective display mode (a stored <see cref="WidgetDisplayMode.List"/> counts only
    /// when the widget supports lists), so a widget can register no routes for its list items — a list
    /// that claimed <c>{slug}</c> would answer every unknown URL under its page.
    /// </summary>
    Task<IReadOnlyList<CMSRouteDTO>> GenerateRoutesAsync(
        string parentRoute, Guid contentZoneItemNodeId, WidgetDisplayMode displayMode, CancellationToken ct);
}
