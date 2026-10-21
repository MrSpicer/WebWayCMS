using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// Outcome of publishing a page's pending zone-item drafts: the items that went live (so the caller
/// can register their widget routes) and an error per item that could not be published.
/// </summary>
public sealed record PageZonePublishResult(
    IReadOnlyList<ContentZoneItemDTO> Published,
    IReadOnlyList<string> Errors);
