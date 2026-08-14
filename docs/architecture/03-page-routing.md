# Area 3: CMS Routing Subsystem

**Namespaces:**
- `WebWayCMS.Routing` — `CMSRouteTransformer`, `NotReservedConstraint`
- `WebWayCMS.Pages` — `PageControllerRegistry`, `IPageControllerRegistry`, `PageControllerInfo`
- `WebWayCMS.Data.Services` — `ICMSRouteService`, `CMSRouteService`, `CMSRouteMatchResult`
- `WebWayCMS.Interfaces` — `IRouteRegistrationService`, `IRoutableContent`, `IRoutableViewComponent`
- `WebWayCMS.Controllers` — `PageControllerBase<TConfig>`, `GenericPageController`, `GenericAdminPageController`
- `WebWayCMS.Attributes` — `[PageController]`, `[CmsRoute]`

**Depends on:** Data Tier (`ICMSRouteService`, `IContentStore<PageDTO>`), Form Generation Metadata (`FormPropertyBuilder`), ASP.NET Core Routing
**Consumed by:** All page controllers (Web project + CMS built-ins), Content Zone `ViewComponent` (`CMS:PageData` from `HttpContext`), Admin page-edit UI (page-type dropdown populated from the registry)

> **Versioning note (Node/Version model):** routes are **plain, unversioned** rows written by
> **Publish, never Save**. A draft that changes a slug does not touch the route table until published.
> The route transformer resolves pages through `IContentStore<PageDTO>.GetAsync(nodeId)` (read-context
> aware — a rendering-only host serves published rows only). See
> [01-data-tier](01-data-tier.md) for the model.

---

## 1. System Overview

Routing is **database-backed**. Every CMS URL is a row in the `CMSRoutes` table holding a route
pattern plus JSON blobs for defaults, constraints, and data tokens. A single
`DynamicRouteValueTransformer` matches the request path against those rows.

All public URL traffic is caught by a catch-all route registered by the CMS inside
`UseWebWayCmsAdmin()` / `UseWebWayCmsRendering()` (their `MapCmsEndpoints` step — see
[07-cms-bootstrap](07-cms-bootstrap.md)):

```csharp
app.MapDynamicControllerRoute<CMSRouteTransformer>("{**slug}");
```

There are three ways a row gets into `CMSRoutes`:

| Source | `OwningContentType` | Written by |
|---|---|---|
| A page, from its Slug | `"Page"` | `PageModel` → `IRouteRegistrationService.RegisterContentRoutesAsync` on publish |
| A routable widget placed in a zone | e.g. `"ArticleWidget"` | `IRouteRegistrationService.TryRegisterWidgetRoutesAsync` when the zone item is created |
| A `[CmsRoute]`-decorated controller | `"CodeBased"` | `CmsRouteSeeder.EnsureCodeBasedRoutesSeeded` at startup |

Rows are also editable directly through the admin UI at `/wadmin/cmsroutes` (the `cmsroutes`
content type).

---

## 2. `CMSRouteTransformer.TransformAsync` — Resolution

`TransformAsync` first checks for the **preview path**, then falls back to route-table matching:

1. **Normalize** — an empty/unset path is treated as root `/`; lowercase the path; strip a
   trailing `/` (preserving root `/`)
2. **Preview** — if the path is `/_preview/{nodeId:guid}`, resolve the draft directly (see below);
   a malformed node id falls through to normal matching
3. **Match** — `ICMSRouteService.MatchRouteAsync(path)` walks the active routes and returns the
   first pattern that matches, along with the route values it extracted. No match ⇒ `return null!`
4. **Read `DefaultsJson`** — a `Dictionary<string, string>`; a `"controller"` key is required, and
   `"action"` defaults to `"Index"`. Missing `"controller"` ⇒ `return null!`
5. **Code-based short-circuit** — if `OwningContentType == "CodeBased"` (case-insensitive), skip
   straight to step 8. Code-based routes are not validated against the page-type registry and get
   no page data or page config
6. **Registry validation** — otherwise resolve `IPageControllerRegistry.GetByName(controllerName)`;
   if the controller is not a registered page type, `return null!`
