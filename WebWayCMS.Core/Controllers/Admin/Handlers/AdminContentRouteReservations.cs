using Microsoft.Extensions.DependencyInjection;

using WebWayCMS.Data.Services;

namespace WebWayCMS.Controllers.Admin.Handlers;

/// <summary>
/// Admin-mode <see cref="IAdminRouteReservations"/>: a CMS route whose first segment under <c>/wadmin</c> is a
/// registered admin content type (e.g. <c>/wadmin/pages</c>, <c>/wadmin/pages/{x}</c>) is refused, because
/// <c>AdminContentController</c> answers those URLs first (see <see cref="AdminContentTypeConstraint"/>).
/// The registry is resolved on first use: it depends on every handler, and the page handler depends on the
/// route service that depends on this, so constructor injection would be a cycle.
/// </summary>
public sealed class AdminContentRouteReservations : IAdminRouteReservations
{
    private const string AdminPrefix = "/wadmin/";

    private readonly Lazy<IAdminHandlerRegistry> _registry;

    public AdminContentRouteReservations(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _registry = new Lazy<IAdminHandlerRegistry>(services.GetRequiredService<IAdminHandlerRegistry>);
    }

    public string? GetConflict(string normalizedPattern)
    {
        if (!normalizedPattern.StartsWith(AdminPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var segment = normalizedPattern[AdminPrefix.Length..].Split('/')[0];
        return _registry.Value.GetHandler(segment) is null
            ? null
            : $"Route pattern '{normalizedPattern}' collides with the admin screen for '{segment}' (/wadmin/{segment}). Use another path.";
    }
}
