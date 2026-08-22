# Area 9: CMS View Components & Presentation

**Namespaces:**
- `WebWayCMS.ViewComponents` (excluding `ContentZoneViewComponent`, covered in [Area 4](04-content-zone-framework.md)) — in `WebWayCMS.Presentation`
- `WebWayCMS.Views.*`
- `WebWayCMS.Services` — `IViewDiscoveryService`, `ViewDiscoveryService` (in `WebWayCMS.Core`)

**Depends on:** Content Domain Models (ViewModels), Content Zone Component Framework (admin zone edit views), Identity (admin views gated by `[Authorize]`)
**Consumed by:** Web project layout files via `CompiledRazorAssemblyPart`

---

## 1. Embedded Razor Views — and Which Assembly Ships Them

Two CMS assemblies ship pre-compiled Razor views, registered in `ServiceCollectionExtensions` as
two application parts each:

```csharp
apm.ApplicationParts.Add(new AssemblyPart(asm));              // controllers, ViewComponents
apm.ApplicationParts.Add(new CompiledRazorAssemblyPart(asm)); // pre-compiled .cshtml views
```

| Assembly | Registered by | Contains |
|---|---|---|
| `WebWayCMS.Presentation` | `AddWebWayCmsRendering` | Public ViewComponents and their views, the fallback `_Layout`, the error view, and the Identity Razor Pages area |
| `WebWayCMS.Admin` | `AddWebWayCmsAdmin` | All `/wadmin` views, the admin layout and partials, and the admin CSS/JS |

A rendering-only host therefore has no admin views at all — see [Area 11](11-deployment-modes.md).

`WebWayCMS.Core` and `WebWayCMS.Forms` are added as plain `AssemblyPart`s (controllers,
ViewComponents, and the `FormFieldsTagHelper`) — they ship no views.

The Web project uses runtime Razor compilation in development (`AddRazorRuntimeCompilation()`) so changes to `.cshtml` files in the Web project are picked up without rebuild. The CMS libraries' views are pre-compiled and are not affected by runtime compilation.

**View resolution precedence:** ASP.NET Core searches the Web project's `Views/` folder before falling back to the CMS libraries' compiled views. To override any CMS view, create a file at the same relative path in the Web project.

**Static assets** are served from each library's `wwwroot` via the RCL convention:
`~/_content/WebWayCMS.Admin/css/admin.css`, `~/_content/WebWayCMS.Presentation/js/validation.js`,
and so on.

---

## 2. Admin Layout Structure

`WebWayCMS.Admin` provides the shared admin layout:

| File | Purpose |
|------|---------|
| `Views/Shared/_AdminLayout.cshtml` | Root admin layout: navbar, Bulma + FontAwesome from CDN, the CSRF and CKEditor-license meta tags, and optional CKEditor sections |
| `Views/Shared/_AdminNavbar.cshtml` | Top navigation bar (partial, included by `_AdminLayout`). The hand-written links (Pages, Content Blocks, Articles, the Advanced dropdown) point at admin controller actions; ahead of them it renders `RouteNavigation` with `AdminRoutes = true`, so any published `/wadmin`-prefixed route with a `NavigationName` appears automatically |
| `Views/Shared/_DeleteConfirmModal.cshtml` | Reusable delete confirmation modal, rendered once by the layout |
| `Views/AdminShared/VersionHistory.cshtml` | Shared version history list view used by all content types |

Admin assets referenced by the layout: `~/_content/WebWayCMS.Admin/css/admin.css` and
`~/_content/WebWayCMS.Admin/js/admin.js`. The project's `.csproj` compiles
`Views/Shared/Components/ContentZone/edit.scss` into `wwwroot/css/content-zone-edit.css` and copies
the view-adjacent `edit.js` / `PageUpsert.js` into `wwwroot/js/` as part of the build.

Each admin view folder carries its own `_ViewStart.cshtml` pointing at `_AdminLayout`, so individual
views do not set `Layout` themselves.

---

## 3. Built-in View Components

### `PageViewComponent`

**Invocation:**
```razor
@await Component.InvokeAsync("Page", new { config = new PageContentZoneConfiguration() })
```