7. **Load page data and config** —
   - if `OwningContentType == "Page"` and `OwningContentNodeId` is set, load that page's current
     (read-context aware) version into `HttpContext.Items["CMS:PageData"]`
   - otherwise, if `DataTokensJson` carries a `ParentPageNodeId`, load *that* page instead (this
     is how widget-owned routes still render inside their host page)
   - if the controller declares a `ConfigurationType`, deserialize the loaded page's
     `ConfigurationJson` column into it and store it as `"CMS:PageConfig"`, falling back to
     `Activator.CreateInstance(ConfigurationType)` on a parse failure or when no page data is loaded
8. **Dispatch** — store the matched route as `"CMS:RouteData"` and return
   `{ controller, action }` merged with every extracted route value

When `TransformAsync` returns `null!`, routing falls through to the conventional
`{controller}/{action}/{id?}` route, which normally results in a 404.

### Preview path (`/_preview/{nodeId}`)

Preview never touches `CMSRoutes` — it exists precisely so a **never-published** page can render
its draft (a route row only exists after Publish). When the path matches `/_preview/{nodeId:guid}`:

1. **Authorize** — `httpContext.User` must be authenticated and in `Admin` or `Editor`, otherwise
   `return null!` (404, no information leak)
2. **Load the draft** — `IContentStore<PageDTO>.GetCurrentDraftAsync(nodeId)` (draft, not
   read-context aware)
3. **Resolve the controller** — `IPageControllerRegistry.GetByName(page.ControllerName)`, then
   populate `CMS:PageData` / `CMS:PageConfig` exactly as the normal path does, plus a synthetic
   `CMS:RouteData` (`Pattern = "/_preview/{nodeId}"`)
4. **Dispatch** — return `{ controller = page.ControllerName, action = "Index" }`

`AdminContentController.Preview` sets the `wwcms_preview` cookie and redirects to
`/_preview/{nodeId}`; the cookie is what makes `PreviewAwareReadContext` serve drafts for the content
zones and widgets rendered *inside* the previewed page.

> **Routing precedence.** Attribute-routed controllers are mapped via `app.MapControllers()`
> **before** the dynamic route, so real controller routes such as `AdminContentController`'s
> `wadmin/{contentType}` out-rank the catch-all. Without this, `/wadmin/pages` would be captured by
> the CMS route table rather than handled by its controller.

---

## 3. `ICMSRouteService` — Matching Semantics

Routes are **plain, unversioned rows** — there is no "latest version" selection. `GetActiveRoutesAsync`
returns every row ordered by `Order` ascending then `Pattern.Length` ascending; `MatchRouteAsync`
then iterates that list ordered by `Order` and returns the **first** pattern that matches — so
`Order` is the primary tie-breaker between competing patterns.

`MatchRouteAsync` delegates loading the active route list to `ICMSRouteRegistry`, a **singleton**
that caches the result for **60 seconds**. `CMSRouteService` no longer queries the database
directly on every match request. Every mutation (`UpsertAsync`, `DeleteAsync`,
`DeleteByOwningContentAsync`) calls `ICMSRouteRegistry.Invalidate()` to drop the cache, so
admin edits take effect within at most one minute. **Invalidation is per-process** — in a
multi-instance deployment, another instance will not see a route change until its TTL expires.

**Reserved routes.** A row with `IsReserved = true` is skipped during matching and can therefore
never resolve. It still occupies its pattern for the uniqueness check in
`IsPatternAvailableAsync`, so reserving a pattern is how you stop an editor from creating a page
that would shadow a real controller route. Requests to a reserved pattern fall through to standard
ASP.NET routing.

**Pattern syntax.** Matching is hand-rolled in `CMSRouteService.TryMatchPattern` — it is *not*
ASP.NET Core's route parser, and it supports a deliberately small subset:

| Form | Notes |
|---|---|
| `/literal/path` | Exact match, case-insensitive per segment |
| `{param}` | Captures one segment |
| `{param?}` | Optional trailing segment |
| `{param:int}` / `{param:guid}` / `{param:bool}` | Type constraints |
| `{param:regex(^[a-z]+$)}` | Regex constraint; an invalid regex fails closed |
| `{**catchall}` | Greedy — captures all remaining segments and returns immediately |
| `page-{slug}` / `{slug}-page` | A literal and **one** parameter in a single segment |

