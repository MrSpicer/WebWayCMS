using WebWayCMS.Data.Services;

namespace WebWayCMS.Services.Relations;

/// <summary>
/// The delete cascade's deleter for zone items (<c>contentzoneitems</c>), which have no admin handler:
/// a page or zone delete reaches its items as containment descendants, and each goes through
/// <see cref="IContentZoneService.DeleteItemAsync"/> (its widget routes are swept with it).
/// </summary>
public sealed class ZoneItemDeleter : IContentDeleter
{
    private readonly IContentZoneService _zones;

    public ZoneItemDeleter(IContentZoneService zones)
    {
        _zones = zones ?? throw new ArgumentNullException(nameof(zones));
    }

    public string ContentTypeKey => ContentRelationRegistry.ContentZoneItemContentType;

    public Task<bool> DeleteSelfAsync(Guid nodeId, CancellationToken ct = default)
        => _zones.DeleteItemAsync(nodeId, ct);
}
