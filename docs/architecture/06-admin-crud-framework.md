# Area 6: Admin CRUD Framework

**Namespaces:**
- `WebWayCMS.Controllers.Admin` — `AdminContentController`, `AdminContentZoneController` (in **`WebWayCMS.Admin`**)
- `WebWayCMS.Controllers.Admin.Handlers` — `IAdminCrudHandler`, `RelatedChild`, `IAdminHandlerRegistry`, `IAdminRegistryHandler`, `AdminSaveResult` (contracts, in **`WebWayCMS.Core`**); `AdminHandlerRegistry` (implementation, in **`WebWayCMS.Admin`**)
- `WebWayCMS.Controllers.Api` — `ContentZoneApiController` (in **`WebWayCMS.Admin`**)

**Depends on:** Content Domain Models (resolves handlers), Content Zone Component Framework (zone controller uses `IWidgetRegistry`), Identity (`[Authorize]`), Form Generation Metadata (tag helper in views)

> **Versioning note (Node/Version model):** the version routes are keyed on `nodeId` (not `masterId`),
> and `IAdminCrudHandler` gained `SupportsPublishing`, `PublishRoles`, `PublishAsync`, `UnpublishAsync`,
> and one-step `RestoreVersionAsync`. New routes: `POST publish|unpublish/{nodeId}`,
> `POST versions/{nodeId}/restore/{id}`, `GET preview/{nodeId}`. See [01-data-tier](01-data-tier.md).
**Consumed by:** The MCP toolsets, which dispatch through the same `IAdminHandlerRegistry`. Otherwise a leaf layer

> **Where this lives.** The contracts (`IAdminCrudHandler` and friends) and `AdminCrudModel<T>` stay
> in `WebWayCMS.Core`, so the domain models can implement them without depending on the admin
> assembly. The controllers, Razor views, admin CSS/JS, and the `AdminHandlerRegistry`
> implementation live in `WebWayCMS.Admin` and are only registered when a host calls
> `AddWebWayCmsAdmin`. See [Area 11](11-deployment-modes.md).

---

## 1. Framework Overview

The admin CRUD framework handles all content management HTTP routes through a single `AdminContentController`. New content types do not require new controllers — they register an `IAdminCrudHandler` implementation in DI, and the framework routes automatically apply.

`AdminHandlerRegistry` is the dispatch table: a dictionary keyed on `ContentType` string (case-insensitive), built from all `IAdminCrudHandler` instances in the DI container.

Ten handlers ship in the box: `pages`, `contentblocks`, `images`, `articlelists`, `articles`, `contentzones`, `widgets`, `pagetypes`, `cmsroutes`, `formcomponents`.

**The same registry backs the MCP server.** `WebWayCMS.Mcp`'s toolsets resolve
`IAdminHandlerRegistry` and drive the identical handler methods, so any content type reachable in
the admin UI is automatically reachable over MCP with no per-type tool code. See
[Area 12](12-mcp-server.md).

---

## 2. `IAdminCrudHandler` Full Method Reference

