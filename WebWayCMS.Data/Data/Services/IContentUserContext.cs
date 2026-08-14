namespace WebWayCMS.Data.Services;

/// <summary>
/// Resolves the id of the currently authenticated user, or null when unauthenticated (or when no
/// identity host is wired up). Kept minimal so the Data tier has no dependency on ASP.NET Identity.
/// </summary>
public interface IContentUserContext
{
    Guid? CurrentUserId { get; }
}
