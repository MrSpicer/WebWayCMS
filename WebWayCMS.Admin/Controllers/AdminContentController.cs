using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.Shared;

namespace WebWayCMS.Controllers.Admin;

[Authorize(Roles = "Admin")]
[Route("wadmin")]
public class AdminContentController : Controller
{
    private static readonly Serilog.ILogger Logger = Serilog.Log.ForContext<AdminContentController>();

    private readonly IAdminHandlerRegistry _registry;
    private readonly ICMSRouteService _routeService;

    public AdminContentController(IAdminHandlerRegistry registry, ICMSRouteService routeService)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private IActionResult HandlerNotFound(string contentType) =>
        NotFound($"No admin handler registered for content type '{contentType}'.");

    private bool HasWriteAccess(string[]? writeRoles)
    {
        var granted = writeRoles == null
            ? User.IsInRole("Admin")
            : writeRoles.Any(r => User.IsInRole(r));

        if (!granted)
            Logger.Warning("Denied admin write access to {Path}", Request.Path);

        return granted;
    }

    // ─── Top-level CRUD ───────────────────────────────────────────────────────

    [HttpGet("{contentType}")]
    public async Task<IActionResult> Index(string contentType, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        var vm = await handler.GetIndexViewModelAsync(Request.Query, ct);
        return View(handler.IndexViewPath, vm);
    }

    [HttpGet("{contentType}/create")]
    [ActionName("Create")]
    public async Task<IActionResult> Create(string contentType, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        var vm = await handler.GetUpsertViewModelAsync(null, Request.Query, ct);
        return View(handler.UpsertViewPath, vm ?? handler.CreateEmptyUpsertViewModel());
    }

    [HttpGet("{contentType}/edit/{id:guid}")]
    public async Task<IActionResult> Edit(string contentType, Guid id, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        var vm = await handler.GetUpsertViewModelAsync(id, Request.Query, ct);
        if (vm == null) return NotFound();
        return View(handler.UpsertViewPath, vm);
    }

    [HttpPost("{contentType}/edit/{id:guid?}")]
    [ActionName("Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPost(string contentType, Guid? id, CancellationToken ct, string? submitAction = null)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        if (!HasWriteAccess(handler.WriteRoles)) return Forbid();

        var model = handler.CreateEmptyUpsertViewModel();
        await TryUpdateModelAsync(model, model.GetType(), prefix: "");

        if (!ModelState.IsValid)
            return View(handler.UpsertViewPath, model);

        var result = await handler.SaveUpsertAsync(model, ct);
        if (!result.Success)
        {
            ModelState.AddModelError(
                result.ErrorField ?? string.Empty,
                result.ErrorMessage ?? "An error occurred.");
            return View(handler.UpsertViewPath, model);
        }

        if (string.Equals(submitAction, "publish", StringComparison.OrdinalIgnoreCase)
            && handler.SupportsPublishing
            && result.NodeId.HasValue)
        {
            var publishResult = await handler.PublishAsync(result.NodeId.Value, ct);
            if (!publishResult.Success)
                TempData["Error"] = publishResult.ErrorMessage;
        }

        return RedirectToAction(nameof(Index), new { contentType });
    }

    [HttpPost("{contentType}/delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string contentType, Guid id, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        if (!HasWriteAccess(handler.WriteRoles)) return Forbid();

        await handler.DeleteAsync(id, ct);
        return RedirectToAction(nameof(Index), new { contentType });
    }

    // ─── Publishing ───────────────────────────────────────────────────────────

    [HttpPost("{contentType}/publish/{nodeId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(string contentType, Guid nodeId, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);
        if (!handler.SupportsPublishing) return NotFound();
        if (!HasWriteAccess(handler.PublishRoles)) return Forbid();

        var result = await handler.PublishAsync(nodeId, ct);
        if (!result.Success)
            TempData["Error"] = result.ErrorMessage;

        return RedirectToAction(nameof(Index), new { contentType });
    }

    [HttpPost("{contentType}/unpublish/{nodeId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unpublish(string contentType, Guid nodeId, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);
        if (!handler.SupportsPublishing) return NotFound();
        if (!HasWriteAccess(handler.PublishRoles)) return Forbid();

        var result = await handler.UnpublishAsync(nodeId, ct);
        if (!result.Success)
            TempData["Error"] = result.ErrorMessage;

        return RedirectToAction(nameof(Index), new { contentType });
    }

    [HttpGet("{contentType}/preview/{nodeId:guid}")]
    [Authorize(Roles = "Admin,Editor")]
    public async Task<IActionResult> Preview(string contentType, Guid nodeId, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        Response.Cookies.Append(PreviewConstants.CookieName, PreviewConstants.CookieValue, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddMinutes(5)
        });

        var route = (await _routeService.GetByOwningContentAsync(nodeId, ct)).FirstOrDefault();
        return Redirect(route?.Pattern ?? "/");
    }

    // ─── API list endpoints ────────────────────────────────────────────────────

    [HttpGet("{contentType}/api/list")]
    public async Task<IActionResult> ApiList(string contentType, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        var items = await handler.GetApiListAsync(ct);
        return Json(items);
    }

    [HttpGet("{contentType}/api/{key}")]
    public async Task<IActionResult> SecondaryApiList(string contentType, string key, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        if (!handler.HasSecondaryApiList) return NotFound();

        var items = await handler.GetSecondaryApiListAsync(key, ct);
        return Json(items);
    }

    // ─── Registry endpoints ────────────────────────────────────────────────────

    [HttpGet("{contentType}/registry")]
    public IActionResult RegistryList(string contentType)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        if (handler.RegistryHandler == null) return NotFound();
        return handler.RegistryHandler.GetAll();
    }

    [HttpGet("{contentType}/registry/{name}/properties")]
    public IActionResult RegistryProperties(string contentType, string name)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        if (handler.RegistryHandler == null) return NotFound();
        return handler.RegistryHandler.GetProperties(name);
    }

    [HttpPost("{contentType}/registry/{name}/form")]
    [ValidateAntiForgeryToken]
    public IActionResult RegistryForm(string contentType, string name, [FromBody] string? valuesJson)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        if (handler.RegistryHandler == null) return NotFound();
        return handler.RegistryHandler.GetForm(name, valuesJson);
    }

