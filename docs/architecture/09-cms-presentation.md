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
| `Views/Shared/_AdminNavbar.cshtml` | Top navigation bar (partial, included by `_AdminLayout`) |
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

**Purpose:** Renders a page reference by fetching page data from `IPageModel`. Used to embed a page's content block zones as a widget within another zone.

**Parameter:** `PageContentZoneConfiguration? config` — contains the target page configuration (zone slot names).

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
