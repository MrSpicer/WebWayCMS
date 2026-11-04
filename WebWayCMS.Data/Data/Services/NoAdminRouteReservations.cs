namespace WebWayCMS.Data.Services;

/// <summary>Reserves nothing (rendering-only hosts, and the default before admin services replace it).</summary>
public sealed class NoAdminRouteReservations : IAdminRouteReservations
{
    public string? GetConflict(string normalizedPattern) => null;
}