    // ─── Version History (top-level) ──────────────────────────────────────────

    [HttpGet("{contentType}/versions/{nodeId:guid}")]
    [ActionName("VersionHistory")]
    public async Task<IActionResult> VersionHistory(string contentType, Guid nodeId, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);
        if (!handler.SupportsVersionHistory) return NotFound();

        var vm = await handler.GetVersionHistoryViewModelAsync(nodeId, ct);
        if (vm == null) return NotFound();
        return View("~/Views/AdminShared/VersionHistory.cshtml", vm);
    }

    [HttpGet("{contentType}/versions/{nodeId:guid}/edit/{id:guid}")]
    [ActionName("VersionRestoreEdit")]
    public async Task<IActionResult> VersionRestoreEdit(string contentType, Guid nodeId, Guid id, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);
        if (!handler.SupportsVersionHistory) return NotFound();

        var vm = await handler.GetRestoreVersionViewModelAsync(id, ct);
        if (vm == null) return NotFound();
        return View(handler.UpsertViewPath, vm);
    }

    [HttpPost("{contentType}/versions/{nodeId:guid}/restore/{id:guid}")]
    [ActionName("RestoreVersion")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreVersion(string contentType, Guid nodeId, Guid id, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);
        if (!handler.SupportsVersionHistory) return NotFound();
        if (!HasWriteAccess(handler.WriteRoles)) return Forbid();

        var result = await handler.RestoreVersionAsync(id, ct);
        if (!result.Success)
            TempData["Error"] = result.ErrorMessage;

        return RedirectToAction(nameof(VersionHistory), new { contentType, nodeId });
    }

    [HttpPost("{contentType}/versions/{nodeId:guid}/delete/{id:guid}")]
    [ActionName("VersionDelete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VersionDelete(string contentType, Guid nodeId, Guid id, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);
        if (!handler.SupportsVersionHistory) return NotFound();
        if (!HasWriteAccess(handler.WriteRoles)) return Forbid();

        await handler.DeleteVersionAsync(id, ct);
        return RedirectToAction(nameof(VersionHistory), new { contentType, nodeId });
    }

    // ─── Child CRUD ────────────────────────────────────────────────────────────

    [HttpGet("{contentType}/{parentKey:notreserved}/{childType}")]
    public async Task<IActionResult> ChildIndex(
        string contentType, string parentKey, string childType, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        var child = handler.ChildHandler;
        if (child == null || !string.Equals(child.ChildType, childType, StringComparison.OrdinalIgnoreCase))
            return NotFound();

        var vm = await child.GetChildIndexViewModelAsync(parentKey, ct);
        if (vm == null) return NotFound();
        return View(child.ChildIndexViewPath, vm);
    }

    [HttpGet("{contentType}/{parentKey:notreserved}/{childType}/create")]
    [ActionName("ChildCreate")]
    public async Task<IActionResult> ChildCreate(
        string contentType, string parentKey, string childType, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        var child = handler.ChildHandler;
        if (child == null || !string.Equals(child.ChildType, childType, StringComparison.OrdinalIgnoreCase))
            return NotFound();

        var vm = await child.GetChildUpsertViewModelAsync(parentKey, null, ct);
        if (vm == null) return NotFound();
        await child.SetChildUpsertViewDataAsync(ViewData, parentKey, ct);
        return View(child.ChildUpsertViewPath, vm);
    }

    [HttpGet("{contentType}/{parentKey:notreserved}/{childType}/edit/{id:guid}")]
    public async Task<IActionResult> ChildEdit(
        string contentType, string parentKey, string childType, Guid id, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        var child = handler.ChildHandler;
        if (child == null || !string.Equals(child.ChildType, childType, StringComparison.OrdinalIgnoreCase))
            return NotFound();

        var vm = await child.GetChildUpsertViewModelAsync(parentKey, id, ct);
        if (vm == null) return NotFound();
        await child.SetChildUpsertViewDataAsync(ViewData, parentKey, ct);
        return View(child.ChildUpsertViewPath, vm);
    }

    [HttpPost("{contentType}/{parentKey:notreserved}/{childType}/edit/{id:guid?}")]
    [ActionName("ChildEdit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChildEditPost(
        string contentType, string parentKey, string childType, Guid? id, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        var child = handler.ChildHandler;
        if (child == null || !string.Equals(child.ChildType, childType, StringComparison.OrdinalIgnoreCase))
            return NotFound();

        if (!HasWriteAccess(child.WriteRoles)) return Forbid();

        var model = child.CreateEmptyChildUpsertViewModel();
        await TryUpdateModelAsync(model, model.GetType(), prefix: "");

        if (!ModelState.IsValid)
        {
            await child.SetChildUpsertViewDataAsync(ViewData, parentKey, ct);
            return View(child.ChildUpsertViewPath, model);
        }

        var result = await child.SaveChildUpsertAsync(parentKey, model, ct);
        if (!result.Success)
        {
            ModelState.AddModelError(
                result.ErrorField ?? string.Empty,
                result.ErrorMessage ?? "An error occurred.");
            await child.SetChildUpsertViewDataAsync(ViewData, parentKey, ct);
            return View(child.ChildUpsertViewPath, model);
        }

        return RedirectToAction(nameof(ChildIndex), new { contentType, parentKey, childType });
    }

    [HttpPost("{contentType}/{parentKey:notreserved}/{childType}/delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChildDelete(
        string contentType, string parentKey, string childType, Guid id, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        var child = handler.ChildHandler;
        if (child == null || !string.Equals(child.ChildType, childType, StringComparison.OrdinalIgnoreCase))
            return NotFound();

        if (!HasWriteAccess(child.WriteRoles)) return Forbid();

        await child.DeleteChildAsync(id, ct);
        return RedirectToAction(nameof(ChildIndex), new { contentType, parentKey, childType });
    }

    [HttpPost("{contentType}/{parentKey:notreserved}/{childType}/reorder")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChildReorder(
        string contentType, string parentKey, string childType,
        [FromBody] List<Guid> orderedIds, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        var child = handler.ChildHandler;
        if (child == null || !string.Equals(child.ChildType, childType, StringComparison.OrdinalIgnoreCase))
            return NotFound();

        if (!HasWriteAccess(child.WriteRoles)) return Forbid();
        if (!child.SupportsReorder) return BadRequest(new { error = "Reorder is not supported for this content type." });

        var success = await child.ReorderAsync(parentKey, orderedIds, ct);
        return success ? Ok() : StatusCode(500);
    }

    // ─── Child Version History ─────────────────────────────────────────────────

    [HttpGet("{contentType}/{parentKey:notreserved}/{childType}/versions/{nodeId:guid}")]
    [ActionName("ChildVersionHistory")]
    public async Task<IActionResult> ChildVersionHistory(
        string contentType, string parentKey, string childType, Guid nodeId, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        var child = handler.ChildHandler;
        if (child == null || !string.Equals(child.ChildType, childType, StringComparison.OrdinalIgnoreCase))
            return NotFound();
        if (!child.SupportsVersionHistory) return NotFound();

        var vm = await child.GetChildVersionHistoryViewModelAsync(parentKey, nodeId, ct);
        if (vm == null) return NotFound();
        return View("~/Views/AdminShared/VersionHistory.cshtml", vm);
    }

    [HttpGet("{contentType}/{parentKey:notreserved}/{childType}/versions/{nodeId:guid}/edit/{id:guid}")]
    [ActionName("ChildVersionRestoreEdit")]
    public async Task<IActionResult> ChildVersionRestoreEdit(
        string contentType, string parentKey, string childType, Guid nodeId, Guid id, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        var child = handler.ChildHandler;
        if (child == null || !string.Equals(child.ChildType, childType, StringComparison.OrdinalIgnoreCase))
            return NotFound();
        if (!child.SupportsVersionHistory) return NotFound();

        var vm = await child.GetChildRestoreVersionViewModelAsync(parentKey, id, ct);
        if (vm == null) return NotFound();
        await child.SetChildUpsertViewDataAsync(ViewData, parentKey, ct);
        return View(child.ChildUpsertViewPath, vm);
    }

    [HttpPost("{contentType}/{parentKey:notreserved}/{childType}/versions/{nodeId:guid}/delete/{id:guid}")]
    [ActionName("ChildVersionDelete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChildVersionDelete(
        string contentType, string parentKey, string childType, Guid nodeId, Guid id, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        var child = handler.ChildHandler;
        if (child == null || !string.Equals(child.ChildType, childType, StringComparison.OrdinalIgnoreCase))
            return NotFound();
        if (!child.SupportsVersionHistory) return NotFound();
        if (!HasWriteAccess(child.WriteRoles)) return Forbid();

        await child.DeleteChildVersionAsync(id, ct);
        return RedirectToAction(nameof(ChildVersionHistory), new { contentType, parentKey, childType, nodeId });
    }
}
