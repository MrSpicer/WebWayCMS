# Area 4: Content Zone Component Framework

**Namespaces:**
- `WebWayCMS.ContentZones` — `WidgetRegistry`, `IWidgetRegistry`, `WidgetRegistrationInfo`
- `WebWayCMS.Data.Services` — `IWidgetRegistrationService`, `WidgetRegistrationService`
- `WebWayCMS.Attributes` — `[ContentZoneComponent]`
- `WebWayCMS.ViewComponents` — `ContentZoneViewComponent`
- `WebWayCMS.Models.ContentZone` — `ContentZoneViewModel`, `ContentZoneObject`, `IContentZoneObject`, `ContentZoneUpsertViewModel`
- `WebWayCMS.Models.WidgetRegistration` — `WidgetRegistrationModel` (the `widgets` admin content type)

**Depends on:** Data Tier (`IContentZoneService`), Form Generation Metadata (`FormPropertyBuilder`), Page Routing Subsystem (`CMS:PageData` from `HttpContext`)

> **Versioning note (Node/Version model):** zone **items** and assignments are keyed on `ContentNode.Id`
> (not a version row); items have no static `Items` navigation and are resolved through
> `IContentZoneService.GetItemsAsync(zoneNodeId)` at the read context. Items in a page's zones are
> drafts until the page is published (the page publish cascades to them); items in global zones
> auto-publish on write. See [01-data-tier](01-data-tier.md) §7.
**Consumed by:** Admin CRUD Framework (zone controller + inline API), any Razor view invoking `ContentZone` component

---

## 1. System Overview

Content zones are named database-backed slots that appear in Razor views. Each zone holds an ordered list of *widget instances* — rows in `ContentZoneItems` that reference a ViewComponent by name and store a JSON configuration blob.