**Purpose:** Renders a **navigation tree** of CMS pages. Reads `IPageModel.GetPageIndexAsync()` and maps
the page tree to `PageNavigationViewModel` / `PageNavigationItem`. Tree nodes without a `PageNodeId`
(intermediate path segments that are not themselves pages) are dropped, so their children rise a level.
The view emits bare Bulma `<a class="navbar-item">` links with a nested `<ul>` for children, and no
wrapping element — it is meant to drop straight into a navbar.

**Parameter:** `PageContentZoneConfiguration? config` — `ShowDraftPages`, `ShowHiddenPages`,
`AdminPages` (partitions on the `/wadmin` prefix: admin-only when true, admin-excluded when false)
and `ViewName` (a ViewPicker over `Views/Shared/Components/Page/`).

---

### `RouteNavigationViewComponent`

**Invocation:**
```razor
@await Component.InvokeAsync("RouteNavigation", new { config = new RouteNavigationConfiguration() })
```

**Purpose:** Renders a navigation tree of links for every active CMS route. Unlike `PageViewComponent`,
it reads `ICMSRouteRegistry.GetActiveRoutes()` (the 60s-cached route registry) rather than `IPageModel`,
so it surfaces routes owned by non-Page content (routable widgets, `[CmsRoute]` controllers) as well as
pages. It renders **the same markup as `PageViewComponent`** — bare `<a class="navbar-item">` links with
a nested `<ul>` for children — so the two widgets are interchangeable inside a navbar zone.

**Link text is the route's `NavigationName`**, and a route without one is **not rendered at all** — the
widget is an opt-in menu, not a dump of every route. Page routes get theirs from the page title on their
*first* publish (see `RegisterContentRoutesAsync` in [03-page-routing.md](03-page-routing.md)); code-based
routes declare theirs on `[CmsRoute(..., NavigationName = "…")]`; anything else is named by an admin at
`/wadmin/cmsroutes`. Clearing the name there is the supported way to pull a page out of the menu — a
republish carries the blank forward rather than re-seeding it from the title.

**Filters**, applied in order: patterns containing a `{` placeholder are always excluded (they cannot be
linked without values); routes with no `NavigationName` are excluded; reserved routes are excluded unless
`IncludeReserved` (a reserved row never dispatches — `CMSRouteService.MatchRouteAsync` skips it — so it is
an unroutable placeholder); then the `/wadmin` prefix partition. That last one matches on a **segment
boundary** (`AdminPathPrefix`, shared with `PageViewComponent`), so a public route at `/wadmin-guide`
stays in the public nav instead of being swept into the admin navbar.

**Nesting** happens *after* filtering: each surviving pattern is attached to its nearest surviving
ancestor pattern (`/blog/news` under `/blog`), or stays at the root when it has none. A filtered-out
parent therefore lets its children rise a level — so a named child of an **unnamed** parent appears at the
top level — mirroring how `PageViewComponent` drops nodes without a `PageNodeId`. The site root `/` is
never treated as a parent, so it does not swallow every other link.

**Parameter:** `RouteNavigationConfiguration? config` — `AdminRoutes`, `IncludeReserved`, and `ViewName`
(a ViewPicker over `Views/Shared/Components/RouteNavigation/`).

**In-CMS consumer:** `WebWayCMS.Admin/Views/Shared/_AdminNavbar.cshtml` invokes it with
`AdminRoutes = true` and `ViewName = "AdminNavbar"` (it replaced an equivalent `PageViewComponent` call
— `PageViewComponent` itself remains a placeable zone widget). It asks for the named view rather than
`Default` so a host re-skinning `Default.cshtml` cannot break the admin navbar; see the warning in §6. The integration host drives its whole public navbar from it; see §6
for how that host re-skins the view for Bootstrap.

> The widget seeder (`CmsWidgetRegistrationSeeder`) is insert-only. A database seeded before this widget
> gained a `ConfigurationType` keeps `PropertyDefinitionsJson = "[]"` and shows no config form; delete
> and re-seed the row, or set its **Configuration Type Name** to
> `WebWayCMS.Models.CMSRoute.RouteNavigationConfiguration` at `/wadmin/widgetregistration` and re-save.

---

