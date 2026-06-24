# Area 9: Blazor Presentation Layer

**Namespaces:**
- `WebWayCMS.Presentation.Components` — `CmsLayout`, `CmsPageHost`, `AdminPageHost`, `AdminLayout`, `ContentZone`, `App`
- `WebWayCMS.Presentation.Components.Widgets` — built-in widgets (`ContentBlockWidget`, `ArticleWidget`, `LayoutWidget`, `PageNavigationWidget`)
- `WebWayCMS.Presentation.Components.Admin` — admin pages, `ContentZoneEditor`, `InteractiveFormFields`, `RichTextEditor`
- `WebWayCMS.Presentation.Rendering` — `ICmsPageRenderer`, `IContentZoneResolver`, `IContentZoneWidgetRegistry`, `ICmsChromeRegistry`, `ICmsPageViewRegistry`, `IContentZoneViewRegistry`, `IFormOptionsProvider`, `AdminRoutes`

**Depends on:** Content Domain Models (ViewModels), Content Zone Component Framework, Identity (admin/account components gated by `[Authorize]`)
**Consumed by:** Page controllers (via `ICmsPageRenderer`), the host's optional branding components

---

## 1. Overview

The CMS view layer is **Blazor SSR**, shipped as a Razor Class Library (`WebWayCMS.Presentation`).
There are **no `.cshtml` views** and no view pre-compilation (`CompiledRazorAssemblyPart`) or runtime
Razor compilation — those MVC mechanisms were removed in the migration. Public pages render **Static
SSR**; the admin UI (`/admin/*`) and the content-zone editor render **Interactive Server**; the Identity
UI is Blazor components at `/Account/*` (see [Area 8](08-identity-auth.md)).

`App.razor` is the Blazor Web App root mapped by `MapRazorComponents<App>()`. Static assets ship as RCL
content under `_content/WebWayCMS.Presentation/` (e.g. `_content/WebWayCMS.Presentation/css/admin.css`).

---

## 2. Document Shell & Page Hosts

Page controllers do not return a view; they return a Blazor root component via `ICmsPageRenderer`
(`RazorComponentResult<CmsPageHost>` / `RazorComponentResult<AdminPageHost>` — see
[Area 3](03-page-routing.md)).

- **`CmsPageHost`** — root for public pages. Establishes the per-request `CmsRenderContext` cascade and
  renders the page body: a host `[CmsPageView]` component when the page selects one, otherwise the
  page's `Main` content zone. Wraps the body in `CmsLayout`.
- **`CmsLayout`** — the public **document shell**. Emits `<!DOCTYPE html>`, the `<HeadOutlet />`, the
  per-page `Meta`/`Style`/`Script`, and `<script src="_framework/blazor.web.js">`. If the host provides
  a `[CmsChrome]` component, the page body is wrapped in that chrome; otherwise it renders in a default
  `<main>`. The CMS owns the document and the framework script.
- **`AdminPageHost`** — root for admin pages returned by `GenericAdminPageController`. Emits the admin
  document shell (Bulma + Font Awesome + `admin.css` + a static admin navbar) and renders either the
  admin dashboard or the page's `Main` zone. Admin chrome is **CMS-owned and not host-branded**.
- **`AdminLayout`** — the `@layout` applied to the routable admin `@page` components (the CRUD pages),
  providing the shared admin shell for those pages.

---

## 3. Built-in Widgets

The four former ViewComponents are now Blazor widgets in `Components/Widgets/`, each decorated with
`[ContentZoneComponent]` and receiving its config through a `[Parameter] Config` (see
[Area 4](04-content-zone-framework.md)):

| Widget | Component name | Purpose |
|--------|----------------|---------|
| `ContentBlockWidget` | `ContentBlock` | Renders a reusable content block by ID (latest published version) |
| `ArticleWidget` | `Article` | Renders an article list or a single article (delegates to `ArticleList`/`ArticleDetail`) |
| `PageNavigationWidget` | `Page` (explicit `Name`) | Renders navigation / a page reference |
| `LayoutWidget` | `Layout` | Renders a selected multi-column layout template (see §4) |

The render-time name→type dispatch is handled by `IContentZoneWidgetRegistry` + `<DynamicComponent>` in
`ContentZone.razor`.

---

## 4. `LayoutWidget` Templates

