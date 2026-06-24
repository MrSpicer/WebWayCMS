# Area 3: Page Routing Subsystem

**Namespaces:**
- `WebWayCMS.Routing` — `PageRouteTransformer`
- `WebWayCMS.Pages` — `PageControllerRegistry`, `IPageControllerRegistry`, `PageControllerInfo`
- `WebWayCMS.Controllers` — `PageControllerBase<TConfig>`, `GenericPageController`, `GenericAdminPageController`
- `WebWayCMS.Attributes` — `[PageController]`

**Depends on:** Data Tier (`IPageService.GetByRouteAsync`), Form Generation Metadata (`FormPropertyBuilder`), ASP.NET Core Routing
**Consumed by:** All page controllers (Web project + CMS built-ins), content-zone rendering (`ContentZone.razor`, via the `CmsRenderContext` cascade), Admin page-edit UI (controller dropdown populated from registry)

---

## 1. System Overview

All public URL traffic is caught by a single catch-all route registered by the CMS inside
`EnsureCMS()` (its `ConfigureMiddleware` step — see [07-cms-bootstrap](07-cms-bootstrap.md)):

```csharp
app.MapDynamicControllerRoute<PageRouteTransformer>("{**slug}");
```

`PageRouteTransformer` is an ASP.NET Core `DynamicRouteValueTransformer`. On every request it:
1. Looks up the requested path in the `Pages` table
2. If found, stores the `PageDTO` and deserialized config in `HttpContext.Items`
3. Returns `{ controller = page.ControllerName, action = "Index" }` to the routing system

The controller's `Index()` action then runs and returns a **Blazor root component** via
`ICmsPageRenderer` (a `RazorComponentResult<CmsPageHost>` for public pages,
`RazorComponentResult<AdminPageHost>` for admin pages) — not an MVC view. `CmsPageHost` establishes a
per-request `CmsRenderContext` cascade (carrying the `PageDTO`/config) and renders the page body.
Content zones placed in that body (`<ContentZone ZoneName="..." />`) read the current page from the
cascaded `CmsRenderContext` rather than from `HttpContext.Items` directly.

---

## 2. Four-Step Resolution Algorithm

`PageRouteTransformer.TransformAsync` resolves a URL in this order:

1. **Normalize** — an empty/unset path is treated as root `/`; lowercase the path; strip trailing `/` (preserving root `/`)
2. **Exact match** — query `IPageService.GetByRouteAsync(path)` for a published, non-deleted, latest-version page
3. **Progressive parent match** — if no exact match and path has multiple segments, try the longest nested parent path:
   - For `/about/team/alice`, tries `/about/team`, then `/about`
   - A parent page only serves the path when some registered `ISubRouteContent` resolver can actually resolve the trailing sub-route (see below); on success, stores the remaining segments as `CMS:SubRoute` (e.g. `"alice"`)
   - The root page at `/` is **not** treated as a catch-all parent: unmatched top-level paths (e.g. `/nope`) resolve to no page rather than silently rendering the home page
4. **Registry lookup** — resolves `page.ControllerName` via `IPageControllerRegistry.GetByName`; if not found, returns `null!` (causes 404)

If no page is found at any step, `TransformAsync` returns `null!` and routing falls through to the standard MVC route table, which results in a 404.

> **Routing precedence.** Attribute-routed controllers (`app.MapControllers()`) and the routable
> Blazor components (`app.MapRazorComponents<App>()`, including the admin pages such as
> `/admin/blocks` and the Identity components at `/Account/*`) are both mapped **before** the dynamic
> page route (`MapDynamicControllerRoute<PageRouteTransformer>("{**slug}")`), so those real routes
> out-rank the catch-all. Without this, a path like `/admin/blocks` would be captured by the page
> route as a sub-route of the `/admin` page rather than handled by its component.

### Sub-route validation (404 for unresolved sub-routes)

When the progressive parent match finds a candidate parent page, that page is only the
correct response if some content on it can actually serve the trailing sub-route. The
transformer therefore asks every registered `ISubRouteContent` resolver whether it can
resolve the candidate `CMS:SubRoute` **before committing to the page**. If none can, the
transformer leaves the page unresolved (`return null!`) so the request falls through to the
controller route table and ultimately a `404`, instead of rendering the parent page with a
`200`. `ArticleSubRouteResolver` is the built-in resolver; it resolves a sub-route to an
article by slug via `IArticleModel.GetBySlugAsync`.

---

## 3. `HttpContext.Items` Contract

The transformer populates these keys for the dispatched controller:

| Key | Type | Description |
|-----|------|-------------|
| `"CMS:PageData"` | `PageDTO` | The resolved page record |
| `"CMS:PageConfig"` | `object` (typed to `TConfig`) | Deserialized `ConfigurationJson`; falls back to `Activator.CreateInstance(ConfigurationType)` on parse failure |
| `"CMS:SubRoute"` | `string` | Remaining path segments after the matched page route; only present when a parent page matched |

