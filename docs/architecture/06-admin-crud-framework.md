# Area 6: Admin CRUD Framework

**Namespaces:**
- `WebWayCMS.Controllers.Admin` — `AdminContentController`, `AdminContentZoneController` (in **`WebWayCMS.Admin`**)
- `WebWayCMS.Controllers.Admin.Handlers` — `IAdminCrudHandler`, `IAdminCrudChildHandler`, `IAdminHandlerRegistry`, `IAdminRegistryHandler`, `AdminSaveResult` (contracts, in **`WebWayCMS.Core`**); `AdminHandlerRegistry` (implementation, in **`WebWayCMS.Admin`**)
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
    IAdminCrudChildHandler? ChildHandler { get; }

    bool SupportsPublishing => true;
    bool SupportsPreview => false;
    string[]? PublishRoles => WriteRoles;
    Task<AdminSaveResult> PublishAsync(Guid nodeId, CancellationToken ct = default);
    Task<AdminSaveResult> UnpublishAsync(Guid nodeId, CancellationToken ct = default);
    Task<AdminSaveResult> RestoreVersionAsync(Guid versionId, CancellationToken ct = default);

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
- `SaveUpsertAsync` — receives the model-bound ViewModel; returns `AdminSaveResult(true)` on success or `AdminSaveResult(false, message, field?)` on failure
- `GetApiListAsync` — returns `[ { id, title } ]` used by GUID entity pickers in admin forms
- `HasSecondaryApiList` / `GetSecondaryApiListAsync(key)` — extension for handlers needing multiple named lists (none of the built-in handlers uses it; prefer a separate content type with its own `GetApiListAsync`)
- `GetIndexViewModelAsync(IQueryCollection query)` — a default-implemented overload the controller calls with the request query; a handler re-implements it (explicitly, re-listing `IAdminCrudHandler`) to filter its index — e.g. `ArticleModel` honours `?list={nodeId}`, `ContentZoneModel` `?pageId=`/`?zoneId=`

---

## 3. `IAdminCrudChildHandler` — Child Resource Contract

Manages entities that belong to a parent. The parent handler exposes `ChildHandler`; the controller dispatches child routes to it.

```csharp
public interface IAdminCrudChildHandler
{
    string ChildType { get; }            // URL segment, e.g. "items"
    string ChildDisplayName { get; }
    string[]? WriteRoles { get; }
    string ChildIndexViewPath { get; }
    string ChildUpsertViewPath { get; }

    Task<object?> GetChildIndexViewModelAsync(string parentKey, CancellationToken ct = default);
    Task<object?> GetChildUpsertViewModelAsync(string parentKey, Guid? id, CancellationToken ct = default);
    Task SetChildUpsertViewDataAsync(ViewDataDictionary viewData, string parentKey, CancellationToken ct = default);
    object CreateEmptyChildUpsertViewModel();
    Task<AdminSaveResult> SaveChildUpsertAsync(string parentKey, object model, CancellationToken ct = default);
    Task<bool> DeleteChildAsync(Guid id, CancellationToken ct = default);

    bool SupportsReorder { get; }
    Task<bool> ReorderAsync(string parentKey, List<Guid> orderedIds, CancellationToken ct = default);

    // optional: move a child to a 0-based position under another parent (MCP move_child)
    bool SupportsMove => false;
    Task<(bool Success, string? ErrorMessage)> MoveAsync(Guid childId, string targetParentKey, int position, CancellationToken ct = default);

    bool SupportsVersionHistory => false;
    Task<VersionHistoryViewModel?> GetChildVersionHistoryViewModelAsync(string parentKey, Guid nodeId, CancellationToken ct = default);
    Task<object?> GetChildRestoreVersionViewModelAsync(string parentKey, Guid historicalId, CancellationToken ct = default);
    Task<bool> DeleteChildVersionAsync(Guid id, CancellationToken ct = default);

    // one-step restore (history page Restore button, MCP restore_child_version); the default saves the
    // GetChildRestoreVersionViewModelAsync form through SaveChildUpsertAsync
    Task<AdminSaveResult> RestoreChildVersionAsync(string parentKey, Guid versionId, int? expectedVersionNumber, CancellationToken ct = default);
    // DeleteChildVersionAsync with the reason a delete was refused
    Task<(bool Success, string? ErrorMessage)> TryDeleteChildVersionAsync(Guid id, CancellationToken ct = default);
}
```

