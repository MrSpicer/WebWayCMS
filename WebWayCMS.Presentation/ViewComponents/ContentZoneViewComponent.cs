using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Data.Models;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.ViewComponents.SlotDiscovery;

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
    private readonly Serilog.ILogger _logger = Serilog.Log.ForContext<ContentZoneViewComponent>();

    private readonly IContentZoneModel _model;
    private readonly IWidgetRegistry _registry;

    public ContentZoneViewComponent(IContentZoneModel model, IWidgetRegistry registry)
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
    /// <param name="zoneId">When provided, skips name/page resolution and fetches the zone directly by node id.</param>
    public async Task<IViewComponentResult> InvokeAsync(
        string? zoneName = null,
        bool IsGlobal = false,
        bool editMode = false,
        Guid? zoneId = null)
    {
        var ct = HttpContext.RequestAborted;

        // A slot-discovery render (IWidgetSlotDiscovery) only notes which of the container's zones its view
        // shows: nothing is loaded or created, and nothing nested renders.
        if (ViewData[SlotDiscoveryRecorder.ViewDataKey] is SlotDiscoveryRecorder recorder)
        {
            if (!IsGlobal && !zoneId.HasValue && !string.IsNullOrWhiteSpace(zoneName)
                && ViewData["ContentZone:ParentItemId"] as Guid? == recorder.ItemNodeId)
                recorder.Record(zoneName);
            return Content(string.Empty);
        }

        // A nested zone inside a zone being edited is edited too. Inheritance only ever turns edit
        // mode on: an ancestor rendering in View mode must not override an explicit editMode:true
        // (e.g. a view rendered inside a public zone that embeds an inline editor).
        if (ViewData["ContentZone:RenderMode"] is WidgetRenderMode.Edit)
            editMode = true;

        ContentZoneViewModel? vm;

        Guid? pageNodeIdForVm = null;

        // The page whose zone tree this zone belongs to (edit mode only): lets the zone editor tell
        // which zones a widget may be dragged between. Unknown until resolved below.
        Guid? ownerPageNodeId = null;
        var ownerResolved = false;

        if (zoneId.HasValue)
        {
            // Direct lookup by zone node id - bypasses name/page resolution
            vm = await _model.GetViewModelByIdAsync(zoneId.Value, ct);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(zoneName))
                return Content(string.Empty);

            // Set for each rendered item by WidgetViewComponent (from the item's configuration) and by
            // the shipped zone views.
            var parentItemNodeId = ViewData["ContentZone:ParentItemId"] as Guid?;

            // Rendered inside a zone (the enclosing zone set the render mode) but with no owning item:
            // falling back to the page slot or zone name could resolve the enclosing zone itself and
            // recurse forever, so render nothing.
            if (!IsGlobal && !parentItemNodeId.HasValue && ViewData.ContainsKey("ContentZone:RenderMode"))
            {
                _logger.Warning(
                    "Nested content zone '{ZoneName}' has no owning zone item; it is not rendered. A widget that renders nested zones must derive from WidgetViewComponent, or its zone view must set ViewData[\"ContentZone:ParentItemId\"].",
                    zoneName);
                return Content(string.Empty);
            }

            if (!IsGlobal && parentItemNodeId.HasValue)
            {
                // Nested zone owned by the zone item (e.g. a Layout widget) rendering it, so the
                // nested content moves with the item.
                vm = await _model.GetOrCreateViewModelByItemSlotAsync(parentItemNodeId.Value, zoneName, ct);

                // An enclosing edit-mode zone already resolved the owning page; null (a global zone)
                // is a resolved answer too, so it is inherited rather than looked up again.
                if (ViewData.ContainsKey("ContentZone:OwnerPageId"))
                {
                    ownerPageNodeId = ViewData["ContentZone:OwnerPageId"] as Guid?;
                    ownerResolved = true;
                }
            }
            else if (!IsGlobal && HttpContext.Items["CMS:PageData"] is PageDTO pageData)
            {
                // Top-level page zone: resolve via assignment
                pageNodeIdForVm = pageData.Version.Node.Id;
                ownerPageNodeId = pageNodeIdForVm;
                ownerResolved = true;
                vm = await _model.GetOrCreateViewModelByPageSlotAsync(pageData.Version.Node.Id, zoneName, ct);
            }
            else
            {
                // Global or layout zone: get or create by name
                vm = await _model.GetOrCreateViewModelAsync(zoneName, ct);
                ownerResolved = IsGlobal;
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
                ParentPageNodeId = pageNodeIdForVm
            };
        }
        else
        {
            vm.CanEdit = editMode;
            vm.RawZoneName = zoneName ?? vm.Name;
            vm.ParentPageNodeId = pageNodeIdForVm;
        }

        // Thread the render mode down so widgets and nested zones (e.g. inside a Layout widget)
        // resolve the correct view mode. Edit in the zone editor, View everywhere else.
        ViewData["ContentZone:RenderMode"] = editMode ? WidgetRenderMode.Edit : WidgetRenderMode.View;

        if (editMode)
        {
            if (!ownerResolved && vm.Id != Guid.Empty)
                ownerPageNodeId = await _model.GetParentPageNodeForZoneAsync(vm.Id, ct);
            vm.OwnerPageNodeId = ownerPageNodeId;
            ViewData["ContentZone:OwnerPageId"] = ownerPageNodeId;

            // The page zone editor lists the zones its widgets own that nothing rendered (ZoneEditRenderLog).
            // A fallback VM for an unresolvable zone has no node id and must not pollute the log's zone set.
            var renderLog = ZoneEditRenderLog.For(HttpContext);
            if (vm.Id != Guid.Empty)
            {
                renderLog.ZoneRendered(vm.Id);
                foreach (var item in vm.ZoneObjects)
                    renderLog.ItemRendered(item.Id);
            }

            ViewData["ComponentsByCategory"] = _registry.GetComponentsByCategory();
            return View("Edit", vm);
        }

        if (vm.ZoneObjects?.Any() != true)
            return Content(string.Empty);

        return View(vm);
    }
}