Any constraint name the matcher does not recognise is treated as satisfied. A segment mixing a
literal with more than one parameter is not supported and degrades to a plain string comparison.

**Other members:** `GetActiveRoutesAsync`, `GetByOwningContentAsync`, `GetByIdAsync`,
`IsPatternAvailableAsync(pattern, excludeNodeId, excludeRouteId)`, `UpsertAsync`, `DeleteAsync`,
`DeleteByOwningContentAsync`.

**`UpsertAsync` keys the replace on `(OwningContentNodeId, Pattern)`, never "owner OR pattern":**

```csharp
Task<(bool Success, string? ErrorMessage, CMSRouteDTO? Route)> UpsertAsync(CMSRouteDTO route, CancellationToken ct = default);
```

1. **Collision check** — if another row already holds this pattern under a *different* owner, it
   returns `(false, "This route pattern is already in use by another content item.", null)` rather
   than stealing it. This is what makes two pages publishing the same slug fail loudly instead of
   the second publish silently overwriting the first's URL.
2. **Replace in place** — it deletes the row with `r.Id == route.Id` (when `route.Id` is set, which
   covers a pattern *rename*) plus any row with the same `(OwningContentNodeId, Pattern)`, then
   inserts the new row. A multi-route widget therefore replaces only its own `(owner, pattern)`
   pairs, so sibling routes survive; and an admin edit that renames a pattern removes its own old
   row instead of colliding on the live primary key.
3. **Atomic backstop** — `Pattern` is the only unique index on `CMSRoutes`, so a concurrent insert
   that loses the race to the same pattern surfaces as a `DbUpdateException` on the final
   `SaveChangesAsync`; it is caught and mapped to the same collision result as the pre-check.
4. **Owner-less rows** are treated as their own owner (`OwningContentNodeId == null`), used by
   code-based and reserved routes.

`IsPatternAvailableAsync` excludes rows owned by `excludeNodeId` — using explicit null handling
(`OwningContentNodeId == null || OwningContentNodeId != excludeNodeId`), so a NULL-owner row is
still treated as a conflict rather than silently dropped by SQL three-valued logic.

`NormalizePattern` (applied to both stored patterns and incoming paths): blank ⇒ `/`; trim and
lowercase; ensure a leading `/`; strip a trailing `/` unless the pattern is exactly `/`.

---

## 4. `HttpContext.Items` Contract

The transformer populates these keys for the dispatched controller:

| Key | Type | Description |
|-----|------|-------------|
| `"CMS:PageData"` | `PageDTO` | The owning page record. Absent for code-based routes |
| `"CMS:PageConfig"` | `object` (typed to `TConfig`) | Deserialized from the page record's `ConfigurationJson` column; falls back to `Activator.CreateInstance(ConfigurationType)` on parse failure or when no page data is loaded |
| `"CMS:RouteData"` | `CMSRouteDTO` | The matched route row |

`CMS:PageData` is also read by `ContentZoneViewComponent` to scope content zones to the current
page. Any controller or view component that needs the current page should read from
`HttpContext.Items`, not the database.

---

## 5. `PageControllerBase<TConfig>`

```csharp
public abstract class PageControllerBase<TConfig> : Controller where TConfig : class, new()
{
    protected PageDTO? CurrentPage => HttpContext.Items["CMS:PageData"] as PageDTO;
    protected TConfig PageConfig => HttpContext.Items["CMS:PageConfig"] as TConfig ?? new TConfig();
    public abstract Task<IActionResult> Index();
}
```

- `CurrentPage` — the resolved `PageDTO`; `null` for code-based routes, or if the controller is
  reached without going through the transformer
- `PageConfig` — the typed configuration; returns a default instance if not set (safe fallback)
- `Index()` — the action the transformer dispatches to unless the route's `DefaultsJson` names a
  different one