### `ContentBlockViewComponent`

**Invocation:**
```razor
@await Component.InvokeAsync("ContentBlock", new { config = myConfig })
```

**Purpose:** Renders a reusable content block by ID. Fetches the latest published version from `IContentBlockModel` and renders it. Used as a widget within content zones.

**Parameter:** `ContentBlockContentZoneConfiguration config` — contains the content block identifier and rendering options.

---

### `ArticleViewComponent`

**Invocation:**
```razor
@await Component.InvokeAsync("Article", new { config = myConfig })
```

**Purpose:** Renders an article list or a specific article. Fetches from `IArticleListModel` and `IArticleModel`.

**Parameter:** `ArticleContentZoneConfiguration config` — contains the article list reference and display options (list view vs detail view).

---

### `LayoutViewComponent`

**Invocation:**
```razor
@await Component.InvokeAsync("Layout", new { config = new LayoutContentZoneConfiguration() })
```

**Purpose:** Renders a multi-column layout by composing multiple `ContentZone` components. The layout variant determines how columns are arranged.

**Parameter:** `LayoutContentZoneConfiguration config` — specifies the layout variant.

**Available layout variants:**

| Variant | Description |
|---------|-------------|
| `Default` | Single column (full width) |
| `SingleColumn` | Explicit single column |
| `TwoColumnEqual` | Two equal 50/50 columns |
| `TwoColumnSidebar` | Main content + narrow sidebar |
| `ThreeColumn` | Three equal columns |
| `FourColumn` | Four equal columns |
| `OneThirdTwoThird` | 1/3 + 2/3 split |
| `AsymmetricRightHeavy` | Narrow left + wide right |
| `CenteredNarrow` | Centered, constrained-width single column |
| `HeaderContentFooter` | Three stacked rows (header/body/footer) |
| `HeroWithColumns` | Full-width hero row + columned body |

Each variant renders named zone slots (`Column1`, `Column2`, `Header`, `Footer`, etc.) that editors populate with widgets.

---

## 4. Shared Admin Partials

Located in `WebWayCMS.Admin`:

| File | Description |
|---------|-------------|
| `Views/Shared/_DeleteConfirmModal.cshtml` | Bulma modal for delete confirmation; renders a form POST to the delete route |
| `Views/AdminShared/VersionHistory.cshtml` | Full version list view with restore and delete-version actions |
| `Views/Shared/Components/ContentZone/edit.cshtml` | The inline zone editor, with its own `edit.scss` and `edit.js` |

The public components directory (`WebWayCMS.Presentation/Views/Shared/Components/`) contains the default views for each built-in ViewComponent (e.g. `ContentZone/Default.cshtml`).

---

## 5. `IViewDiscoveryService`

`ViewDiscoveryService` discovers available view names (excluding partials prefixed with `_`) from **two combined sources**, so the result is correct in both debug and Release/Docker builds. It is a **scoped** service.

1. **Compiled views from application parts** — enumerates `ApplicationPartManager` → `ViewsFeature` and inspects each `CompiledViewDescriptor.RelativePath`. This is the only source available in Release/Docker, where views are compiled into assemblies (e.g. `CompiledRazorAssemblyPart` for `WebWayCMS.Presentation`) and no `.cshtml` files exist on disk.
2. **Filesystem scan** — scans standard ASP.NET view locations on disk, so freshly-added `.cshtml` files appear in development without a rebuild.

The two result sets are unioned (case-insensitive).

```csharp
public interface IViewDiscoveryService
{
    IReadOnlyList<string> GetAvailableViews(string componentName);
    IReadOnlyList<string> GetControllerViews(string controllerName);
}
```

**`GetAvailableViews(componentName)`** — returns views whose path tail is `Views/Shared/Components/{componentName}/{view}.cshtml` (optionally under an `Areas/{area}/` prefix). Sources:
- Compiled descriptors matching that tail (e.g. `/Views/Shared/Components/{componentName}/Default.cshtml`)
- `{contentRoot}/Views/Shared/Components/{componentName}/`
- `{contentRoot}/Areas/*/Views/Shared/Components/{componentName}/`
- Sibling directories (to find views in `WebWayCMS/Views/`)

