# Area 4: Content Zone Component Framework

**Namespaces:**
- `WebWayCMS.ContentZones` — `WidgetRegistry`, `IWidgetRegistry`, `WidgetRegistrationInfo`
- `WebWayCMS.Data.Services` — `IWidgetRegistrationService`, `WidgetRegistrationService`
- `WebWayCMS.Attributes` — `[ContentZoneComponent]`
- `WebWayCMS.ViewComponents` — `ContentZoneViewComponent`
- `WebWayCMS.Models.ContentZone` — `ContentZoneViewModel`, `ContentZoneObject`, `IContentZoneObject`, `ContentZoneUpsertViewModel`
- `WebWayCMS.Models.WidgetRegistration` — `WidgetRegistrationModel` (the `widgets` admin content type)

**Depends on:** Data Tier (`IContentZoneService`), Form Generation Metadata (`FormPropertyBuilder`), Page Routing Subsystem (`CMS:PageData` from `HttpContext`)

> **Versioning note (Node/Version model):** zone **items** and zones are keyed on `ContentNode.Id`
> (not a version row), and the structure is relations: an item's version sits in its zone through a `Zone`
> containment (with its ordinal), a zone's version names its page or container item through an `Owner`
> containment (slot in the qualifier) — see [17-content-relations](17-content-relations.md) §7. Items have no static `Items` navigation and are resolved through
> `IContentZoneService.GetItemsAsync(zoneNodeId)` at the read context. Items in a page's zones are
> drafts until the page is published (the page publish cascades to them); items in global zones
> auto-publish on write. See [01-data-tier](01-data-tier.md) §7.
**Consumed by:** Admin CRUD Framework (zone controller + inline API), any Razor view invoking `ContentZone` component

---

## 1. System Overview

Content zones are named database-backed slots that appear in Razor views. Each zone holds an ordered list of *widget instances* — rows in `ContentZoneItems` that reference a ViewComponent by name and store a JSON configuration blob.