`CurrentPage.ViewName`, when set, is the page's view override — `GenericAdminPageController` uses
it to render `Dashboard.cshtml` for the seeded `/wadmin` page.

---

## 6. `[PageController]` Attribute Reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DisplayName` | `string` | Controller name (spaces inserted) | Shown in the admin page type dropdown |
| `Description` | `string` | `""` | Help text in admin UI |
| `Category` | `string` | `"General"` | Groups related page types |
| `ConfigurationType` | `Type?` | `null` | Configuration class for per-page settings |
| `IconClass` | `string` | `""` | CSS icon class |
| `Order` | `int` | `0` | Sort order within category |

The attribute is a **seeding** input, not a runtime lookup. At startup,
`CmsPageControllerSeeder.EnsurePageControllerRegistrationsSeeded` scans the Core, Admin, and entry assemblies
for `Controller` subclasses carrying `[PageController]` and inserts a
`PageControllerRegistrationDTO` row for each `ControllerName` not already present. For existing rows
the seeder re-syncs `ConfigurationTypeName` and `PropertyDefinitionsJson` (the only two fields
derived from code analysis); display metadata (DisplayName, Description, Category, Icon, Order) is
never overwritten after the first run. The database is the source of truth and the admin UI
at `/wadmin/pagetypes` is how you change page-type metadata.

---

## 7. `PageControllerRegistry`

`PageControllerRegistry` is a **singleton** that reads from the database, not from reflection. It
resolves `IPageControllerRegistrationService` through an `IServiceScopeFactory`, calls
`GetActiveAsync()`, and caches the result for **5 minutes**; `Invalidate()` drops the cache
immediately (every mutation in `PageControllerRegistrationModel` calls it). Load failures are
swallowed so a transient database problem cannot take down routing at startup.

Each row is projected into a `PageControllerInfo`:

```csharp
public class PageControllerInfo
{
    public string Name { get; set; }              // e.g. "GenericPage"
    public string DisplayName { get; set; }
    public string Description { get; set; }
    public string Category { get; set; }
    public string IconClass { get; set; }
    public int Order { get; set; }
    public Type ControllerType { get; set; }
    public Type? ConfigurationType { get; set; }
    public List<FormPropertyInfo> Properties { get; set; }
}
```

`ControllerType` and `ConfigurationType` are resolved from the stored type names at load time
(`Type.GetType`, then a sweep of loaded assemblies). `Properties` comes from the row's
`PropertyDefinitionsJson`, which was produced by `FormPropertyBuilder.BuildPropertyInfos`.

**Interface:**
```csharp
PageControllerInfo? GetByName(string controllerName)
IReadOnlyList<PageControllerInfo> GetAllControllers()
IReadOnlyList<string> GetCategories()
IReadOnlyList<PageControllerInfo> GetByCategory(string category)
object? CreateDefaultConfiguration(string controllerName)
IReadOnlyList<string> ValidateConfiguration(string controllerName, object configuration)
void Invalidate()
```

`GetByName` is used by the transformer at runtime. `GetAllControllers` feeds the admin page-edit
UI's page-type dropdown (`EditorType.PageControllerPicker`). `ValidateConfiguration` applies
`[FormProperty]` required/range/length/pattern checks to a deserialized config object.

---

## 8. `[CmsRoute]` — Code-Based Routes

`[CmsRoute]` declares a route directly on a controller class, for URLs that are part of the
application rather than editor-managed content.

```csharp
[CmsRoute("/code-test/product/{id:int}", Order = 30, Action = "ProductId")]
[CmsRoute("/code-test/docs/{**path}", Order = 70, Action = "DocsPath")]
public class CodeTestController : Controller { /* ... */ }
```

| Member | Type | Default | Description |
|---|---|---|---|
| `Pattern` | `string` | *(ctor, required)* | The route pattern |
| `Order` | `int` | `0` | Match precedence — lower wins |
| `Action` | `string` | `"Index"` | Action to dispatch to |
| `Defaults` | `string?` | `null` | Extra route defaults, as a raw JSON object |
| `Constraints` | `string?` | `null` | Stored verbatim into `ConstraintsJson` |
| `DataTokens` | `string?` | `null` | Extra data tokens, as a raw JSON object |