Zones can be:
- **Page-scoped** — tied to a specific page via `ContentZoneAssignments (ParentPageNodeId, SlotName)`
- **Nested** — owned by the zone *item* that renders it (e.g. a Layout widget's columns) via
  `ContentZoneAssignments (ParentItemNodeId, SlotName)`, so the nested content moves with the item
- **Global** — looked up by name only, shared across all pages

Widgets are ViewComponents decorated with `[ContentZoneComponent]`. They receive their stored JSON configuration deserialized into a typed object.

---

## 2. `ContentZoneViewComponent.InvokeAsync` — Parameters

```csharp
await Component.InvokeAsync("ContentZone", new
{
    zoneName = "Main",    // slot name
    IsGlobal = false,     // bypass page/zone context
    editMode = false,     // force edit UI (admin inline editing)
    zoneId = (Guid?)null  // skip name/page resolution; fetch by ID
})
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `zoneName` | `string?` | `null` | Named slot to render; required unless `zoneId` is provided |
| `IsGlobal` | `bool` | `false` | When `true`, ignores `CMS:PageData` and resolves by name only |
| `editMode` | `bool` | `false` | When `true`, renders the edit UI with add/remove/reorder controls |
| `zoneId` | `Guid?` | `null` | Direct zone ID lookup, bypassing name/page resolution entirely |

**Read mode** (default): Renders each widget ViewComponent in order. Returns `Content(string.Empty)` for empty zones.

**Edit mode**: Sets `ViewData["ContentZone:RenderMode"] = WidgetRenderMode.Edit` (read mode sets
`WidgetRenderMode.View`), which mode-aware widgets (`WidgetViewComponent`) honour — they render their
`edit.*` views (or the generic edit card) instead of their public views. Also populates
`ViewData["ComponentsByCategory"]` with `IWidgetRegistry.GetComponentsByCategory()` for the
add-widget dropdown.

---

## 3. Zone Resolution Algorithm

`ContentZoneViewComponent.InvokeAsync` resolves zones in this order:

1. **Direct ID lookup** — if `zoneId` is provided, call `_model.GetViewModelByIdAsync(zoneId)` and skip all other steps
2. **Nested zone** — if `ViewData["ContentZone:ParentItemId"]` is set (a zone item is rendering), call `_model.GetOrCreateViewModelByItemSlotAsync(parentItemId, zoneName)`. If it is *not* set but the invocation is inside a zone render (`ViewData["ContentZone:RenderMode"]` present) and `IsGlobal = false`, the zone renders nothing and logs a warning: falling through to steps 3–4 could resolve the enclosing zone itself and recurse forever
3. **Page-scoped zone** — if `HttpContext.Items["CMS:PageData"]` is a `PageDTO` and `IsGlobal = false`, call `_model.GetOrCreateViewModelByPageSlotAsync(pageNodeId, zoneName)`
4. **Global zone** — otherwise, call `_model.GetOrCreateViewModelAsync(zoneName)`

If the resolved `ContentZoneViewModel` is `null`, an empty view model is constructed (zone exists conceptually but has no DB record yet).

---

## 4. Lazy Zone Creation

Zones are created on demand. The first time a page is rendered in admin edit mode, `GetOrCreateByPageSlotAsync` runs inside a database transaction:

1. Check if an assignment exists for `(pageNodeId, slotName)`
2. If yes, resolve the zone **draft-aware** — `GetAsync` (published) then `GetCurrentDraftAsync`
   (draft) — and return it. An *unpublished* zone is therefore found and reused, not silently
   duplicated.
3. If the assignment points at a zone that no longer exists (both lookups miss), the assignment is
   **dangling** — create a real published zone and **repoint** the assignment to it. Get-or-create
   never hands back an unpersisted zone with `Node.Id == Guid.Empty`.
4. If no assignment exists, begin transaction → re-check (double-checked locking) → create
   `ContentZoneDTO` + `ContentZoneAssignmentDTO` atomically → commit

This means zones do not need to be seeded or pre-created. They appear in the database only when an
admin first visits a page in edit mode. The same pattern applies to global zones
(`GetOrCreateByNameAsync`, which also uses a draft-aware name lookup) and nested zones
(`GetOrCreateByItemSlotAsync`).

---

## 5. The Widget Registry — Seed Once, Then Read From the Database

Widget discovery happens in two distinct stages. Reflection runs **only at startup, as a seeder**;
everything at runtime reads from the database.

### Stage 1 — Startup seeding

`CmsWidgetRegistrationSeeder.EnsureWidgetRegistrationsSeeded` scans:
- `typeof(ContentZoneViewComponent).Assembly` — `WebWayCMS.Presentation`
- `Assembly.GetEntryAssembly()` — the host Web project

Any non-abstract class inheriting from `ViewComponent` and carrying `[ContentZoneComponent]` is
turned into a `WidgetRegistrationDTO` row in the `WidgetRegistrations` table. The component name is
derived by stripping the `"ViewComponent"` suffix from the class name; the attribute's
`ConfigurationType` is reflected through `FormPropertyBuilder.BuildPropertyInfos` and stored as
`PropertyDefinitionsJson`.

Seeding **only inserts**. A component name that already has a row is skipped, and the existing row
is never updated — so editing a `[ContentZoneComponent]` attribute in code has no effect on an
already-seeded widget. Change it at `/wadmin/widgets` instead. Set
`WEBWAYCMS_SKIP_DEFAULTWIDGETS=true` to suppress seeding.

Because the registration is a row rather than a reflection result, widgets can be renamed,
re-categorised, re-ordered, or deactivated (`IsActive = false`) by an admin with no code change.

### Stage 2 — Runtime lookup: `IWidgetRegistry`

`WidgetRegistry` is a **singleton** registered as `IWidgetRegistry`. It resolves
`IWidgetRegistrationService` through an `IServiceScopeFactory`, calls `GetActiveAsync()`, and
caches the result for **5 minutes**. `Invalidate()` drops the cache immediately — every mutation in
`WidgetRegistrationModel` calls it, so admin edits take effect at once.

`GetActiveAsync` returns rows where `IsActive && IsPublished && !IsDeleted`, latest version per
`NodeId`, ordered by Category → Order → DisplayName.

**Interface:**
```csharp
IReadOnlyList<WidgetRegistrationInfo> GetAllComponents()
WidgetRegistrationInfo? GetByName(string componentName)
IReadOnlyList<string> GetCategories()
IReadOnlyList<WidgetRegistrationInfo> GetByCategory(string category)
IReadOnlyDictionary<string, IReadOnlyList<WidgetRegistrationInfo>> GetComponentsByCategory()
object? CreateDefaultConfiguration(string componentName)
IReadOnlyList<string> ValidateConfiguration(string componentName, object configuration)
void Invalidate()
```

`WidgetRegistrationInfo` carries `Name`, `DisplayName`, `Description`, `Category`, `IconClass`,
`Order`, `ConfigurationTypeName`, `Properties` (`List<FormPropertyInfo>`), and the derived
`HasConfiguration`. The configuration CLR type is resolved from `ConfigurationTypeName` at load
time by sweeping the loaded assemblies.

`ValidateConfiguration` checks required fields, numeric range, max length, and regex pattern against
the resolved `FormPropertyInfo` list. It accepts either a typed config object or a JSON string.

> The older reflection-based `ContentZoneComponentRegistry` has been removed. `IWidgetRegistry`
> (backed by the database) is the sole source of widget metadata at runtime.

---

## 6. `[ContentZoneComponent]` Attribute Reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DisplayName` | `string` | Component name with spaces | Shown in the add-widget dropdown |
| `Description` | `string` | `""` | Help text |
| `Category` | `string` | `"General"` | Groups related widgets (e.g. "Content", "Layout", "Media") |
| `ConfigurationType` | `Type?` | `null` | Config class; properties become the widget's config form |
| `IconClass` | `string` | `""` | CSS icon class for admin UI display |
| `Order` | `int` | `0` | Sort order within category |

Remember these values are **seed defaults**, not live metadata — see §5.

---

## 7. `ContentZoneObject` / `IContentZoneObject`

`ContentZoneObject` is the render-time wrapper passed to each widget invocation. It is built from a `ContentZoneItemDTO` + registry lookup:

```csharp
public class ContentZoneObject : IContentZoneObject
{
    public Guid Id { get; set; }              // ContentZoneItemDTO.Id
    public string ComponentName { get; set; } // e.g. "ContentBlock"
    public string ComponentPropertiesJson { get; set; }  // raw JSON stored in DB
    public object? Configuration { get; set; }  // deserialized config object
    public int Ordinal { get; set; }
    public bool IsActive { get; set; }
}
```

The `ContentZoneViewComponent` renders each item by invoking:
```razor
@await Component.InvokeAsync(item.ComponentName, new { configuration = item.Configuration })
```

Widget ViewComponents receive the deserialized configuration as a parameter named `configuration` with the type they declared in `ConfigurationType`.

---

## 8. Nested Zones

Zones can contain other zones by having a widget render its own `ContentZone` component invocations.
A nested zone is **owned by the zone item** that renders it, and the item's node ID is threaded through
`ViewData`:

1. `ContentZoneModel` attaches the item's node id to its configuration
   (`WidgetConfiguration.ZoneItemNodeId`, `[JsonIgnore]`), and `WidgetViewComponent.ModeView` copies it into
   `ViewData["ContentZone:ParentItemId"]` before rendering. This works whichever ContentZone view rendered
   the item, including a host override. The shipped zone views (`Default`, `list`, admin `edit`) also set
   `ViewData["ContentZone:ParentItemId"] = obj.Id` per item, which covers container widgets that don't
   derive from `WidgetViewComponent`
2. When the widget renders `@await Component.InvokeAsync("ContentZone", new { zoneName = "Inner" })`, that `ViewData` is in scope
3. The inner invocation detects `ContentZone:ParentItemId` and calls `GetOrCreateViewModelByItemSlotAsync(parentItemId, "Inner")`

Keying by the item (not the zone it sits in) means two Layout widgets in one zone each get their own
columns, and moving a Layout to another zone (drag-and-drop) carries its columns' content with it.
A nested zone's "parent zone" is derived: the zone its owning item's current draft sits in
(`GetAllByParentZoneAsync`, `GetZoneNodeIdsWithChildrenAsync`, `GetParentPageNodeForZoneAsync`).

Hard-deleting a container item (never published, or in a global zone) deletes its nested zones and
their items with it; a zone still referenced by another assignment survives. Deleting a published
container in a page zone is a soft `IsActive=false` draft, so its nested zones stay until publish and
Discard brings everything back.

Nesting depth is unlimited, but each level adds a database query. Avoid deep nesting for performance-sensitive pages.

---

## 8a. Moving Items (drag-and-drop)

`IContentZoneService.MoveItemAsync(itemNodeId, targetZoneNodeId, position, expectedVersionNumber)` moves an item to a 0-based
position (clamped) among the target zone's active items, returning `(bool Success, string? ErrorMessage)`.
Removed (inactive) items in the target zone are renumbered in place along with the live ones, so ordinals
stay unique if a removal is later discarded; a cross-zone move also renumbers the source zone's remaining
items. `expectedVersionNumber` (the moved item's version as the client rendered it, `data-version` on the
edit view) rejects the move with the shared stale-version message when the widget changed since the page
loaded, and every sibling the reorder rewrites carries the version it was read with, so a concurrent edit
fails the move instead of being silently reverted:

- **Same zone** — a reorder; a global zone auto-publishes, a page zone saves draft ordinals.
- **Another zone** — allowed only when both zones resolve to the **same page**
  (`GetParentPageNodeForZoneAsync`), since a global zone auto-publishes and another page publishes on its
  own schedule. A container can't be moved into one of its own nested zones (at any depth).
- `ContentZoneNodeId` is a versioned column, so a move is an ordinary draft: the live site keeps the
  published zone until the page is published, the preview and editor show the draft zone, and Discard
  moves it back. Widget routes are owned by the item node, so a move never touches the route table.
- Discard runs once-published items first, so a live widget dragged into a never-published container
  returns home before that container is deleted; `DeleteZoneAsync` likewise sends an item whose
  published version lives in another zone back instead of deleting it.

Surfaces: `POST /api/contentzones/items/{itemId}/move` (`{ targetZoneId, position, expectedVersionNumber }`; a stale version answers 409, used by the zone
editor) and the generic MCP `move_child` tool (`IAdminCrudChildHandler.SupportsMove`/`MoveAsync`).

**Zone editor UI** (`ContentZone/drag.js` → `wwwroot/js/content-zone-drag.js`, loaded once by the edit
view after `content-zone-edit.js`): each item's toolbar starts with a grip handle. Dragging it uses
Pointer Events (mouse, touch, pen; `touch-action: none` on the handle); a slim placeholder marks the drop
point and the hovered zone is outlined (red when it can't take the drop). The client only offers drops
into the source zone or another saved zone with the same `data-page-node-id` (the owning page, resolved
by `ContentZoneViewComponent` in edit mode and inherited by nested zones via
`ViewData["ContentZone:OwnerPageId"]`, where a null owner — a global zone — is inherited too rather than
looked up again); the server enforces the same rules. The window auto-scrolls near
its edges, Escape cancels, and every drop saves and reloads like other zone edits. On a focused handle,
ArrowUp/ArrowDown move the item one step within its zone and focus returns to the handle after the reload.

---

## 9. Component Configuration Contract

**Storage:** `ContentZoneItemDTO.ComponentPropertiesJson` — a JSON string written when the admin saves the widget's config form.

**Admin form generation:** `IWidgetRegistry.GetByName(componentName).Properties` — deserialized from the registration row's `PropertyDefinitionsJson`, which `FormPropertyBuilder.BuildPropertyInfos(ConfigurationType)` produced at seed time. The `FormFieldsTagHelper` renders this into an HTML form.

**Runtime deserialization:** `ContentZoneModel` deserializes the JSON into the `ConfigurationType` when building `ContentZoneViewModel`. The result is stored in `ContentZoneObject.Configuration`.

**Widget receives:** The typed configuration object as a parameter to `Invoke`. Mode-aware widgets
derive from `WidgetViewComponent` and return `ModeView(config, model)`, which resolves the active
render mode's view (`{mode}.{variant}.cshtml`, per-mode fallback chain, generic edit card / empty
content per mode — see [widget-system.md → Render Modes](../widget-system.md#render-modes)):
```csharp
public class MyWidgetViewComponent : WidgetViewComponent
{
    public IViewComponentResult Invoke(MyWidgetConfiguration? configuration)
    {
        configuration ??= new MyWidgetConfiguration();
        return ModeView(configuration, configuration);
    }
}
```

The configuration type derives from `WidgetConfiguration` (contributes the `ViewName` (Single) /
`ListViewName` (List) pickers and the attached `WidgetMetadata`). At render time `ContentZoneModel`
deserializes the zone item's `MetadataJson` column into `WidgetMetadata` and attaches it to
`config.Metadata`; the metadata's `DisplayMode` (Single/List, only for
`[ContentZoneComponent(SupportsList = true)]` widgets — coerced to Single otherwise) together with the
request path (admin vs. public) and the zone's edit flag decide the render mode. If `ConfigurationType` is `null`,
the widget receives no configuration parameter and passes `null` to `ModeView`.

### Entity-picker endpoint resolution

The list endpoint for an `EntityPicker` is resolved **server-side**: `FormEntityPicker` passes the
field's `EntityType` and the (optional) `IAdminHandlerRegistry` to `EntityPickerEndpoint.Resolve`
(`WebWayCMS.Core/Controllers/Admin/Handlers/`), and `adminwrite.default.cshtml` emits the result as
`data-endpoint` on the `<select>`. `form-components.js` only fetches that URL; it holds no endpoint
map. Resolution order:

1. a handler whose `ContentType` equals the entity type → `GET /wadmin/{contentType}/api/list`
   (e.g. host `faqs`);
2. a handler whose `ContentType` is the plural (`ContentBlock` → `contentblocks`, `Image` → `images`,
   `Article` → `articles`, `ArticleList` → `articlelists`, `ContentZone` → `contentzones`);
3. a handler secondary list key equal to the entity type or its plural (for a handler that serves a
   second list under its own URL: `GET /wadmin/{contentType}/api/{key}`).

A host content type can therefore be an `EntityPicker` target by setting
`EntityType = "<contentTypeKey>"` — no JS edit required. With no match (or on a rendering-only host,
which has no registry) the attribute is omitted and the picker shows "Unknown entity type".

---

## 10. Known Gaps (configuration contract)

Building a full end-to-end widget test surfaced these gaps. None are silently papered over here.

1. **Widget config is never sanitized or validated on save.** `ContentZoneApiController.SaveItem`
   writes `ComponentPropertiesJson` straight through `IContentZoneService.AddItemAsync` /
   `UpdateItemAsync`. It never calls `RichTextSanitizer` (unlike `AdminCrudModel.SaveUpsertAsync`)
   and never calls `IWidgetRegistry.ValidateConfiguration` — which is dead code, nothing in the
   product calls it. So `IsRequired` / `Min` / `Max` / `MaxLength` / `Pattern` on a widget config are
   declared and never enforced, and a `RichText` config property is stored raw.
2. **A widget route's `DefaultsJson` never reaches `RouteData`.** `CMSRouteTransformer` forwards only
   `controller` and `action` from defaults, plus the captured route values — and every CMS request also
   carries the host catch-all's `slug` (the page path on a plain page request). Widgets therefore decide
   whether a route value is theirs from the matched route (`HttpContext.Items["CMS:RouteData"]`) via
   `WidgetViewComponent.IsOwnWidgetRoute` / `IsWidgetRouteRequest`, not from route-value names.
3. **`NormalizePattern` lowercases whole widget patterns**, so an uppercase-bearing inline regex
   constraint in a widget route silently stops matching.
4. **`RouteRegistrationService` defaults `OwningContentType ??= "{ComponentName}Widget"`** for a widget
   route that leaves it unset (it was once hard-coded to `"ArticleWidget"`).
5. **`CMSRouteDTO.ConstraintsJson` is write-only.** `CMSRouteService.TryMatchPattern` parses
   constraints out of the *pattern text* (`ParseParameter` → `ApplyConstraint`, supporting `int`,
   `guid`, `bool` and `regex(...)`); nothing in the product ever reads `ConstraintsJson`. It is
   written by `MappingProfile` and `CmsRouteSeeder`, and
   consumed by nobody — so a constraint declared there is silently unenforced. Routable widgets must
   put constraints **inline**: `Pattern = "{slug:regex(^[a-z0-9-]+$)}"`, not
   `ConstraintsJson = {"slug":"regex(...)"}`.
6. **A widget config property that is a non-nullable value type can drop the whole configuration.**
   The admin form serializer (`form-components.js` → `serializeDataProps`) writes `null` for an empty
   number field, and `System.Text.Json` rejects `null` for a non-nullable `int`. The resulting
   `JsonException` is swallowed by `ContentZoneModel.DeserializePropertiesToConfigType`, which
   returns an empty object — so *every* saved value is lost, not just the empty one. Declare numeric
   config properties as nullable (`int?`) and apply the fallback in the widget. `bool` is safe
   (a checkbox always serializes a value).

---

*See also:* [docs/widget-system.md](../widget-system.md) for the step-by-step guide to creating a custom widget.