Zones can be:
- **Page-scoped** — owned by a specific page's slot (the zone's `Owner` relation → the page, qualifier = slot)
- **Nested** — owned by the zone *item* that renders it (e.g. a Layout widget's columns) through the same
  `Owner` relation → the item, so the nested content moves with the item
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

Zones are created on demand. The first time a page is rendered in admin edit mode, `GetOrCreateByPageSlotAsync`:

1. Looks up the zone owned by `(pageNodeId, slotName)` — or, when there is none, the slot's
   deterministic node id `ZoneNodeIds.For(pageNodeId, slotName)` (an RFC 4122 v5 Guid; the slot is
   case-sensitive).
2. Resolves it **draft-aware** — `GetAsync` (published) then `GetCurrentDraftAsync` (draft) — and returns
   it. An *unpublished* zone is therefore found and reused, not silently duplicated.
3. Otherwise creates it, published, on that node id (`IContentStore.CreateWithNodeIdAsync`) with its
   `Owner` relation in the same save. Two concurrent first renders collide on the `ContentNodes` primary
   key; the loser (`AlreadyExistsMessage`) reads the winner's zone. Inside a caller's transaction (a
   container's view-change remap) the create joins it.

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
IReadOnlyList<string> ValidateConfiguration(string componentName, object configuration, WidgetDisplayMode? displayMode)
void Invalidate()
```

`WidgetRegistrationInfo` carries `Name`, `DisplayName`, `Description`, `Category`, `IconClass`,
`Order`, `ConfigurationTypeName`, `Properties` (`List<FormPropertyInfo>`), and the derived
`HasConfiguration`. The configuration CLR type is resolved from `ConfigurationTypeName` at load
time by sweeping the loaded assemblies.

`ValidateConfiguration` checks required fields, numeric range, max length, and regex pattern against
the resolved `FormPropertyInfo` list. It accepts either a typed config object or a JSON string. With a
`displayMode` it checks only the fields that apply to it (`FormProperty.DisplayModes`), as only those are
on the item's form; `null` checks every field. The range/length/pattern rules are
`FormPropertyConstraints` (shared with `ModelValidator` and the page-controller registry); a pattern has
HTML `pattern` semantics — an empty value passes and the whole value must match.

Zone-item writes call it through `WidgetConfigurationJson.Validate(registry, name, json, displayMode)`
with the caller's payload **before** `ExtractRelations` strips the relation ids (a required relation
field would otherwise read as `Guid.Empty`) and the item's effective display mode
(`WidgetMetadataJson.EffectiveDisplayMode`: a create's chosen metadata, an update's stored metadata). An
empty payload is validated as `{}`.

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
their items with it (a zone has one owner, so nothing else can still reference it). Deleting a published
container in a page zone is a soft `IsActive=false` draft, so its nested zones stay until publish and
Discard brings everything back.

Nesting depth is unlimited, but each level adds a database query. Avoid deep nesting for performance-sensitive pages.

### Changing a container's view

A container's slots are whatever its current view renders, so switching a Layout from two columns to
three would strand the old columns' zones (still owned by the item, never rendered). Every item save and
restore therefore goes through `ContentZoneModel.PlanSlotRemapAsync`:

1. **Fast path.** An item that is being removed, is unchanged, or owns no zone holding a current-draft
   widget (`IContentZoneService.GetOwnedSlotsWithItemsAsync`) saves as before, with no render.
2. **Discovery by dry render.** `IWidgetSlotDiscovery.DiscoverSlotsAsync` (`ViewComponentSlotDiscovery`)
   invokes the widget twice — old and new configuration, built exactly as the zone would
   (`BuildComponentProperties`: metadata, List→Single downgrade, `ZoneItemNodeId`) — with a
   `SlotDiscoveryRecorder` in ViewData and the output discarded. Under it, `ContentZoneViewComponent`
   records the `zoneName` of each non-global, by-name zone whose `ContentZone:ParentItemId` is the item and
   returns empty content (no zone is loaded or created, nothing nested renders), and
   `WidgetViewComponent.GetContextMode` picks the public `view.*`/`list.*` variant even in the zone editor
   (so the slots follow the configured view and host overrides). With no request (the startup seeder) it
   runs in a stand-in `DefaultHttpContext`. Any failure returns null: the save goes ahead unmoved and the
   page zone editor's Unplaced panel shows the content.
3. **Mapping** (`SlotRemapPlanner.Plan`): a slot name in both views keeps its content; the remaining old
   slots pair with the remaining new ones by position; any old slot left over goes to the new view's last
   slot. Only old slots holding widgets produce a `SlotMove`. HeroWithColumns → TwoColumnEqual gives
   Hero→Left, Column1→Right, Column2→Right, Column3→Right; TwoColumnSidebar → OneThirdTwoThird moves nothing.
4. **Atomic write.** `UpdateItemAsync(item, SlotRemapPlan, expected)` / `RestoreItemVersionAsync(…, plan)`
   create the target zones first (zone creation has its own transaction), then in one transaction save the
   container and rewrite each target zone's order: what it already held, then every current draft of each
   source (removed ones included, in order), noted "Moved to {slot} (layout changed)". Moves on a page are
   drafts until the page publishes; in a global zone they publish with the container. One page-history
   entry records it: "{label}: layout changed; content moved (Hero → Left, …)".

Page-history revert and discard restore exact placements and never remap.

**Unplaced content.** Below its slots, the page zone editor lists zones owned by the page's active widgets
that hold active widgets but were not rendered (`ZoneEditRenderLog` records every edit-mode zone and widget
per request; `IContentZoneModel.GetUnplacedCandidatesAsync` supplies "{widget} › {slot}" candidates). Each
renders in edit mode by `zoneId`, so its widgets can be dragged into a real zone (same page tree) or
removed; the loop repeats because a stranded zone may hold a container with its own zones. It never
appears on the live site.

**Removing a container** asks for confirmation naming how many widgets are inside it; the removal itself
stays non-destructive (an inactive version that a restore brings back with its content).

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
editor) and the generic MCP `move_child` tool (`ContentZoneItemModel.MoveRelatedAsync`, with the item's
`expectedVersionNumber`).

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

## 8b. History in the Zone Editor

**Every save is history.** Every save of an item mints a new version (nothing is rewritten in place), each
with an automatic change note — "Added to Main", "Content edited", "Settings changed", "Moved to Side
(position 2)", "Position changed (moved by another widget)" for siblings a drag renumbered, "Reordered
(position n)", "Removed", "Restored from version n". **Removing an item is never a delete**: it saves an
inactive version (a draft on a page; published at once in a global zone), so it can be restored. A removed
container keeps its nested items, hidden, and brings them back when restored.

**Container timeline.** A container's history page also lists every version of every widget that ever sat
inside it, at any depth (`IContentZoneService.GetDescendantItemNodeIdsAsync`, any version counts), merged
newest first with a Widget column (`VersionHistoryViewModel.IsCombined`). "Latest" and each row's Restore
expected version are per widget (`VersionItemViewModel.NodeCurrentVersionNumber`); MCP
`list_child_versions` returns the same rows with their `nodeId`.

**Per item.** Each item's edit-mode toolbar has a History link (`a.zone-history-item`, plain link, no
script) to the zone item's history page,
`/wadmin/contentzoneitems/versions/{itemNodeId}?returnUrl={this editor}`; the local
`returnUrl` becomes the page's Back link and is carried through its forms. Restore there is **one step**
(`POST .../versions/{nodeId}/restore/{id}` → `ContentZoneItemModel.RestoreVersionAsync(versionId, expected)` →
`IContentZoneService.RestoreItemVersionAsync`): the version's content, metadata (List→Single when the
widget no longer supports lists — `WidgetMetadataJson.Restorable`), active state, zone and ordinal come
back as one new draft version, inserted at the old position with siblings renumbered. It is refused when the old zone
is gone or now belongs to another page. Deleting a version that a page-history entry captured is refused
with the entry's date.

**Per page (page-set history).** Every change to the page set records a **Draft** `PageSetSnapshot` (the
page version + every active item in its zone tree, with a note such as "Added ContentBlock to Main"), and
every publish records a **Published** one (what went live); an entry identical to the previous one is
skipped (see [01 → PageSetSnapshot](01-data-tier.md)). **Discard changes** returns the page to what is live
as new versions and is itself recorded, so it can be undone; it is offered only on a published page with
changes. "Pending changes" compares content, so drafts equal to what is live are not pending. The page zone editor toolbar's **History** button opens
`AdminPageHistoryController` (`/wadmin/contentzones/page/{pageNodeId}/history`, Admin only):

| Route | Screen |
|---|---|
| `GET .../history?skip=&take=` | Entries, newest first, paged (`take` default 50, max 200; Newer/Older links): time, Draft/Published tag, the change note, and added/updated/moved/removed counts against the previous entry. `IPageHistoryService.ListPageAsync` reads one page plus the next-older entry, so the last row still diffs against its predecessor |
| `GET .../history/{entryId}` | Entry detail: who/when, captured title, each widget with its change vs. the previous entry and a link to its own history |
| `GET .../history/{entryId}/compare` | The current drafts vs. the entry (what changed *since*), by content; the Revert form carries the draft state token |
| `GET .../history/{entryId}/preview` | Banner + iframe of `/_preview/{pageNodeId}/history/{entryId}` (snapshot read mode) |
| `POST .../history/{entryId}/revert` | Reverts the page and every zone item to the entry as **drafts**; later items become inactive drafts; back to the editor to review and publish. A stale token returns to the comparison with an error |
| `POST .../history/{entryId}/delete` | Deletes the entry record only (its versions are kept, and become deletable) |

The model is `IPageHistoryModel` (Core) over `IPageHistoryService` (Data). The confirmations use
`Views/AdminPageHistory/PageHistory.js` (→ `wwwroot/js/page-history.js`).

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

1. **Widget config validation covers the constraints, not the shapes.** Every zone-item write path
   (`ContentZoneApiController.SaveItem`, the `ContentZoneItemModel` handler behind the admin form,
   MCP and the seeder) runs `RichTextSanitizer` via `WidgetConfigurationJson.Sanitize` and — since the
   2026-10 review closes-out — `IWidgetRegistry.ValidateConfiguration` (no longer dead code): a
   config's `IsRequired` / `Min` / `Max` / `MaxLength` / `Pattern` are enforced server-side for the
   item's display mode (see Stage 2 above), and an
   over-length payload is rejected with a friendly message before it can overflow the 4000-char
   column. What remains unvalidated is *shape*: unknown keys, wrong types, missing defaults the
   render-time deserializer is written to tolerate.
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