`AllowMultiple = true`, so one controller can declare many routes.

**Registration.** `CmsRouteSeeder.EnsureCodeBasedRoutesSeeded` runs at startup in *both* bootstrap
modes (skip with `WEBWAYCMS_SKIP_CODEBASEDROUTES=true`). It scans the Core, Admin, Presentation,
and entry assemblies for non-abstract `Controller` types carrying the attribute, normalizes each
pattern, and inserts a row with `OwningContentType = "CodeBased"` and
`DataTokens["RouteSource"] = "CodeBased"`. **Seeding is idempotent by pattern**: a pattern that
already exists is skipped, and the existing row is never updated. Changing a `[CmsRoute]` pattern
in code therefore adds a route rather than editing one — delete the stale row in
`/wadmin/cmsroutes` (or rebuild the database) to retire it.

Once seeded, code-based routes are ordinary rows: they compete with page routes purely on `Order`
then pattern length, and an admin can edit or delete them.

A worked example of every supported pattern form lives in the
[WebWayCMS.TestHost](https://github.com/MrSpicer/WebWayCMS.TestHost)
repo (`Controllers/CodeTestController.cs`).

---

## 9. How a Page Gets Its URL

Pages no longer store a route. `PageDTO` has no `Route` column — the URL is derived from the page's
**Slug** plus its **parent** (persisted as `ContentNode.ParentNodeId`), and written into `CMSRoutes`
**on publish, never on save**.

`PageModel.DeriveRoutePatternFromSlug(slug, parentRoutePrefix)`:

- with a parent prefix ⇒ `parentPrefix.TrimEnd('/') + "/" + slug`
- no prefix and slug is `"home"` (case-insensitive) ⇒ `"/"`
- otherwise ⇒ `"/" + slug`

**Parentage is carried on the node, not the route.** `PageUpsertViewModel` has a hidden
`ParentNodeId` (resolved from `?parentRoute=` by route→owner lookup, or from the node's
`ParentNodeId` on edit); `MappingProfile` copies it onto `ContentVersion.Node.ParentNodeId`, and
`ContentStore` persists it on create (`CreateDraftAsync`) and edit (`ApplyNodeFields`).

**On publish**, `DeriveRoutePatternForPublishAsync` derives the prefix from the **parent node's**
route (`GetByOwningContentAsync(parentNodeId)`), falling back to the page's own existing route, then
to no prefix — so a first publish of a child under `/docs` lands at `/docs/child`, not `/child`.

**Slug-collision check.** `PageModel.IsSlugAvailableAsync(slug, parentNodeId, excludeNodeId)` runs on
*both* save and publish. It checks (a) that no sibling *current draft* under the same parent already
claims the slug — via the sibling-scoped `GetCurrentDraftChildrenAsync(parentNodeId)` query rather
than scanning every draft — and (b) `IsPatternAvailableAsync` — catching reserved and code-based
patterns. A collision fails the save (or publish) with
`"A page with this slug already exists at this location."` on the `Slug` field rather than silently
stealing a URL.

`PublishPageAsync` validates the slug (via `IsSlugAvailableAsync`) and derives the route pattern
**before** calling `PublishAsync`, then replaces the page's existing route (`UnregisterContentRoutesAsync`
followed by `RegisterContentRoutesAsync`, so a slug rename drops the old pattern rather than
accumulating a second one). If route registration fails after the state flip, `PublishPageAsync`
rolls the publish back (`UnpublishAsync`) and re-upserts the snapshotted previous route(s) so a
republish whose new pattern collides still resolves at its old URL — a page is never left Published
with no route. `UnpublishPageAsync` calls `UnregisterContentRoutesAsync` (which hard-deletes the
page's routes). Unpublishing a page therefore removes its URL without deleting the page. A draft slug
change does not touch the route table until published.

When the admin "Add child" link supplies a `parentRoute`, `ResolveParentNodeIdAsync` first matches a
`CMSRoutes` row by pattern; if none exists (an unpublished parent) it falls back to walking the
current drafts through the same path-derivation the admin tree uses, so a child can be created
directly beneath an unpublished parent.

The admin page tree is built by joining pages to their `CMSRoutes` patterns and splitting those
patterns into segments. A page with **no active route** (unpublished) derives its path by walking
`ParentNodeId`, so unpublished children nest under their parent instead of surfacing at root.
`PageTreeNode` exposes this as `Path` (formerly `Route`).

---

## 10. `IRouteRegistrationService` and Routable Widgets

`RouteRegistrationService` is the single place that turns domain content into route rows. The
registration methods return `(bool Success, string? ErrorMessage)` so a collision surfaced by
`UpsertAsync` propagates up to the caller (`PageModel.PublishPageAsync` returns the error instead of a
bare success).

| Method | Purpose |
|---|---|
| `RegisterContentRoutesAsync(content, routePattern, controllerName, contentNodeId, ct)` | Writes the owning content's route with `Defaults = {controller, action}` and `DataTokens = {RouteContentType}` |
| `UnregisterContentRoutesAsync(contentNodeId, ct)` | Hard-deletes all routes owned by that content |
| `RegisterWidgetRoutesAsync(...)` | First sweeps the owner's existing routes (a widget's route set is fully recomputed, so a rename/reparent drops stale patterns), then prefixes a widget's pattern with its parent page route, merges the parent's defaults, and injects `DataTokens["ParentPageNodeId"]`; each generated route is upserted by its own `(owner, pattern)`, so a multi-route widget keeps every route |
| `TryRegisterWidgetRoutesAsync(componentName, contentZoneItemNodeId, parentPageNodeId, isActive, ct)` | Looks the component up among the registered `IRoutableViewComponent`s and registers its routes if it is one; returns `(bool Success, string? ErrorMessage)` — the inactive/no-parent/no-widget/no-page-route early-outs are benign no-ops reported as success, and a real `UpsertAsync` collision propagates as failure |

