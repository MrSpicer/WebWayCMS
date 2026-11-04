namespace WebWayCMS.Data.Services;

/// <summary>
/// Route patterns a CMS route may not take because an admin screen already answers them: in admin mode,
/// <c>/wadmin/{contentType}</c> is the admin list for that content type, so a CMS page at
/// <c>/wadmin/pages</c> would never be reached. <see cref="CMSRouteService.UpsertAsync"/> refuses such a pattern
/// with <see cref="GetConflict"/>'s message. A rendering-only host reserves nothing.
/// </summary>
public interface IAdminRouteReservations
{
    /// <summary>The refusal message for <paramref name="normalizedPattern"/>, or <c>null</c> when it is free.</summary>
    string? GetConflict(string normalizedPattern);
}