```csharp
public interface IAdminCrudHandler
{
    string ContentType { get; }      // URL segment, e.g. "contentblocks"
    string DisplayName { get; }      // Human-readable, e.g. "Content Block"
    string[]? WriteRoles { get; }    // null = Admin only; ["Admin","Editor"] to allow editors
    string IndexViewPath { get; }    // Absolute Razor view path for the list view
    string UpsertViewPath { get; }   // Absolute Razor view path for create/edit form

    Task<object> GetIndexViewModelAsync(CancellationToken ct = default);
    Task<object> GetIndexViewModelAsync(IQueryCollection query, CancellationToken ct = default);
    Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default);
    object CreateEmptyUpsertViewModel();
    Task<AdminSaveResult> SaveUpsertAsync(object model, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default);

    bool HasSecondaryApiList { get; }
    Task<IEnumerable<object>> GetSecondaryApiListAsync(string key, CancellationToken ct = default);

    IAdminRegistryHandler? RegistryHandler { get; }

    bool SupportsPublishing => true;
    bool SupportsPreview => false;
    string[]? PublishRoles => WriteRoles;
    Task<AdminSaveResult> PublishAsync(Guid nodeId, CancellationToken ct = default);
    Task<AdminSaveResult> UnpublishAsync(Guid nodeId, CancellationToken ct = default);
    Task<AdminSaveResult> RestoreVersionAsync(Guid versionId, CancellationToken ct = default);
    // the restore, refused when the item moved past the caller's expected version (default: ignores it)
    Task<AdminSaveResult> RestoreVersionAsync(Guid versionId, int? expectedVersionNumber, CancellationToken ct = default);

    // containment (see §3); defaults: no children, reorder/move not supported
    Task<IReadOnlyList<RelatedChild>> GetRelatedChildrenAsync(string relationName, Guid parentNodeId, CancellationToken ct = default);
    Task<(bool Success, string? ErrorMessage)> ReorderRelatedAsync(string relationName, Guid parentNodeId, IReadOnlyList<Guid> orderedNodeIds, CancellationToken ct = default);
    Task<(bool Success, string? ErrorMessage)> MoveRelatedAsync(string relationName, Guid childNodeId, Guid targetParentNodeId, int? position, int? expectedVersionNumber, CancellationToken ct = default);

    bool SupportsVersionHistory => false;
    Task<VersionHistoryViewModel?> GetVersionHistoryViewModelAsync(Guid nodeId, CancellationToken ct = default);
    Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default);
    Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default);

    // DeleteVersionAsync with the reason a delete was refused; default wraps it with VersionNotDeletedMessage
    Task<(bool Success, string? ErrorMessage)> TryDeleteVersionAsync(Guid id, CancellationToken ct = default);
}
```

`TryDeleteVersionAsync` is what the admin `VersionDelete` action and MCP `delete_version` call; the reason
is shown to the user. `PageModel` overrides it to refuse a page version that a page-history entry
captured (`PageHistoryMessages.VersionInHistory`).

**Semantics:**
- `GetIndexViewModelAsync(IQueryCollection)` — default implementation delegates to the parameterless overload; override to support filtering via query string
- `GetUpsertViewModelAsync(null, ...)` — returns an empty create form; non-null `id` returns the edit form, or `null` if not found (triggers 404)
- `CreateEmptyUpsertViewModel()` — returns a fresh ViewModel instance for `TryUpdateModelAsync` binding; must match the type `SaveUpsertAsync` expects
- `SaveUpsertAsync` — receives the model-bound ViewModel; returns `AdminSaveResult(true)` on success or `AdminSaveResult(false, message, field?)` on failure. In `AdminCrudModel` it sanitizes rich text, runs `ModelValidator`, then the **relation binder** (`IRelationBinder`: every `[ContentRelation]` target must be a current draft of the declared type, and `Required` ones set — a field error on the property otherwise), before `SaveUpsertCoreAsync`
- `DeleteAsync` / `TryDeleteAsync` — `TryDeleteAsync` (default: `DeleteAsync` with no reason and no count) returns `(Success, ErrorMessage, DeletedDescendants)`. `AdminCrudModel` implements both through `IContentDeleteService` (the relation engine: containment descendants deepest first and owned targets in one transaction; a `Restrict` reference from outside refuses with "'{title}' is used by n item(s): …", a `Detach` one is removed by saving its referrer) and leaves a model only `DeleteSelfAsync` (its own node). The admin `Delete` action shows the refusal as `TempData["Error"]` and a cascade as `TempData["Success"]`; MCP `delete_content` returns `deletedDescendants` and throws the refusal
- `GetApiListAsync` — returns `[ { id, title } ]` used by GUID entity pickers in admin forms (an optional `path` is shown after the title; pages send it)
- `GetApiListExcludingSubtreeAsync(nodeId)` — the list for a **containment** picker on an existing item's form (`ApiList?excludeSubtreeOf={nodeId}`, sent by `form-components.js` when the picker has `data-exclude-subtree`); a handler may leave out the item and its containment descendants. Default (interface and `AdminCrudModel`): the plain list. `PageModel` overrides it.
- `HasSecondaryApiList` / `GetSecondaryApiListAsync(key)` — extension for handlers needing multiple named lists (none of the built-in handlers uses it; prefer a separate content type with its own `GetApiListAsync`)
- `GetIndexViewModelAsync(IQueryCollection query)` — a default-implemented overload the controller calls with the request query; a handler re-implements it (explicitly, re-listing `IAdminCrudHandler`) to filter its index — e.g. `ArticleModel` honours `?list={nodeId}`, `ContentZoneModel` `?pageId=`/`?zoneId=`

