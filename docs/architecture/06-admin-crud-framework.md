# Area 6: Admin CRUD Framework

**Namespaces:**
- `WebWayCMS.Controllers.Admin` — `AdminContentController`, `AdminContentZoneController`
- `WebWayCMS.Controllers.Admin.Handlers` — `IAdminCrudHandler`, `IAdminCrudChildHandler`, `IAdminHandlerRegistry`, `IAdminRegistryHandler`, `AdminHandlerRegistry`, `AdminSaveResult`
- `WebWayCMS.Controllers.Api` — `ContentZoneApiController`

**Depends on:** Content Domain Models (resolves handlers), Content Zone Component Framework (zone controller uses registry), Identity (`[Authorize]`), Form Generation Metadata (tag helper in views)
**Consumed by:** Nothing (leaf layer; handles HTTP requests)

---

## 1. Framework Overview

The admin CRUD framework handles all content management HTTP routes through a single `AdminContentController`. New content types do not require new controllers — they register an `IAdminCrudHandler` implementation in DI, and the framework routes automatically apply.

`AdminHandlerRegistry` is the dispatch table: a dictionary keyed on `ContentType` string (case-insensitive), built from all `IAdminCrudHandler` instances in the DI container at startup.

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

    bool SupportsVersionHistory => false;
    Task<VersionHistoryViewModel?> GetVersionHistoryViewModelAsync(Guid masterId, CancellationToken ct = default);
    Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default);
    Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default);
}
```

**Semantics:**
- `GetIndexViewModelAsync(IQueryCollection)` — default implementation delegates to the parameterless overload; override to support filtering via query string
- `GetUpsertViewModelAsync(null, ...)` — returns an empty create form; non-null `id` returns the edit form, or `null` if not found (triggers 404)
- `CreateEmptyUpsertViewModel()` — returns a fresh ViewModel instance for `TryUpdateModelAsync` binding; must match the type `SaveUpsertAsync` expects
- `SaveUpsertAsync` — receives the model-bound ViewModel; returns `AdminSaveResult(true)` on success or `AdminSaveResult(false, message, field?)` on failure
- `GetApiListAsync` — returns `[ { id, title } ]` used by GUID entity pickers in admin forms
- `HasSecondaryApiList` / `GetSecondaryApiListAsync(key)` — extension for handlers needing multiple named lists (e.g., `ArticleListModel` exposes `"articlelists"`)

---

## 3. `IAdminCrudChildHandler` — Child Resource Contract

Manages entities that belong to a parent. The parent handler exposes `ChildHandler`; the controller dispatches child routes to it.

```csharp
public interface IAdminCrudChildHandler
{
    string ChildType { get; }            // URL segment, e.g. "articles"
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

