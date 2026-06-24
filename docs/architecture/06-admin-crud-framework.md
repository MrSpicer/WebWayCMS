# Area 6: Admin CRUD Framework

**Namespaces:**
- `WebWayCMS.Controllers.Admin.Handlers` — `IAdminCrudHandler`, `IAdminCrudChildHandler`, `IAdminHandlerRegistry`, `IAdminRegistryHandler`, `AdminHandlerRegistry`, `AdminSaveResult`
- `WebWayCMS.Presentation.Components.Admin` — the Blazor admin pages (list / `AdminUpsert` / `AdminChildUpsert` / `AdminVersionHistoryPage` / `ContentZoneEditor`)
- `WebWayCMS.Presentation.Rendering` — `AdminRoutes`, `IFormOptionsProvider`

**Depends on:** Content Domain Models (resolves handlers), Content Zone Component Framework (zone editor uses registry), Identity (`[Authorize]`), Form Generation Metadata (`InteractiveFormFields`)
**Consumed by:** Nothing (leaf layer; the admin Blazor UI)

---

## 1. Framework Overview

The admin CRUD framework manages all content through **Interactive Server Blazor pages** in
`WebWayCMS.Presentation/Components/Admin/`. New content types do not require new pages for the generic
flows — they register an `IAdminCrudHandler` implementation in DI, and the generic `AdminUpsert` /
`AdminChildUpsert` / `AdminVersionHistoryPage` components drive the create/edit/version flows by
resolving that handler. (Per-content-type **routable** wrapper pages supply the fixed route +
`ContentType` to those generic components — see §6.)

`AdminHandlerRegistry` is the dispatch table: a dictionary keyed on `ContentType` string
(case-insensitive), built from all `IAdminCrudHandler` instances in the DI container per request.

> The former MVC controllers `AdminContentController`, `AdminContentZoneController`, and
> `ContentZoneApiController` have been **deleted**. The handler interfaces below are unchanged; only
> the transport (Blazor components instead of MVC actions/views) changed.

---

## 2. `IAdminCrudHandler` Full Method Reference

```csharp
public interface IAdminCrudHandler
{
    string ContentType { get; }      // URL key, e.g. "contentblocks"
    string DisplayName { get; }      // Human-readable, e.g. "Content Block"
    string[]? WriteRoles { get; }    // null = Admin only; ["Admin","Editor"] to allow editors

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
- `GetUpsertViewModelAsync(null, ...)` — returns an empty create form; non-null `id` returns the edit form, or `null` if not found (the Blazor page shows a "not found" notice)
- `CreateEmptyUpsertViewModel()` — returns a fresh ViewModel instance bound by `AdminUpsert`'s edit form; must match the type `SaveUpsertAsync` expects
- `SaveUpsertAsync` — receives the bound ViewModel; returns `AdminSaveResult(true)` on success or `AdminSaveResult(false, message, field?)` on failure
- `GetApiListAsync` — returns `[ { id, title } ]` used by GUID entity pickers (`IFormOptionsProvider`) in admin forms
- `HasSecondaryApiList` / `GetSecondaryApiListAsync(key)` — extension for handlers needing multiple named lists (e.g., `ArticleListModel` exposes `"articlelists"`)

---

## 3. `IAdminCrudChildHandler` — Child Resource Contract

Manages entities that belong to a parent. The parent handler exposes `ChildHandler`; the child Blazor
pages (`AdminChildUpsert`, the child list/version pages) dispatch to it.

```csharp
public interface IAdminCrudChildHandler
{
    string ChildType { get; }            // URL segment, e.g. "articles"
    string ChildDisplayName { get; }
    string[]? WriteRoles { get; }

