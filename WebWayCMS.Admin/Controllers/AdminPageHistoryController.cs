using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Data.Services;
using WebWayCMS.Models.PageHistory;

namespace WebWayCMS.Controllers.Admin;

/// <summary>
/// The page editor's page-set history: every publish of a page with all of its zone items, which an
/// admin can inspect, compare with the current drafts, preview, revert to (as drafts) or delete.
/// </summary>
[Authorize(Roles = "Admin")]
[Route("wadmin/contentzones/page/{pageNodeId:guid}/history")]
public class AdminPageHistoryController : Controller
{
    private const string ViewRoot = "~/Views/AdminPageHistory/";

    private readonly IPageHistoryModel _model;

    public AdminPageHistoryController(IPageHistoryModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        Guid pageNodeId, int skip = 0, int take = IPageHistoryService.DefaultPageSize, CancellationToken ct = default)
    {
        var vm = await _model.GetIndexAsync(pageNodeId, skip, take, ct);
        return vm == null ? NotFound() : View(ViewRoot + "Index.cshtml", vm);
    }

    [HttpGet("{entryId:guid}")]
    public async Task<IActionResult> Entry(Guid pageNodeId, Guid entryId, CancellationToken ct)
    {
        var vm = await _model.GetEntryAsync(pageNodeId, entryId, ct);
        return vm == null ? NotFound() : View(ViewRoot + "Entry.cshtml", vm);
    }

    [HttpGet("{entryId:guid}/compare")]
    public async Task<IActionResult> Compare(Guid pageNodeId, Guid entryId, CancellationToken ct)
    {
        var vm = await _model.GetCompareAsync(pageNodeId, entryId, ct);
        return vm == null ? NotFound() : View(ViewRoot + "Compare.cshtml", vm);
    }

    /// <summary>The entry rendered in a frame (<c>/_preview/{page}/history/{entry}</c>) under a banner.</summary>
    [HttpGet("{entryId:guid}/preview")]
    public async Task<IActionResult> Preview(Guid pageNodeId, Guid entryId, CancellationToken ct)
    {
        var vm = await _model.GetEntryAsync(pageNodeId, entryId, ct);
        return vm == null ? NotFound() : View(ViewRoot + "Preview.cshtml", vm);
    }

    /// <summary>
    /// Reverts the page and its zone items to the entry as drafts, then returns to the page editor to
    /// review and publish. A stale or failed revert goes back to the comparison, re-read.
    /// </summary>
    [HttpPost("{entryId:guid}/revert")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revert(Guid pageNodeId, Guid entryId, [FromForm] string? stateToken, CancellationToken ct)
    {
        var (success, error) = await _model.RevertAsync(pageNodeId, entryId, stateToken ?? string.Empty, ct);
        if (!success)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Compare), new { pageNodeId, entryId });
        }

        TempData["Success"] = "The page was reverted as a draft. Review it, then publish to make it live.";
        return RedirectToAction("PageZones", "AdminContentZone", new { pageNodeId });
    }

    /// <summary>Deletes the history entry only; the versions it captured are kept.</summary>
    [HttpPost("{entryId:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid pageNodeId, Guid entryId, CancellationToken ct)
    {
        if (await _model.DeleteAsync(pageNodeId, entryId, ct))
            TempData["Success"] = "History entry deleted.";
        else
            TempData["Error"] = "History entry not found.";

        return RedirectToAction(nameof(Index), new { pageNodeId });
    }
}