    bool SupportsVersionHistory => false;
    Task<VersionHistoryViewModel?> GetChildVersionHistoryViewModelAsync(string parentKey, Guid masterId, CancellationToken ct = default);
    Task<object?> GetChildRestoreVersionViewModelAsync(string parentKey, Guid historicalId, CancellationToken ct = default);
    Task<bool> DeleteChildVersionAsync(Guid id, CancellationToken ct = default);
}
```

`parentKey` is a slug (for articles) or Guid string (for zone items) — determined by the child handler's own interpretation.

`SetChildUpsertViewDataAsync` is called before rendering the upsert view to allow async data loading into `ViewData` (e.g., fetching the parent's title for breadcrumb display).

---

## 4. `IAdminRegistryHandler`

Optional extension point for handlers that need to expose a component/controller registry as JSON endpoints. `PageModel` uses this to feed the admin UI's page-type picker with available controllers and their config properties.

```csharp
public interface IAdminRegistryHandler
{
    IActionResult GetAll();                        // GET /admin/{contentType}/registry
    IActionResult GetProperties(string name);      // GET /admin/{contentType}/registry/{name}/properties
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

All routes are prefixed with `/admin` and require `[Authorize(Roles = "Admin")]`.

**Top-level CRUD:**

| Method | Route | Action |
|--------|-------|--------|
| GET | `/admin/{contentType}` | `Index` — list view |
| GET | `/admin/{contentType}/create` | `Create` — empty create form |
| GET | `/admin/{contentType}/edit/{id:guid}` | `Edit` — populated edit form |
| POST | `/admin/{contentType}/edit/{id:guid?}` | `EditPost` — save (create or update) |
| POST | `/admin/{contentType}/delete/{id:guid}` | `Delete` — delete |

**API endpoints:**

| Method | Route | Action |
|--------|-------|--------|
| GET | `/admin/{contentType}/api/list` | `ApiList` — entity picker list |
| GET | `/admin/{contentType}/api/{key}` | `SecondaryApiList` — named secondary list |

**Registry endpoints:**

| Method | Route | Action |
|--------|-------|--------|
| GET | `/admin/{contentType}/registry` | `RegistryList` |
| GET | `/admin/{contentType}/registry/{name}/properties` | `RegistryProperties` |

**Version history:**

| Method | Route | Action |
|--------|-------|--------|
| GET | `/admin/{contentType}/versions/{masterId:guid}` | `VersionHistory` |
| GET | `/admin/{contentType}/versions/{masterId:guid}/edit/{id:guid}` | `VersionRestoreEdit` |
| POST | `/admin/{contentType}/versions/{masterId:guid}/delete/{id:guid}` | `VersionDelete` |

**Child CRUD:**

| Method | Route | Action |
|--------|-------|--------|
| GET | `/admin/{contentType}/{parentKey:notreserved}/{childType}` | `ChildIndex` |
| GET | `/admin/{contentType}/{parentKey:notreserved}/{childType}/create` | `ChildCreate` |
| GET | `/admin/{contentType}/{parentKey:notreserved}/{childType}/edit/{id:guid}` | `ChildEdit` |
| POST | `/admin/{contentType}/{parentKey:notreserved}/{childType}/edit/{id:guid?}` | `ChildEditPost` |
| POST | `/admin/{contentType}/{parentKey:notreserved}/{childType}/delete/{id:guid}` | `ChildDelete` |
| POST | `/admin/{contentType}/{parentKey:notreserved}/{childType}/reorder` | `ChildReorder` (JSON body: `List<Guid>`) |

**Child version history:**

| Method | Route | Action |
|--------|-------|--------|
| GET | `/admin/{contentType}/{parentKey:notreserved}/{childType}/versions/{masterId:guid}` | `ChildVersionHistory` |
| GET | `/admin/{contentType}/{parentKey:notreserved}/{childType}/versions/{masterId:guid}/edit/{id:guid}` | `ChildVersionRestoreEdit` |
| POST | `/admin/{contentType}/{parentKey:notreserved}/{childType}/versions/{masterId:guid}/delete/{id:guid}` | `ChildVersionDelete` |

---

## 7. Version History Support

Version history is opt-in per handler. `AdminCrudModel<T>` sets `SupportsVersionHistory = true` by default; `IAdminCrudHandler` default interface implementation sets it to `false`.

Flow:
1. User visits `/admin/{contentType}/versions/{masterId}`
2. Controller checks `handler.SupportsVersionHistory`; returns 404 if false
3. Calls `GetVersionHistoryViewModelAsync` → renders shared `VersionHistory.cshtml`
4. User clicks "Restore" → `VersionRestoreEdit` loads the historical version via `GetRestoreVersionViewModelAsync`
5. The restore view renders the existing upsert form pre-filled with historical data; saving it creates a new version on top of the current latest
6. User can delete individual versions via `VersionDelete` → `DeleteVersionAsync`

Child resources follow the same pattern with `ChildVersionHistory*` routes.

---

## 8. Child CRUD Routes — `{parentKey:notreserved}` Pattern

The `{parentKey}` segment carries the parent identifier (slug or Guid string). The `notreserved` constraint prevents ambiguity with reserved action names (`edit`, `delete`, `create`, `registry`, `api`, `reorder`, `versions`). See [Area 3](03-page-routing.md#7-notreservedconstraint) for the constraint definition.

Example URLs:
- `/admin/articles/my-blog/articles` — list articles in the "my-blog" article list
- `/admin/articles/my-blog/articles/create` — create article
- `/admin/articles/my-blog/articles/reorder` — reorder (POST, JSON body)

---

## 9. `AdminContentZoneController`

Handles the admin inline zone editing view at `/admin/contentzones/edit/{zoneId}`. Delegates to `ContentZoneModel` for zone metadata and `IContentZoneComponentRegistry` for the available component list. This controller is separate from `AdminContentController` because zone editing uses a specialized split-panel UI rather than the standard upsert form.

---

## 10. Inline Edit API (`ContentZoneApiController`)

JSON-only API for the inline zone edit mode. Authenticated but not form-POST (no antiforgery on JSON endpoints, relies on `[Authorize]` and CORS).

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/contentzones/items/{itemId}` | Get a single zone item by ID |
| POST | `/api/contentzones/items` | Add a new zone item (JSON body: `{ zoneId, componentName, componentPropertiesJson }`) |
| DELETE | `/api/contentzones/items/{itemId}` | Remove a zone item |

This differs from admin CRUD routes: it accepts and returns JSON, no model binding to ViewModels, and no redirect-after-post.

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
3. Create Razor views at the paths returned by `IndexViewPath` and `UpsertViewPath`
4. The routes appear automatically; no controller changes required

*See also:* [docs/content-system.md](../content-system.md) for the full walkthrough.
