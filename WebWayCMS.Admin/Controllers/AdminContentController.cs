using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Models.Shared;

namespace WebWayCMS.Controllers.Admin;

[Authorize(Roles = "Admin")]
[Route("wadmin")]
public class AdminContentController : Controller
{
    private static readonly Serilog.ILogger Logger = Serilog.Log.ForContext<AdminContentController>();

    private readonly IAdminHandlerRegistry _registry;
    private readonly IWidgetRegistry _widgetRegistry;

    public AdminContentController(IAdminHandlerRegistry registry, IWidgetRegistry widgetRegistry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _widgetRegistry = widgetRegistry ?? throw new ArgumentNullException(nameof(widgetRegistry));
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
        if (AdminModal.IsModalRequest(Request))
            ViewData[AdminModal.ViewDataKey] = true;
        return View(handler.UpsertViewPath, vm ?? handler.CreateEmptyUpsertViewModel());
    }

    [HttpGet("{contentType}/edit/{id:guid}")]
    public async Task<IActionResult> Edit(string contentType, Guid id, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        var vm = await handler.GetUpsertViewModelAsync(id, Request.Query, ct);
        if (vm == null) return NotFound();
        if (AdminModal.IsModalRequest(Request))
            ViewData[AdminModal.ViewDataKey] = true;
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

        var isModal = AdminModal.IsModalRequest(Request);
        if (!ModelState.IsValid)
            return ModalOrView(handler, model, isModal);

        var result = await handler.SaveUpsertAsync(model, ct);
        if (!result.Success)
        {
            ModelState.AddModelError(
                result.ErrorField ?? string.Empty,
                result.ErrorMessage ?? "An error occurred.");
            return ModalOrView(handler, model, isModal);
        }

        if (isModal)
        {
            var title = new[] { "Title", "Pattern", "NavigationName", "Name" }
                .Select(name => model.GetType().GetProperty(name)?.GetValue(model) as string)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            return Json(new { id = result.NodeId, title = title ?? string.Empty });
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

    /// <summary>
    /// A validation/save failure inside the picker modal re-renders the form in place with 422 (the
    /// iframe's script swaps the document); outside it, the ordinary full-page form.
    /// </summary>
    private IActionResult ModalOrView(IAdminCrudHandler handler, object model, bool isModal)
    {
        if (!isModal)
            return View(handler.UpsertViewPath, model);

        ViewData[AdminModal.ViewDataKey] = true;
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return View(handler.UpsertViewPath, model);
    }

    [HttpPost("{contentType}/delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string contentType, Guid id, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        if (!HasWriteAccess(handler.WriteRoles)) return Forbid();

        var result = await handler.TryDeleteAsync(id, ct);
        if (!result.Success)
            TempData["Error"] = result.ErrorMessage ?? "The item could not be deleted.";
        else if (result.DeletedDescendants > 0)
            TempData["Success"] = $"Deleted, together with the {result.DeletedDescendants} item(s) it contained.";
        return RedirectToAction(nameof(Index), new { contentType });
    }

    // ─── Publishing ───────────────────────────────────────────────────────────

    [HttpPost("{contentType}/publish/{nodeId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(string contentType, Guid nodeId, [FromForm] string? returnUrl, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);
        if (!handler.SupportsPublishing) return NotFound();
        if (!HasWriteAccess(handler.PublishRoles)) return Forbid();

        var result = await handler.PublishAsync(nodeId, ct);
        if (!result.Success)
            TempData["Error"] = result.ErrorMessage;
        else if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            TempData["Success"] = result.ErrorMessage;

        return RedirectAfterPublishing(contentType, returnUrl);
    }

    [HttpPost("{contentType}/unpublish/{nodeId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unpublish(string contentType, Guid nodeId, [FromForm] string? returnUrl, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);
        if (!handler.SupportsPublishing) return NotFound();
        if (!HasWriteAccess(handler.PublishRoles)) return Forbid();

        var result = await handler.UnpublishAsync(nodeId, ct);
        if (!result.Success)
            TempData["Error"] = result.ErrorMessage;

        return RedirectAfterPublishing(contentType, returnUrl);
    }

    /// <summary>
    /// Back to where the publish was requested from (e.g. the page zone editor's toolbar) when that is
    /// a local URL; otherwise to the content type's index. Never redirects off-site.
    /// </summary>
    private IActionResult RedirectAfterPublishing(string contentType, string? returnUrl)
        => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction(nameof(Index), new { contentType });

    /// <summary>
    /// Gates the draft preview on the content type, then redirects to <c>/_preview/{nodeId}</c>. Sets no
    /// cookie: the route transformer marks that one request as a draft read, so drafts never leak site-wide.
    /// Only Admins reach this action (the controller requires the Admin role), but the transformer itself
    /// also admits Editors, so an Editor can preview via the <c>/_preview/{nodeId}</c> URL directly.
    /// </summary>
    [HttpGet("{contentType}/preview/{nodeId:guid}")]
    public async Task<IActionResult> Preview(string contentType, Guid nodeId, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);
        if (!handler.SupportsPreview) return NotFound();

        return Redirect($"{PreviewConstants.PathPrefix}/{nodeId}");
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
    public IActionResult RegistryForm(string contentType, string name, [FromBody] string? valuesJson, [FromQuery] string? displayMode = null)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        if (handler.RegistryHandler == null) return NotFound();

        // ?displayMode=single|list renders only that mode's configuration fields; anything else
        // (including no value) renders every field.
        WidgetDisplayMode? mode = Enum.TryParse<WidgetDisplayMode>(displayMode, ignoreCase: true, out var parsed)
            && (parsed == WidgetDisplayMode.Single || parsed == WidgetDisplayMode.List)
                ? parsed
                : null;
        return handler.RegistryHandler.GetForm(name, valuesJson, mode);
    }

    // ─── Widget metadata form ─────────────────────────────────────────────────

    /// <summary>
    /// Renders the per-item widget metadata form (the display mode plus the admin presentation
    /// fields) for the zone editor's settings (cog) dialog. The display-mode field is left out for
    /// widgets that do not support list mode.
    /// </summary>
    [HttpPost("widgets/registry/{name}/metadata-form")]
    [ValidateAntiForgeryToken]
    public IActionResult WidgetMetadataForm(string name, [FromBody] string? valuesJson)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { error = "Component name is required." });

        var component = _widgetRegistry.GetByName(name);
        if (component == null)
            return NotFound(new { error = $"Component '{name}' not found." });

        var metadata = WebWayCMS.Forms.DynamicConfigurationForm.Materialize(typeof(WidgetMetadata), valuesJson);
        var excluded = component.SupportsList ? null : new[] { nameof(WidgetMetadata.DisplayMode) };
        return WebWayCMS.Forms.DynamicConfigurationForm.Render(metadata, name, excludedProperties: excluded);
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

        var (success, error) = await handler.TryDeleteVersionAsync(id, ct);
        if (!success)
            TempData["Error"] = error;

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

        var (success, errorMessage) = await child.ReorderAsync(parentKey, orderedIds, ct);
        return success
            ? Ok()
            : StatusCode(500, new { error = errorMessage ?? "Failed to reorder the items." });
    }

    // ─── Child Version History ─────────────────────────────────────────────────

    /// <summary>
    /// A child's version history. <paramref name="returnUrl"/> (local URLs only, e.g. the page zone editor
    /// that linked here) replaces the page's Back link and is carried through its restore/delete forms.
    /// </summary>
    [HttpGet("{contentType}/{parentKey:notreserved}/{childType}/versions/{nodeId:guid}")]
    [ActionName("ChildVersionHistory")]
    public async Task<IActionResult> ChildVersionHistory(
        string contentType, string parentKey, string childType, Guid nodeId, string? returnUrl, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        var child = handler.ChildHandler;
        if (child == null || !string.Equals(child.ChildType, childType, StringComparison.OrdinalIgnoreCase))
            return NotFound();
        if (!child.SupportsVersionHistory) return NotFound();

        var vm = await child.GetChildVersionHistoryViewModelAsync(parentKey, nodeId, ct);
        if (vm == null) return NotFound();

        var local = LocalReturnUrl(returnUrl);
        if (local != null)
        {
            vm.ReturnUrl = local;
            vm.BackUrl = local;
        }

        return View("~/Views/AdminShared/VersionHistory.cshtml", vm);
    }

    /// <summary>
    /// One-step restore of a child version (see <see cref="IAdminCrudChildHandler.RestoreChildVersionAsync"/>);
    /// back to the history page with the outcome.
    /// </summary>
    [HttpPost("{contentType}/{parentKey:notreserved}/{childType}/versions/{nodeId:guid}/restore/{id:guid}")]
    [ActionName("ChildVersionRestore")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChildVersionRestore(
        string contentType, string parentKey, string childType, Guid nodeId, Guid id,
        int? expectedVersionNumber, string? returnUrl, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        var child = handler.ChildHandler;
        if (child == null || !string.Equals(child.ChildType, childType, StringComparison.OrdinalIgnoreCase))
            return NotFound();
        if (!child.SupportsVersionHistory) return NotFound();
        if (!HasWriteAccess(child.WriteRoles)) return Forbid();

        var result = await child.RestoreChildVersionAsync(parentKey, id, expectedVersionNumber, ct);
        if (result.Success)
            TempData["Success"] = "Version restored as a draft.";
        else
            TempData["Error"] = result.ErrorMessage;

        return RedirectToAction(nameof(ChildVersionHistory),
            new { contentType, parentKey, childType, nodeId, returnUrl = LocalReturnUrl(returnUrl) });
    }

    [HttpPost("{contentType}/{parentKey:notreserved}/{childType}/versions/{nodeId:guid}/delete/{id:guid}")]
    [ActionName("ChildVersionDelete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChildVersionDelete(
        string contentType, string parentKey, string childType, Guid nodeId, Guid id, string? returnUrl, CancellationToken ct)
    {
        var handler = _registry.GetHandler(contentType);
        if (handler == null) return HandlerNotFound(contentType);

        var child = handler.ChildHandler;
        if (child == null || !string.Equals(child.ChildType, childType, StringComparison.OrdinalIgnoreCase))
            return NotFound();
        if (!child.SupportsVersionHistory) return NotFound();
        if (!HasWriteAccess(child.WriteRoles)) return Forbid();

        var (success, error) = await child.TryDeleteChildVersionAsync(id, ct);
        if (!success)
            TempData["Error"] = error;

        return RedirectToAction(nameof(ChildVersionHistory),
            new { contentType, parentKey, childType, nodeId, returnUrl = LocalReturnUrl(returnUrl) });
    }

    /// <summary>The return URL when it is local to this site, else null (never an open redirect).</summary>
    private string? LocalReturnUrl(string? returnUrl)
        => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : null;
}
