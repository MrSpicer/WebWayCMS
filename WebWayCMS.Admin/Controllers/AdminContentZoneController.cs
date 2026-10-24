using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using WebWayCMS.ContentZones;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.ContentZone;

namespace WebWayCMS.Controllers.Admin;

[Authorize(Roles = "Admin")]
[Route("wadmin/contentzones")]
public class AdminContentZoneController : Controller
{
    private readonly IContentZoneModel _model;
    private readonly IWidgetRegistry _registry;
    private readonly IContentStore<PageDTO> _pageStore;
    private readonly IPageHistoryService _pageHistory;

    public AdminContentZoneController(
        IContentZoneModel model,
        IWidgetRegistry registry,
        IContentStore<PageDTO> pageStore,
        IPageHistoryService pageHistory)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _pageStore = pageStore ?? throw new ArgumentNullException(nameof(pageStore));
        _pageHistory = pageHistory ?? throw new ArgumentNullException(nameof(pageHistory));
    }

    /// <summary>
    /// Edits one zone. The editor writes current drafts (and checks each write against the version it
    /// rendered), so it reads drafts too — a page-owned zone's pending items would otherwise render
    /// their published version and every save would be rejected as stale.
    /// </summary>
    [HttpGet("zone-edit/{id:guid}")]
    public async Task<IActionResult> ZoneEdit(Guid id, CancellationToken ct)
    {
        PreviewConstants.MarkDraftRequest(HttpContext);

        var zone = await _model.GetViewModelByIdAsync(id, ct);
        if (zone == null) return NotFound();

        ViewData["ZoneId"] = id;
        return View("~/Views/AdminContentZone/ZoneEdit.cshtml", zone);
    }

    /// <summary>
    /// The page zone editor: every zone assigned to the page, editable on one screen. Zone items in a
    /// page's zones are drafts until the page is published, so this request reads drafts.
    /// </summary>
    [HttpGet("page/{pageNodeId:guid}")]
    public async Task<IActionResult> PageZones(Guid pageNodeId, CancellationToken ct)
    {
        var page = await _pageStore.GetCurrentDraftAsync(pageNodeId, ct);
        if (page == null) return NotFound();

        PreviewConstants.MarkDraftRequest(HttpContext);

        var versions = await _pageStore.GetAllVersionsAsync(pageNodeId, ct);
        var isPublished = versions.Any(v => v.Version.State == ContentVersionState.Published);
        var pending = (await _pageHistory.GetPagesWithUnpublishedChangesAsync([pageNodeId], ct)).Contains(pageNodeId);

        var vm = new PageZonesEditViewModel
        {
            PageNodeId = pageNodeId,
            PageTitle = page.Version.Title,
            IsPublished = isPublished,
            HasPendingChanges = pending,
            // Discard returns the page to what is live, so it is offered only for a live page with changes.
            HasPendingZoneChanges = isPublished && pending,
            Slots = await _model.GetPageZoneSlotsAsync(pageNodeId, ct),
            UnplacedCandidates = await _model.GetUnplacedCandidatesAsync(pageNodeId, ct)
        };

        return View("~/Views/AdminContentZone/PageZonesEdit.cshtml", vm);
    }

    /// <summary>
    /// Returns the page and its zones to what is live, as new drafts recorded in page history (so the
    /// discard itself can be undone from there).
    /// </summary>
    [HttpPost("page/{pageNodeId:guid}/discard")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DiscardPageZones(Guid pageNodeId, CancellationToken ct)
    {
        var (success, errorMessage) = await _pageHistory.DiscardUnpublishedChangesAsync(pageNodeId, ct);
        if (!success)
            TempData["Error"] = errorMessage;

        return RedirectToAction(nameof(PageZones), new { pageNodeId });
    }
}
