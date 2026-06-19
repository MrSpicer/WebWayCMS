using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebWayCMS.ContentZones;
using WebWayCMS.Models.ContentZone;

namespace WebWayCMS.Controllers.Admin;

[Authorize(Roles = "Admin")]
[Route("admin/contentzones")]
public class AdminContentZoneController : Controller
{
    private readonly IContentZoneModel _model;
    private readonly IContentZoneComponentRegistry _registry;

    public AdminContentZoneController(IContentZoneModel model, IContentZoneComponentRegistry registry)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    [HttpGet("zone-edit/{id:guid}")]
    public async Task<IActionResult> ZoneEdit(Guid id, CancellationToken ct)
    {
        var zone = await _model.GetViewModelByIdAsync(id, ct);
        if (zone == null) return NotFound();

        ViewData["ZoneId"] = id;
        return View("~/Views/AdminContentZone/ZoneEdit.cshtml", zone);
    }
}
