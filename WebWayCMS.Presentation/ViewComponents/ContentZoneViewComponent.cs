using Microsoft.AspNetCore.Mvc;
using WebWayCMS.ContentZones;
using WebWayCMS.Data.Models;
using WebWayCMS.Models.ContentZone;

namespace WebWayCMS.ViewComponents;

/// <summary>
/// ViewComponent that renders a content zone by slot name.
/// Content zones contain a list of other view components configured in the database.
/// Page and nested zones are resolved via the ContentZoneAssignments table.
/// Global zones fall back to name-based lookup.
/// When an admin user is viewing, an edit mode is displayed allowing inline management.
/// </summary>
public class ContentZoneViewComponent : ViewComponent
{
	private readonly IContentZoneModel _model;
	private readonly IContentZoneComponentRegistry _registry;

	public ContentZoneViewComponent(IContentZoneModel model, IContentZoneComponentRegistry registry)
	{
		_model = model ?? throw new ArgumentNullException(nameof(model));
		_registry = registry ?? throw new ArgumentNullException(nameof(registry));
	}

	/// <summary>
	/// Renders the content zone for the given slot name within the current context.
	/// </summary>
	/// <param name="zoneName">The slot name, e.g. "Main", "Sidebar".</param>
	/// <param name="IsGlobal">When true, bypasses page/zone context and uses name-based lookup.</param>
	/// <param name="editMode">When true, renders the edit UI regardless of the current user's role.</param>
	/// <param name="zoneId">When provided, skips name/page resolution and fetches the zone directly by ID.</param>
	public async Task<IViewComponentResult> InvokeAsync(
		string? zoneName = null,
		bool IsGlobal = false,
		bool editMode = false,
		Guid? zoneId = null)
	{
		var ct = HttpContext.RequestAborted;

		// A nested zone inherits edit mode from the ancestor zone that is being edited.
		if (ViewData["ContentZone:EditMode"] as bool? == true)
			editMode = true;

		ContentZoneViewModel? vm;

		Guid? pageMasterIdForVm = null;

		if (zoneId.HasValue)
		{
			// Direct lookup by zone ID - bypasses name/page resolution
			vm = await _model.GetViewModelByIdAsync(zoneId.Value, ct);
		}
		else
		{
			if (string.IsNullOrWhiteSpace(zoneName))
				return Content(string.Empty);

			var parentZoneId = ViewData["ContentZone:ParentZoneId"] as Guid?;

			if (!IsGlobal && parentZoneId.HasValue)
			{
				// Nested zone inside another zone - works with or without page context.
				vm = await _model.GetOrCreateViewModelByZoneSlotAsync(parentZoneId.Value, zoneName, ct);
			}
			else if (!IsGlobal && HttpContext.Items["CMS:PageData"] is PageDTO pageData)
			{
				// Top-level page zone: resolve via assignment
				pageMasterIdForVm = pageData.ContentMeta.MasterId;
				vm = await _model.GetOrCreateViewModelByPageSlotAsync(pageData.ContentMeta.MasterId, zoneName, ct);
			}
			else
			{
				// Global or layout zone: get or create by name
				vm = await _model.GetOrCreateViewModelAsync(zoneName, ct);
			}
		}

		if (vm == null)
		{
			vm = new ContentZoneViewModel
			{
				Id = Guid.Empty,
				Name = zoneName ?? string.Empty,
				RawZoneName = zoneName ?? string.Empty,
				ZoneObjects = new List<ContentZoneObject>(),
				CanEdit = editMode,
				ParentPageMasterId = pageMasterIdForVm
			};
		}
		else
		{
			vm.CanEdit = editMode;
			vm.RawZoneName = zoneName ?? vm.Name;
			vm.ParentPageMasterId = pageMasterIdForVm;
		}

		// Store this zone's ID in ViewData so nested zones can use it as their parent
		if (vm.Id != Guid.Empty)
			ViewData["ContentZone:ParentZoneId"] = vm.Id;

		// Thread edit mode down so nested zones (e.g. inside a Layout widget) are editable too.
		if (editMode)
			ViewData["ContentZone:EditMode"] = true;

		if (editMode)
		{
			ViewData["ComponentsByCategory"] = _registry.GetComponentsByCategory();
			return View("Edit", vm);
		}

		if (vm.ZoneObjects?.Any() != true)
			return Content(string.Empty);

		return View(vm);
	}
}