---

## 3. Containment — Children, Reorder, Move

There is no separate child-handler contract: a child is ordinary content of its own type, placed under a
parent by a **containment** relation its upsert view model declares
(`[ContentRelation("<parentKey>", Kind = Containment, Ordered?, ShowInParentAdmin?)]`; see
[17 → Containment](17-content-relations.md)). `IAdminCrudHandler` carries three containment members, which
`AdminCrudModel` implements generically for any declared containment:

- `GetRelatedChildrenAsync(relation, parent)` — the current drafts under the parent as
  `RelatedChild(Id, Title, State, Ordinal)`, by position (`IContentStore.FindCurrentDraftsRelatedAsync`).
- `ReorderRelatedAsync(relation, parent, ordered)` — only for an `Ordered` containment; the list must name
  every child once (`AdminCrudModel.ReorderSetMessage`); each child that moved gets a new draft with its new
  ordinal ("Position changed"), checked against the version it was read at.
- `MoveRelatedAsync(relation, child, newParent, position?, expectedVersionNumber?)` — saves the child's upsert
  view model with the relation pointing at the new parent (so validation, the binder's **cycle guard** and the
  mapper apply; "Moved"), then, for an ordered relation, reorders the new siblings to put it at `position`.

**Admin surface.** The `Edit` GET builds one `RelationChildrenPanelViewModel` per containment targeting the
type (`ContentRelationRegistry.GetContainmentsTargeting`, skipping `ShowInParentAdmin = false`) and puts them in
`ViewData`; `_AdminLayout` renders `Views/AdminShared/_RelationChildren.cshtml` under the form (never in the
picker modal). Each panel lists the children (edit link, state), a **New {child}** link
(`/wadmin/{childType}/create?{RelationName}={parentId}`; the `Create` GET runs `RelationPreselect.Apply`,
which fills any relation property named in the query), and, for an ordered relation the viewer may write, Up/Down
buttons (`relation-children.js`, copied by `CopyViewScripts`) that post the whole order to
`POST /wadmin/{childType}/relations/{relationName}/{parentNodeId}/reorder`.

