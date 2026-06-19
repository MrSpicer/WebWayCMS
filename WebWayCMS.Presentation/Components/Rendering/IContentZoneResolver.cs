using WebWayCMS.Data.Models;
using WebWayCMS.Models.ContentZone;

namespace WebWayCMS.Presentation.Rendering;

/// <summary>
/// Resolves a content zone view model for a slot, mirroring the resolution rules of the
/// legacy <c>ContentZoneViewComponent</c> (direct id, page slot, nested zone slot, or
/// global name lookup). Kept as a plain service so the branching is unit-testable without
/// rendering a component.
/// </summary>
public interface IContentZoneResolver
{
    /// <summary>
    /// Resolves (and, for page/zone slots, lazily creates) the zone view model for the given
    /// slot within the current rendering context.
    /// </summary>
    Task<ContentZoneViewModel> ResolveAsync(
        string? zoneName,
        bool isGlobal,
        Guid? zoneId,
        PageDTO? page,
        Guid? parentZoneId,
        CancellationToken ct = default);
}
