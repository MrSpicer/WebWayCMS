using Microsoft.AspNetCore.Http;

namespace WebWayCMS.Models.ContentZone;

/// <summary>A zone owned by a widget on the page, offered by the page zone editor's Unplaced panel when nothing rendered it.</summary>
public sealed record UnplacedZoneCandidate(Guid ZoneNodeId, Guid OwnerItemNodeId, string Label);

/// <summary>
/// What the page zone editor actually rendered in this request — the zones shown in edit mode and the
/// widgets in them — so it can tell which zones owned by the page's widgets went unrendered (stranded by a
/// view change made before content moved with it, a failed slot discovery, or a missing view) and list
/// them in its "Unplaced content" panel instead of letting their content disappear.
/// </summary>
public sealed class ZoneEditRenderLog
{
    /// <summary>The <see cref="HttpContext.Items"/> key holding the request's log.</summary>
    public const string ItemsKey = "ContentZone:EditRenderLog";

    private readonly HashSet<Guid> _zones = [];
    private readonly HashSet<Guid> _items = [];

    /// <summary>The request's log, created on first use.</summary>
    public static ZoneEditRenderLog For(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (httpContext.Items[ItemsKey] is not ZoneEditRenderLog log)
            httpContext.Items[ItemsKey] = log = new ZoneEditRenderLog();
        return log;
    }

    /// <summary>Records a zone rendered in edit mode.</summary>
    public void ZoneRendered(Guid zoneNodeId) => _zones.Add(zoneNodeId);

    /// <summary>Whether the zone was already rendered in edit mode this request.</summary>
    public bool WasRendered(Guid zoneNodeId) => _zones.Contains(zoneNodeId);

    /// <summary>Records a widget rendered inside an edit-mode zone.</summary>
    public void ItemRendered(Guid itemNodeId) => _items.Add(itemNodeId);

    /// <summary>
    /// The next candidates to show: those not rendered and not yet <paramref name="listed"/> whose owner was
    /// rendered (its stranded zones are what the panel is for); when none, any left whose owner was not (an
    /// owner itself sitting in a stranded zone shows once that zone is listed, so this only catches the rest).
    /// Rendering a batch may render more owners, so the editor calls this until it returns nothing.
    /// </summary>
    public IReadOnlyList<UnplacedZoneCandidate> NextUnplaced(IEnumerable<UnplacedZoneCandidate> candidates, ISet<Guid> listed)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(listed);

        var pending = candidates.Where(c => !_zones.Contains(c.ZoneNodeId) && !listed.Contains(c.ZoneNodeId)).ToList();
        var ready = pending.Where(c => _items.Contains(c.OwnerItemNodeId)).ToList();
        return ready.Count > 0 ? ready : pending;
    }
}