The zone-item child handler overrides both: restore goes through
`IContentZoneService.RestoreItemVersionAsync` (content, metadata, active state, zone and ordinal; see
[04 → History in the Zone Editor](04-content-zone-framework.md#8b-history-in-the-zone-editor)), and a
version a page-history entry captured cannot be deleted.

`parentKey` is interpreted by the child handler (zone items, the only shipped child handler, use the zone's Guid string). Prefer a stable id: a slug changes when the parent is renamed.

`SetChildUpsertViewDataAsync` is called before rendering the upsert view to allow async data loading into `ViewData` (e.g., fetching the parent's title for breadcrumb display).

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
| POST | `/wadmin/{contentType}/edit/{id:guid?}` | `EditPost` — save (create or update) |
| POST | `/wadmin/{contentType}/delete/{id:guid}` | `Delete` — delete |

**API endpoints:**

| Method | Route | Action |
|--------|-------|--------|
| GET | `/wadmin/{contentType}/api/list` | `ApiList` — entity picker list |
| GET | `/wadmin/{contentType}/api/{key}` | `SecondaryApiList` — named secondary list |

**Registry endpoints:**

| Method | Route | Action |
|--------|-------|--------|
| GET | `/wadmin/{contentType}/registry` | `RegistryList` |
| GET | `/wadmin/{contentType}/registry/{name}/properties` | `RegistryProperties` |

**Version history:**

| Method | Route | Action |
|--------|-------|--------|
| GET | `/wadmin/{contentType}/versions/{nodeId:guid}` | `VersionHistory` |
| GET | `/wadmin/{contentType}/versions/{nodeId:guid}/edit/{id:guid}` | `VersionRestoreEdit` |
| POST | `/wadmin/{contentType}/versions/{nodeId:guid}/restore/{id:guid}` | `RestoreVersion` |
| POST | `/wadmin/{contentType}/versions/{nodeId:guid}/delete/{id:guid}` | `VersionDelete` |

**Publishing & preview:**

| Method | Route | Action |
|--------|-------|--------|
| POST | `/wadmin/{contentType}/publish/{nodeId:guid}` | `Publish` — optional `returnUrl` form field: redirects back there when it is a local URL, else to the index |
| POST | `/wadmin/{contentType}/unpublish/{nodeId:guid}` | `Unpublish` — same `returnUrl` handling |
| GET | `/wadmin/{contentType}/preview/{nodeId:guid}` | `Preview` — redirects to `/_preview/{nodeId}` (no cookie; the route transformer marks that request as a draft read) (gated on `SupportsPreview`) |

**Child CRUD:**

| Method | Route | Action |
|--------|-------|--------|
| GET | `/wadmin/{contentType}/{parentKey:notreserved}/{childType}` | `ChildIndex` |
| GET | `/wadmin/{contentType}/{parentKey:notreserved}/{childType}/create` | `ChildCreate` |
| GET | `/wadmin/{contentType}/{parentKey:notreserved}/{childType}/edit/{id:guid}` | `ChildEdit` |
| POST | `/wadmin/{contentType}/{parentKey:notreserved}/{childType}/edit/{id:guid?}` | `ChildEditPost` |
| POST | `/wadmin/{contentType}/{parentKey:notreserved}/{childType}/delete/{id:guid}` | `ChildDelete` |
| POST | `/wadmin/{contentType}/{parentKey:notreserved}/{childType}/reorder` | `ChildReorder` (JSON body: `List<Guid>`) |

**Child version history:**

| Method | Route | Action |
|--------|-------|--------|
| GET | `/wadmin/{contentType}/{parentKey:notreserved}/{childType}/versions/{nodeId:guid}` | `ChildVersionHistory` — optional `returnUrl` (local only) replaces the Back link and is carried through the forms |
| POST | `/wadmin/{contentType}/{parentKey:notreserved}/{childType}/versions/{nodeId:guid}/restore/{id:guid}` | `ChildVersionRestore` — one-step restore (`expectedVersionNumber`, `returnUrl` form fields) |
| POST | `/wadmin/{contentType}/{parentKey:notreserved}/{childType}/versions/{nodeId:guid}/delete/{id:guid}` | `ChildVersionDelete` — a refusal's reason goes to `TempData["Error"]` |

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
4. User clicks "Restore" → `VersionRestoreEdit` loads the historical version via `GetRestoreVersionViewModelAsync`
5. The restore view renders the existing upsert form pre-filled with historical data; saving it creates a new version on top of the current latest
6. User can delete individual versions via `VersionDelete` → `DeleteVersionAsync`

**Restore-edit forms stamp the *current* draft's version number.** The centralized
`AdminCrudModel.LoadRestoreVersionAsync(historicalId)` helper loads both the historical version and
the node's current draft, and every handler's `GetRestoreVersionViewModelAsync` stamps the returned
ViewModel's `ExpectedVersionNumber` with the **current** draft's `VersionNumber` (never the historical
row's). This is what lets a restore-edit of an *older* version save cleanly instead of failing the
concurrency check with "changed by someone else".

Child resources list their history the same way (`ChildVersionHistory`), but restore in **one step**:
the history page's Restore button posts `ChildVersionRestore` → `RestoreChildVersionAsync` with the
current draft's version number (`VersionHistoryViewModel.CurrentVersionNumber`) as the expected version.
There is no child restore-edit form. Restore is hidden on the latest version, like Delete.

---

## 8. Child CRUD Routes — `{parentKey:notreserved}` Pattern

The `{parentKey}` segment carries the parent identifier (slug or Guid string). The `notreserved` constraint prevents ambiguity with reserved action names (`edit`, `delete`, `create`, `registry`, `api`, `reorder`, `versions`). See [Area 3](03-page-routing.md#11-notreservedconstraint) for the constraint definition.

Example URLs:
- `/wadmin/contentzones/{zoneId}/items` — list the items of a zone
- `/wadmin/contentzones/{zoneId}/items/create` — create an item
- `/wadmin/contentzones/{zoneId}/items/reorder` — reorder (POST, JSON body)

---

## 9. `AdminContentZoneController`

Handles the admin inline zone editing views. Delegates to `IContentZoneModel` for zone metadata, **`IContentStore<PageDTO>`** for the page, and **`IContentZoneService`** for the page draft lifecycle. This controller is separate from `AdminContentController` because zone editing uses a specialized UI rather than the standard upsert form.

| Method | Route | Action |
|--------|-------|--------|
| GET | `/wadmin/contentzones/zone-edit/{id:guid}` | `ZoneEdit` — one zone (`~/Views/AdminContentZone/ZoneEdit.cshtml`); a draft read (`PreviewConstants.MarkDraftRequest`), so page-owned items render their current draft and version |
| GET | `/wadmin/contentzones/page/{pageNodeId:guid}` | `PageZones` — the **page zone editor** (`PageZonesEdit.cshtml`); 404 for an unknown page |
| POST | `/wadmin/contentzones/page/{pageNodeId:guid}/discard` | `DiscardPageZones` — `IPageHistoryService.DiscardUnpublishedChangesAsync` (return to what is live, recorded in page history), then back to the editor |

**Page zone editor.** Opened from a page's title (or its **Edit zones** button) on the Pages list. It lists the page's
zone assignments (`IContentZoneModel.GetPageZoneSlotsAsync`, ordered by slot name) and renders each in
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

## 10. Inline Edit API (`ContentZoneApiController`)

JSON-only API for the inline zone edit mode. Authenticated but not form-POST (no antiforgery on JSON endpoints, relies on `[Authorize]` and CORS).

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/contentzones/items/{itemId}` | Get a single zone item by ID |
| POST | `/api/contentzones/items` | Add a new zone item (JSON body: `{ zoneId, componentName, componentPropertiesJson }`) |
| DELETE | `/api/contentzones/items/{itemId}` | Remove a zone item |

This differs from admin CRUD routes: it accepts and returns JSON, no model binding to ViewModels, and no redirect-after-post.

Adding or removing an item also calls `IRouteRegistrationService.TryRegisterWidgetRoutesAsync`, so a routable widget (one implementing `IRoutableViewComponent`, e.g. `Article`) in a global zone has its routes kept in step; items in a page's zones register their sub-routes beneath the page's URL when the page is published. See [Area 3](03-page-routing.md#10-irouteregistrationservice-and-routable-widgets).

---

## 11. Authorization

- All admin routes require `[Authorize(Roles = "Admin")]` at the controller level
- Write operations (POST edit, POST delete) additionally check `handler.WriteRoles`:
  - `null` → only users in `Admin` role may write
  - `["Admin", "Editor"]` → users in either role may write
  - Returns `Forbid()` (403) if the check fails

`UserService.IsUserAdmin` and `IsUserAuthor` are available in views for conditional rendering (e.g., showing/hiding edit buttons). See [Area 8](08-identity-auth.md).

---

## 12. Registering a New Content Type

1. Create the domain model extending `AdminCrudModel<TDto>` (see [Area 5](05-content-domain-models.md))
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