Two interfaces opt content into this:

```csharp
public interface IRoutableContent
{
    string RouteContentType { get; }
    Task<IReadOnlyList<CMSRouteDTO>> GetRoutesAsync(Guid contentNodeId, CancellationToken ct);
}

public interface IRoutableViewComponent
{
    string ComponentName { get; }
    Task<IReadOnlyList<CMSRouteDTO>> GenerateRoutesAsync(
        string parentRoute, Guid contentZoneItemNodeId, CancellationToken ct);
}
```

`PageModel` implements `IRoutableContent` (`RouteContentType => "Page"`). `ArticleViewComponent`
implements `IRoutableViewComponent` — dropping an Article widget onto a page registers a
`{slug}` sub-route beneath that page's URL, which is how article detail URLs work. That replaces
the old `ISubRouteContent` resolver mechanism, which no longer exists.

---

## 11. `NotReservedConstraint`

Applied to the `{parentKey}` route segment in admin child resource routes to prevent conflicts with
literal action segments. The reserved words are:

```
edit, delete, create, registry, api, reorder, versions
```

Registered in the route constraint map by `MapAdminTypes` (admin mode only) via:
```csharp
services.Configure<RouteOptions>(o => o.ConstraintMap["notreserved"] = typeof(NotReservedConstraint));
```

Used in admin routes like `{contentType}/{parentKey:notreserved}/{childType}`. This is unrelated to
`CMSRouteDTO.IsReserved`, which concerns public URL matching.

---

## 12. Built-in Page Types

**`GenericPageController`** — `[PageController(DisplayName = "Generic Page", Order = 0)]`
- `PageControllerBase<GenericPageConfiguration>`
- Renders `Views/GenericPage/Index.cshtml` (shipped in `WebWayCMS.Presentation`)
- The controller of the seeded home page at `/`

**`GenericAdminPageController`** — `[PageController(DisplayName = "Generic Admin Page", Order = 1)]`
- Lives in `WebWayCMS.Admin`; carries `[Authorize(Roles = "Admin")]`
- The controller of the seeded `/wadmin` page, whose `ViewName` is `"Dashboard"`

Both are seeded into `PageControllerRegistrations` on first startup.

---

*See also:* [docs/page-system.md](../page-system.md) for the step-by-step guide to creating a custom page type.