`LayoutWidget` composes named zone slots into one of the built-in layout templates, selected by its
config's `ViewName`. Unknown/empty names fall back to `Default`. The template list is the single source
of truth in `LayoutWidget.Layouts` (also exposed as `LayoutWidget.LayoutViewNames` for the admin picker):

| Variant | Description |
|---------|-------------|
| `Default` | Single column (full width) |
| `SingleColumn` | Explicit single column |
| `CenteredNarrow` | Centered, constrained-width single column |
| `TwoColumnEqual` | Two equal 50/50 columns |
| `TwoColumnSidebar` | Main content + narrow sidebar |
| `OneThirdTwoThird` | 1/3 + 2/3 split |
| `ThreeColumn` | Three equal columns |
| `FourColumn` | Four equal columns |
| `HeaderContentFooter` | Three stacked rows (header/body/footer) |
| `HeroWithColumns` | Full-width hero row + columned body |
| `AsymmetricRightHeavy` | Narrow left + wide right |

Each variant renders named zone slots (`Column1`, `Column2`, `Header`, `Footer`, etc.) that editors
populate with widgets.

---

## 5. Admin Forms

`InteractiveFormFields.razor` is the metadata-driven form used by both the admin CRUD pages
(`AdminUpsert`/`AdminChildUpsert`/`AdminPageUpsert`) and the content-zone editor. It renders each
`FormPropertyInfo` (built by `FormPropertyBuilder`) by its `EditorType`
(Hidden/Text/TextArea/DateTime/Date/Number/Url/Email/Color/Checkbox/Dropdown + entity/view pickers via
`Options`), grouped by `FormProperty` group, binding directly to the model by reflection. `RichText`
fields render the `RichTextEditor` component (CKEditor 5 via the `wwwroot/js/richtext.js` interop — see
[Area 10](10-web-application.md)). Version history is rendered by `AdminVersionHistoryPage`.

---

## 6. Host-Extension Registries (replacing `IViewDiscoveryService`)

The old filesystem/compiled-view scanner `IViewDiscoveryService` (which populated `ViewPicker`
dropdowns) has been **removed**. View/component selection is now driven by three convention-scanned
registries (each scans the CMS Presentation assembly + the host entry assembly):

| Registry | Attribute | Selects | Dispatched by |
|----------|-----------|---------|---------------|
| `ICmsChromeRegistry` | `[CmsChrome]` (inherit `CmsChromeBase`) | the host's site chrome (header/nav/footer + `<head>` assets) | `CmsLayout` |
| `ICmsPageViewRegistry` | `[CmsPageView(ForController, Name)]` | an alternate page body, by controller + "View Name" | `CmsPageHost` |
| `IContentZoneViewRegistry` | `[ContentZoneView(ForComponent, Name)]` | an alternate widget rendering sharing the widget's `Config`, by component + "View" | `ContentZone` |

Built-in option lists come from the components themselves: `LayoutWidget.LayoutViewNames` supplies the
Layout widget's template names, and admin **option lists** for entity/view picker fields are produced
in-circuit by `IFormOptionsProvider` (entity pickers via the admin handlers' API lists; the Layout
`ViewPicker` via `LayoutWidget.LayoutViewNames`).

---

## 7. Host Branding & Overrides

A NuGet-consuming host customizes the **public** site with convention-scanned Blazor components — no
registration call needed (the entry assembly is scanned):

- **`[CmsChrome]`** (inherit `CmsChromeBase`) — supplies header/nav/footer and `<head>` assets (via
  `<HeadContent>`), wrapped around the page body by `CmsLayout`.
- **`[CmsPageView(ForController, Name)]`** — supplies an alternate page body, selectable in the admin
  "View Name" dropdown; consumed by `CmsPageHost`.
- **`[ContentZoneView(ForComponent, Name)]`** — supplies an alternate widget rendering sharing the
  widget's `Config`, selectable in the widget "View" dropdown; dispatched by `ContentZone` and persisted
  as `ContentZoneItemDTO.ViewName`.

The CMS owns the document shell and `blazor.web.js`; the **admin** chrome is not host-branded. Working
samples are in `WebWayCMS.TestHost/Components/` (`HostChrome.razor`, `WideHomeView.razor`,
`ContentBlockCardView.razor`). See [getting-started §6](../getting-started.md) and
[10-web-application §2.5](10-web-application.md).