Used by the `ViewPicker` `EditorType` — `FormViewPicker` calls this server-side to populate the
dropdown for view-component views (a stored value not among the discovered views is retained as an
option so an edit load keeps the selection).

**`GetControllerViews(controllerName)`** — returns views whose path tail is `Views/{controllerName}/{view}.cshtml` (optionally under an `Areas/{area}/` prefix). Sources:
- Compiled descriptors matching that tail (e.g. `/Views/{controllerName}/Index.cshtml`)
- `{contentRoot}/Views/{controllerName}/`
- Sibling directories

Used by `PageRegistryHandler.GetProperties` to return the list of available views for a page controller
type. The page form's **View Name** dropdown is populated client-side from this: `page-upsert.js`
calls `GET /wadmin/pages/registry/{name}/properties` and rebuilds the `<select>` from `availableViews`
(the selected page type's `Views/{ControllerName}/*.cshtml`), preserving the current selection when it
is still offered.

---

## 6. Overriding CMS Views in the Web Project

To replace any CMS view with a custom version:

1. Create a file at the same relative path in `MySite/Views/`
2. ASP.NET Core's view resolution searches the Web project first

For example, to replace the admin navbar (from `WebWayCMS.Admin`):
- Create `MySite/Views/Shared/_AdminNavbar.cshtml`

For ViewComponent default views:
- Create `MySite/Views/Shared/Components/ContentBlock/Default.cshtml`

No configuration changes are needed; view resolution precedence handles the override automatically.

**Worked example — re-skinning a component for a different CSS framework.** The CMS ships Bulma markup,
but the integration host is a Bootstrap site. It overrides
`Views/Shared/Components/RouteNavigation/Default.cshtml` to emit a self-contained
`<ul class="navbar-nav flex-grow-1">` of `<li class="nav-item"><a class="nav-link">` instead of the
shipped bare `<a class="navbar-item">`, and turns any item with children into a Bootstrap
`dropdown-toggle` + `.dropdown-menu`. Its `_Layout.cshtml` then just calls
`@await Component.InvokeAsync("RouteNavigation")`. The same host uses this technique for all eleven
`Components/Layout/*.cshtml` views.

Things worth copying from that view:

- **Emit your own wrapper element**, so the component stays valid whether it lands in a navbar or a
  content zone.
- **Use the target framework's real submenu idiom — a nested list is not one.** The obvious translation
  of the Bulma view is a nested `<ul class="navbar-nav">`, and it does not work:
  `.navbar-expand-* .navbar-nav` is a **descendant** selector, so the inner list also inherits
  `flex-direction:row` and renders as a second tier *inside* its parent `<li>`, leaving a ragged
  two-tier block instead of a bar. Bootstrap supports exactly one navbar submenu — the dropdown.
- **When the toggle stops being a link, repeat the item's own link inside the menu.** A
  `dropdown-toggle` is `href="#"`, so without a first `.dropdown-item` pointing at the parent's own
  path, that page becomes unreachable from the nav.
- **Match the framework's depth limit.** Bootstrap has no true multi-level menu, so the host flattens
  grandchildren into the same `.dropdown-menu` with a `ps-*` indent rather than dropping them. Where the
  framework *does* nest (the shipped Bulma view), recurse rather than looping one level deep, so deep
  trees are not silently truncated.

> ⚠️ **An override is app-wide, not host-page-only.** View resolution is per *application*, and the
> admin UI renders in the same application — so shadowing `Components/{X}/Default.cshtml` also changes
> what the **admin** sees. That is invisible for `Components/Layout/*`, which nothing on the admin side
> invokes, but it bites any component the admin UI also renders. `RouteNavigation` is the first such
> component: the admin navbar loads only Bulma, so a Bootstrap `Default.cshtml` in the host would render
> it unstyled. The CMS therefore ships a **named** Bulma view,
> `WebWayCMS.Admin/Views/Shared/Components/RouteNavigation/AdminNavbar.cshtml`, and `_AdminNavbar.cshtml`
> asks for it by `ViewName` — so a host is free to shadow `Default.cshtml` without touching the admin UI.
> Apply the same pattern to any future CMS view rendered on both sides.