**Content zone items** are the top-level handler `ContentZoneItemModel` (`contentzoneitems`, Admin-only,
`SupportsPublishing = false`: items go live with their page, or at once in a global zone). Its
`ContentZoneNodeId` is the ordered `Zone` containment, hidden on the form and `ShowInParentAdmin = false` (the
zone editor is their editor). Every write delegates to `ContentZoneModel` / `IContentZoneService`, so page-draft
semantics, page history, slot remap and config sanitize/extract all stay: create places the item last in the
named zone, an update never moves it, delete is a **removal** (an inactive version), `MoveRelatedAsync` is
`IContentZoneService.MoveItemAsync` with the expected version, restore is
`IContentZoneService.RestoreItemVersionAsync` (content, metadata, active state, zone and ordinal; see
[04 → History in the Zone Editor](04-content-zone-framework.md#8b-history-in-the-zone-editor)), and a version a
page-history entry captured cannot be deleted. Its index lists one zone (`?ContentZoneNodeId={zone}`).

---

## 4. `IAdminRegistryHandler`

Optional extension point for handlers that need to expose a registry as JSON endpoints. Three handlers use it: `PageModel` (via `PageRegistryHandler`, over `IPageControllerRegistry`) feeds the admin UI's page-type picker; `ContentZoneModel` (via `ContentZoneRegistryHandler`, over `IWidgetRegistry`) feeds the add-widget picker; and `WidgetRegistrationModel` exposes its own registry view.

```csharp
public interface IAdminRegistryHandler
{
    IActionResult GetAll();                        // GET /wadmin/{contentType}/registry
    IActionResult GetProperties(string name);      // GET /wadmin/{contentType}/registry/{name}/properties
}
```

---

## 5. `AdminHandlerRegistry`

```csharp
public class AdminHandlerRegistry : IAdminHandlerRegistry
{
    private readonly Dictionary<string, IAdminCrudHandler> _handlers;
    public AdminHandlerRegistry(IEnumerable<IAdminCrudHandler> handlers) { ... }
    public IAdminCrudHandler? GetHandler(string contentType) => ...;
}
```

Constructed from all `IAdminCrudHandler` instances resolved from DI at the start of each request (scoped). The dictionary is case-insensitive on `ContentType`.

---

## 6. `AdminContentController` Route Map

All routes are prefixed with `/wadmin` and require `[Authorize(Roles = "Admin")]`.

**Top-level CRUD:**

| Method | Route | Action |
|--------|-------|--------|
| GET | `/wadmin/{contentType}` | `Index` — list view |
| GET | `/wadmin/{contentType}/create` | `Create` — empty create form |
| GET | `/wadmin/{contentType}/edit/{id:guid}` | `Edit` — populated edit form |
| POST | `/wadmin/{contentType}/edit/{id:guid?}` | `EditPost` — save (create or update); an optional local `returnUrl` form field is where it redirects |
| POST | `/wadmin/{contentType}/delete/{id:guid}` | `Delete` — delete (a page takes its subtree); optional local `returnUrl` |
| POST | `/wadmin/{contentType}/relations/{relationName}/{parentNodeId:guid}/reorder` | `ReorderRelated` — the children panel's reorder (JSON body: `List<Guid>`; 400 with the reason) |

**API endpoints:**

| Method | Route | Action |
|--------|-------|--------|
| GET | `/wadmin/{contentType}/api/list` | `ApiList` — entity picker list; `?excludeSubtreeOf={nodeId}` → `GetApiListExcludingSubtreeAsync` |
| GET | `/wadmin/{contentType}/api/{key}` | `SecondaryApiList` — named secondary list |

**Registry endpoints:**

| Method | Route | Action |
|--------|-------|--------|
| GET | `/wadmin/{contentType}/registry` | `RegistryList` |
| GET | `/wadmin/{contentType}/registry/{name}/properties` | `RegistryProperties` |

**Version history:**

| Method | Route | Action |
|--------|-------|--------|
| GET | `/wadmin/{contentType}/versions/{nodeId:guid}` | `VersionHistory` — optional `returnUrl` (local only) replaces the Back link and is carried through the forms |
| GET | `/wadmin/{contentType}/versions/{nodeId:guid}/edit/{id:guid}` | `VersionRestoreEdit` |
| POST | `/wadmin/{contentType}/versions/{nodeId:guid}/restore/{id:guid}` | `RestoreVersion` — one-step restore (`expectedVersionNumber`, `returnUrl` form fields) |
| POST | `/wadmin/{contentType}/versions/{nodeId:guid}/delete/{id:guid}` | `VersionDelete` — a refusal's reason goes to `TempData["Error"]`; optional `returnUrl` |

**Publishing & preview:**

| Method | Route | Action |
|--------|-------|--------|
| POST | `/wadmin/{contentType}/publish/{nodeId:guid}` | `Publish` — optional `returnUrl` form field: redirects back there when it is a local URL, else to the index |
| POST | `/wadmin/{contentType}/unpublish/{nodeId:guid}` | `Unpublish` — same `returnUrl` handling |
| GET | `/wadmin/{contentType}/preview/{nodeId:guid}` | `Preview` — redirects to `/_preview/{nodeId}` (no cookie; the route transformer marks that request as a draft read) (gated on `SupportsPreview`) |

The old child routes (`/wadmin/{contentType}/{parentKey}/{childType}/…`) are gone: a child is edited under
its own content type (e.g. a zone item at `/wadmin/contentzoneitems/edit/{id}`), see §3.

The page-history screens live on their own controller, `AdminPageHistoryController`
(`/wadmin/contentzones/page/{pageNodeId}/history…`) — see
[04 → History in the Zone Editor](04-content-zone-framework.md#8b-history-in-the-zone-editor).

---

## 7. Version History Support

Version history is opt-in per handler. `AdminCrudModel<T>` sets `SupportsVersionHistory = true` by default; `IAdminCrudHandler` default interface implementation sets it to `false`.

Flow:
1. User visits `/wadmin/{contentType}/versions/{nodeId}`
2. Controller checks `handler.SupportsVersionHistory`; returns 404 if false
3. Calls `GetVersionHistoryViewModelAsync` → renders shared `VersionHistory.cshtml`
4. User clicks "Restore" → `RestoreVersion` restores in **one step** (`RestoreVersionAsync(versionId, expectedVersionNumber)`,
   with the current draft's version number — `VersionHistoryViewModel.CurrentVersionNumber`, or the row's own
   `NodeCurrentVersionNumber` on a combined container timeline — so a concurrent edit is refused); the new
   version is a draft. Restore is hidden on the latest version, like Delete
5. `VersionRestoreEdit` (not linked from the history page) still renders the upsert form pre-filled with a
   historical version via `GetRestoreVersionViewModelAsync`; saving it creates a new version
6. User can delete individual versions via `VersionDelete` → `TryDeleteVersionAsync`

**Restore-edit forms stamp the *current* draft's version number.** The centralized
`AdminCrudModel.LoadRestoreVersionAsync(historicalId)` helper loads both the historical version and
the node's current draft, and every handler's `GetRestoreVersionViewModelAsync` stamps the returned
ViewModel's `ExpectedVersionNumber` with the **current** draft's `VersionNumber` (never the historical
row's). This is what lets a restore-edit of an *older* version save cleanly instead of failing the
concurrency check with "changed by someone else".

---

## 8. `AdminContentZoneController`

Handles the admin inline zone editing views. Delegates to `IContentZoneModel` for zone metadata, **`IContentStore<PageDTO>`** for the page, and **`IContentZoneService`** for the page draft lifecycle. This controller is separate from `AdminContentController` because zone editing uses a specialized UI rather than the standard upsert form.

| Method | Route | Action |
|--------|-------|--------|
| GET | `/wadmin/contentzones/zone-edit/{id:guid}` | `ZoneEdit` — one zone (`~/Views/AdminContentZone/ZoneEdit.cshtml`); a draft read (`PreviewConstants.MarkDraftRequest`), so page-owned items render their current draft and version |
| GET | `/wadmin/contentzones/page/{pageNodeId:guid}` | `PageZones` — the **page zone editor** (`PageZonesEdit.cshtml`); 404 for an unknown page |
| POST | `/wadmin/contentzones/page/{pageNodeId:guid}/discard` | `DiscardPageZones` — `IPageHistoryService.DiscardUnpublishedChangesAsync` (return to what is live, recorded in page history), then back to the editor |

**Page zone editor.** Opened from a page's title (or its **Edit zones** button) on the Pages list. It lists the page's
owned zones (`IContentZoneModel.GetPageZoneSlotsAsync`, ordered by slot name) and renders each in
the unchanged `ContentZone` editor (`editMode: true`), one box per slot; nested zones inside Layout
widgets are editable inline as usual. Widgets can be dragged (grip handle; mouse or touch) within a zone
and between any of the page's zones, nested columns included, or moved with ArrowUp/ArrowDown on a
focused handle — each move is a draft via `POST /api/contentzones/items/{id}/move` (see
[04 → Moving Items](04-content-zone-framework.md#8a-moving-items-drag-and-drop)). The request sets `PreviewConstants.DraftRequestItemKey`, so
`PreviewAwareReadContext` serves drafts for it alone. Above the zones:

- **Toolbar** — status tag (Not published / Pending changes / Up to date), **Publish** and
  **Unpublish** (post to the existing `AdminContentController` actions with `returnUrl` = the editor),
  **Discard changes** (only when zone drafts are pending; confirm first), **Edit page**, **Back to Pages**.
- **Tabs** — *Edit zones* (default) and *Preview*, an iframe of `/wadmin/pages/preview/{id}` (redirects
  to `/_preview/{id}`, which alone reads drafts — the live site is unaffected), reloaded each time the tab opens. The active tab is kept
  in the URL hash (`#preview`) so the reload after every zone edit keeps it.
- **Empty page** — slots are created lazily by the page template, so with none yet the editor loads the
  preview in the background and reloads once (sessionStorage guard) to show the slots it created.

Behaviour lives in `Views/AdminContentZone/PageZonesEdit.js` (copied to `wwwroot/js/page-zones-edit.js`
at build; vanilla JS, no inline script).

---

## 9. Inline Edit API (`ContentZoneApiController`)

JSON-only API for the inline zone edit mode. Authenticated but not form-POST (no antiforgery on JSON endpoints, relies on `[Authorize]` and CORS).

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/contentzones/items/{itemId}` | Get a single zone item by ID |
| POST | `/api/contentzones/items` | Add a new zone item (JSON body: `{ zoneId, componentName, componentPropertiesJson }`) |
| DELETE | `/api/contentzones/items/{itemId}` | Remove a zone item |

This differs from admin CRUD routes: it accepts and returns JSON, no model binding to ViewModels, and no redirect-after-post.

Adding or removing an item also calls `IRouteRegistrationService.TryRegisterWidgetRoutesAsync`, so a routable widget (one implementing `IRoutableViewComponent`, e.g. `Article`) in a global zone has its routes kept in step; items in a page's zones register their sub-routes beneath the page's URL when the page is published. See [Area 3](03-page-routing.md#10-irouteregistrationservice-and-routable-widgets).

---

## 10. Authorization

- All admin routes require `[Authorize(Roles = "Admin")]` at the controller level
- Write operations (POST edit, POST delete) additionally check `handler.WriteRoles`:
  - `null` → only users in `Admin` role may write
  - `["Admin", "Editor"]` → users in either role may write
  - Returns `Forbid()` (403) if the check fails

`UserService.IsUserAdmin` and `IsUserAuthor` are available in views for conditional rendering (e.g., showing/hiding edit buttons). See [Area 8](08-identity-auth.md).

---

## 11. Registering a New Content Type

1. Create the domain model extending `AdminCrudModel<TDto>` (see [Area 5](05-content-domain-models.md)).
   Its constructor passes `(IChangeSetScope, IRelationBinder, IContentDeleteService)` to the base, and it
   implements `DeleteSelfAsync` (delete this one node; the base `DeleteAsync` adds the relation cascade).
   A link to other content is a `[ContentRelation]` property on the upsert view model, not a column.
2. Register in DI — in `Program.cs` or a custom extension method:
   ```csharp
   services.AddScoped<MyThingModel>();
   services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<MyThingModel>());
   ```
3. Create Razor views at the paths returned by `IndexViewPath` and `UpsertViewPath` (follow the
   list page pattern below for the index view)
4. The routes appear automatically; no controller changes required

### List page pattern

Every admin list (Pages, Content Blocks, Images, Zones, Articles, Routes, the registries) uses one row layout,
styled by the "Admin List Pattern" section of `WebWayCMS.Admin/wwwroot/css/admin.css`:

- **Title cell** (`td.admin-list-main`): the title is an `a.admin-list-title` linking to the item's main editing
  screen (a page → its zone editor, a zone → the zone editor, an article list → the Articles page filtered to it, everything else → Edit).
  Secondary text goes in `span.admin-list-sub` (add `admin-list-path` for monospace paths).
- **Status**: `<partial name="_StatusBadge" model="(isPublished, hasPendingChanges)" />` renders
  Draft / Live / Live · Changes pending.
- **Actions** (`div.admin-row-actions` in the last cell, header `th.admin-actions-col`): the frequent actions stay
  visible — **View ↗** where the item has a public URL (live pages, literal non-reserved routes), exactly one of
  **Publish** (draft or pending changes) / **Unpublish** (live, nothing pending) for publishable types, and the type's
  primary button (**Edit zones** for pages, **Edit zone** for zones, **Articles** for article lists, **Edit** otherwise).
  Then a `⋯` Bulma `dropdown is-right admin-row-menu` with a `[data-row-menu-trigger]` button holds the rest (Preview,
  Settings, Version history, …), with Delete (`data-delete-trigger`) — and, for a page with pending changes, Unpublish —
  after a `dropdown-divider`. `admin.js` wires every row menu: one open at
  a time, outside click / Escape to close, arrow keys between items, and an upward flip near the viewport bottom.
- Wrap the table in `div.table-container.admin-list-container` (overflow visible so menus aren't clipped) with
  `table.admin-list-table`; mark secondary columns `is-hidden-mobile`. Below 769px rows stack (title, then
  status + actions).

*See also:* [docs/content-system.md](../content-system.md) for the full walkthrough.