`CMS:PageData` is surfaced to the Blazor render tree through the `CmsRenderContext` cascade
(established by `CmsPageHost`), so `ContentZone.razor` scopes content zones to the current page from
the cascade. Any controller that needs the current page should read from `HttpContext.Items`, not the
database; Blazor components read the cascaded `CmsRenderContext`.

---

## 4. `PageControllerBase<TConfig>`

```csharp
public abstract class PageControllerBase<TConfig> : Controller where TConfig : class, new()
{
    protected PageDTO? CurrentPage => HttpContext.Items["CMS:PageData"] as PageDTO;
    protected TConfig PageConfig => HttpContext.Items["CMS:PageConfig"] as TConfig ?? new TConfig();
    public abstract Task<IActionResult> Index();
}
```

- `CurrentPage` — the resolved `PageDTO`; `null` only if the controller is reached without going through the transformer (should not happen in normal operation)
- `PageConfig` — the typed configuration; returns a default instance if not set (safe fallback)
- `Index()` — the only action the transformer dispatches to; all page rendering happens here. It
  returns a Blazor root component via `ICmsPageRenderer` (a `RazorComponentResult`), not a `ViewResult`

Do not add additional named actions to page controllers. Sub-routing via `CMS:SubRoute` is the correct mechanism for URL segments beyond the page route.

---

## 5. `[PageController]` Attribute Reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DisplayName` | `string` | Controller name (spaces inserted) | Shown in the admin page type dropdown |
| `Description` | `string` | `""` | Help text in admin UI |
| `Category` | `string` | `"General"` | Groups related page types |
| `ConfigurationType` | `Type?` | `null` | Configuration class for per-page settings |
| `IconClass` | `string` | `""` | CSS icon class |
| `Order` | `int` | `0` | Sort order within category |

---

## 6. `PageControllerRegistry`

`PageControllerRegistry` is a **singleton** registered at startup. It scans two assemblies:
- `typeof(ServiceCollectionExtensions).Assembly` — the CMS library
- `Assembly.GetEntryAssembly()` — the host Web project

Scanning finds all non-abstract classes that inherit from `Controller` and carry `[PageController]`. For each, it builds a `PageControllerInfo`:

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
    public List<FormPropertyInfo> Properties { get; set; }  // from FormPropertyBuilder
}
```

**Interface:**
```csharp
PageControllerInfo? GetByName(string controllerName)
IReadOnlyList<PageControllerInfo> GetAllControllers()
IReadOnlyList<string> GetCategories()
IReadOnlyList<PageControllerInfo> GetByCategory(string category)
object? CreateDefaultConfiguration(string controllerName)
IReadOnlyList<string> ValidateConfiguration(string controllerName, object configuration)
```

`GetByName` is used by the transformer at runtime. `GetAllControllers` is used by the admin page-edit UI to populate the controller dropdown. `ValidateConfiguration` applies `[FormProperty]` required/range/length/pattern checks to a deserialized config object.

---

## 7. Built-in Page Types

**`GenericPageController`** — `[PageController("Generic Page")]`
- Configuration class `GenericPageConfiguration` (per-page `ViewName`/`Meta`/`Style`/`Script`)
- Renders the `CmsPageHost` Blazor component via `ICmsPageRenderer.RenderPage(...)` — no host view
  needed. The body is the page's `Main` content zone, or a host `[CmsPageView]` component when the
  page selects one
- The default controller type for the seeded home page

**`GenericAdminPageController`** — `[PageController("Admin Dashboard")]`
- Used for the seeded `/admin` page
- Requires `[Authorize(Roles = "Admin")]`
- Renders the `AdminPageHost` Blazor component via `ICmsPageRenderer.RenderAdminPage(...)` from the
  CMS library

---

## 8. Sub-route Handling

`CMS:SubRoute` contains the path segments after the matched page route, joined with `/`. For example, if `/blog` is a page and the request is for `/blog/2026/my-post`, `CMS:SubRoute` is `"2026/my-post"`.

Use `CMS:SubRoute` when a single page type handles multiple sub-paths (e.g., a blog page that also serves individual post URLs). Parse it in `Index()` to guard for a `404` before rendering, then return the Blazor root via `ICmsPageRenderer`:

```csharp
public override async Task<IActionResult> Index()
{
    var subRoute = HttpContext.Items["CMS:SubRoute"] as string;
    if (!string.IsNullOrEmpty(subRoute) && await _articleService.GetBySlugAsync(subRoute) is null)
        return NotFound();

    return _renderer.RenderPage(CurrentPage, PageConfig);
}
```

The page body (its content zones, or a `[CmsPageView]` component) reads `CmsRenderContext.SubRoute`
to decide what to render (list vs. detail). When a page renders its sub-route content through a
content zone (`ContentZone.razor`) rather than branching in `Index()`, the controller cannot return
`NotFound()`
because the response has already started by the time the component runs. Register an
`ISubRouteContent` resolver instead: `PageRouteTransformer` queries every resolver
during routing and returns a 404 for any sub-route none of them can resolve.
See *Sub-route validation* under §2.

---

*See also:* [docs/page-system.md](../page-system.md) for the step-by-step guide to creating a custom page type.