    Task<object?> GetChildIndexViewModelAsync(string parentKey, CancellationToken ct = default);
    Task<object?> GetChildUpsertViewModelAsync(string parentKey, Guid? id, CancellationToken ct = default);
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

`parentKey` is a slug (for articles) or Guid string (for zone items) — determined by the child handler's
own interpretation.

---

## 4. `IAdminRegistryHandler`

Optional extension point for handlers that expose a component/controller registry. `PageModel` uses it
to feed the admin page editor's page-type picker with available controllers and their config
properties.

```csharp
public interface IAdminRegistryHandler
{
    IActionResult GetAll();                        // all registered entries
    IActionResult GetProperties(string name);      // properties for one entry
}
```

> The standalone `/admin/{contentType}/registry...` HTTP endpoints that previously served this (and the
> old client-side `ViewPicker` script) are gone. Admin form option lists are now produced server-side,
> in-circuit, by `IFormOptionsProvider` (see [Area 2](02-form-generation.md)).

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

Constructed from all `IAdminCrudHandler` instances resolved from DI at the start of each request
(scoped). The dictionary is case-insensitive on `ContentType`. The generic admin components inject
`IAdminHandlerRegistry` and call `GetHandler(ContentType)`.

---

## 6. Blazor Admin Page Route Map

All admin pages carry `@attribute [Authorize(Roles = "Admin")]`, `@layout AdminLayout`, and
`@rendermode InteractiveServer`. Unlike the old generic `/admin/{contentType}` MVC routes, each content
type has **explicit** routes (mapped to `ContentType` keys by `AdminRoutes.ListUrl`):

| Content type | `ContentType` key | List | Create / Edit |
|---|---|---|---|
| Content Blocks | `contentblocks` | `/admin/blocks` | `/admin/blocks/create`, `/admin/blocks/edit/{id:guid}` |
| Article Lists | `articles` | `/admin/article-lists` | `/admin/article-lists/create`, `/admin/article-lists/edit/{id:guid}` |
| Content Zones | `contentzones` | `/admin/zones` | `/admin/zones/create`, `/admin/zones/edit/{id:guid}` |
| Site Pages | `pages` | `/admin/site-pages` | `/admin/site-pages/create`, `/admin/site-pages/edit/{id:guid}` |

**Child resources** (parent → child):

| Route | Page |
|---|---|
| `/admin/article-lists/{Slug}/articles` | `AdminArticlesPage` (child list) |
| `/admin/article-lists/{Slug}/articles/create`, `/edit/{Id:guid}` | `AdminArticleUpsertPage` |
| `/admin/zones/{ZoneId:guid}/items` | `AdminZoneItemsPage` (child list) |
| `/admin/zones/{ZoneId:guid}/items/edit/{Id:guid}` | `AdminZoneItemUpsertPage` |

**Content-zone inline editor:** `/admin/zone-editor/{ZoneId:guid}` (`AdminZoneEditorPage` → `ContentZoneEditor.razor`).

How a request flows: the routable list page (e.g. `AdminContentBlocksPage` at `/admin/blocks`) injects
its model and renders the list with an interactive delete (inline confirm, no reload). Create/Edit pages
(e.g. `AdminContentBlockUpsertPage`) render the generic `AdminUpsert` component, passing
`ContentType="contentblocks"`, the optional `Id`, and `ListUrl`. `AdminUpsert` resolves the handler,
loads/creates the upsert view model, renders the metadata-driven `InteractiveFormFields`, validates with
DataAnnotations, and calls `SaveUpsertAsync` in-circuit — replacing the old GET-render + EditPost.

---

## 7. Version History Support

Version history is opt-in per handler. `AdminCrudModel<T>` sets `SupportsVersionHistory = true` by
default; the `IAdminCrudHandler` default interface implementation returns `false`.

`AdminVersionHistoryPage` is the generic Blazor page (top-level **and** child) at:

- `/admin/versions/{ContentType}/{MasterId:guid}` — top-level (e.g. `/admin/versions/contentblocks/{id}`)
- `/admin/versions/{ContentType}/{ParentKey}/{ChildType}/{MasterId:guid}` — child (e.g. `/admin/versions/articles/{slug}/articles/{id}`)

Flow:
1. A list page's "Versions" link navigates to the route above
2. The page resolves the handler and checks `SupportsVersionHistory`
3. Calls `GetVersionHistoryViewModelAsync` and renders the version list
4. "Restore" loads the historical version via `GetRestoreVersionViewModelAsync` and reuses the upsert
   form pre-filled with historical data; saving it creates a new version on top of the current latest
5. Individual versions can be deleted via `DeleteVersionAsync`

Child resources follow the same pattern via the child route and the `GetChild...Version...` members.

---

## 8. Admin Route Map Examples

The Blazor admin pages declare **explicit** `@page` routes per content type (§6); there are no custom
route constraints. Example child URLs:
- `/admin/article-lists/my-blog/articles` — list articles in the "my-blog" article list
- `/admin/article-lists/my-blog/articles/create` — create article

---

## 9. Content-Zone Editing — `ContentZoneEditor.razor`

The former `AdminContentZoneController` (split-panel zone edit view) is replaced by
`ContentZoneEditor.razor`, hosted by `AdminZoneEditorPage` at `/admin/zone-editor/{ZoneId:guid}`
(Interactive Server). It uses `ContentZoneModel` for zone data and `IContentZoneComponentRegistry` for
the available-component picker; add/reorder/delete and the dynamic config form
(`InteractiveFormFields.razor`) are in-circuit C# event handlers. See [Area 4](04-content-zone-framework.md#10-inline-editing--contentzoneeditorrazor).

---

## 10. Inline Editing Transport

The old JSON `ContentZoneApiController` (`/api/contentzones/items/...`) is **deleted**. Because
`ContentZoneEditor` runs as an Interactive Server circuit, add/get/delete of zone items are direct C#
calls to `ContentZoneModel` over the SignalR connection — there is no separate JSON API surface, model
binding, or redirect-after-post.

---

## 11. Authorization

- All admin pages require `[Authorize(Roles = "Admin")]` via `@attribute` on the component
- Write operations additionally check `handler.WriteRoles`:
  - `null` → only users in `Admin` role may write
  - `["Admin", "Editor"]` → users in either role may write
  - The save is rejected (and surfaced as an error) if the check fails

`UserService.IsUserAdmin` and `IsUserAuthor` are available in components for conditional rendering
(e.g., showing/hiding edit buttons). See [Area 8](08-identity-auth.md).

---

## 12. Registering a New Content Type

1. Create the domain model extending `AdminCrudModel<TDto>` (see [Area 5](05-content-domain-models.md))
2. Register in DI — in `Program.cs` or a custom extension method:
   ```csharp
   services.AddScoped<MyThingModel>();
   services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<MyThingModel>());
   ```
3. Add routable Blazor wrapper pages for the list and create/edit routes (mirroring
   `AdminContentBlocksPage` / `AdminContentBlockUpsertPage`), pointing `AdminUpsert` at the new
   `ContentType`. No MVC controllers or per-type Razor views are needed.

*See also:* [docs/content-system.md](../content-system.md) for the full walkthrough.
