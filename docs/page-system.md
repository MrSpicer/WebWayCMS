# Page System

The page system drives dynamic URL routing — every database-managed page is dispatched to a custom controller type that you define in the Web project (or the CMS library).

## Table of Contents

- [System Overview](#system-overview)
- [Core Components](#core-components)
- [Creating a Custom Page Type](#creating-a-custom-page-type)
- [Accessing Page Data in Your Controller](#accessing-page-data-in-your-controller)
- [Placing Content Zones in Your View](#placing-content-zones-in-your-view)
- [\[PageController\] Attribute Reference](#pagecontroller-attribute-reference)

---

## System Overview

Routing is database-backed. A page's URL is a row in the `CMSRoutes` table, derived from the page's
**Slug** and written whenever the page is saved.

On every request, `CMSRouteTransformer` (a `DynamicRouteValueTransformer`) intercepts the catch-all
route `{**slug}` registered by the CMS inside `EnsureCMS()`. It:

1. Normalises the request path (lowercase, strips a trailing slash).
2. Matches the path against the active `CMSRoutes` patterns via `ICMSRouteService.MatchRouteAsync`, ordered by `Order`. First match wins; reserved routes are skipped.
3. Reads `controller` (required) and `action` (default `"Index"`) from the matched route's `DefaultsJson`, and resolves the controller against `IPageControllerRegistry`.
4. Loads the owning page (via the route's `OwningContentMasterId`) and deserialises the config carried in the route's `DataTokens["ConfigurationJson"]` into the controller's declared config type, storing both the `PageDTO` and the config object in `HttpContext.Items`.
5. Returns `{ controller, action }` plus any route values captured by the pattern — ASP.NET Core dispatches to `{ControllerName}Controller.Index()`.

The controller extends `PageControllerBase<TConfig>`, which exposes `CurrentPage` (the `PageDTO`) and `PageConfig` (the typed config) as read-only properties backed by `HttpContext.Items`. The `Index()` action typically renders a Razor view with `PageConfig` as the model, and the view places one or more **ContentZones** for admin-managed widget regions.

`[PageController]`-decorated controllers are discovered by reflection **once at startup** and seeded into the `PageControllerRegistrations` table; `IPageControllerRegistry` then serves them from the database with a 5-minute cache. So a new page type still needs no manual registration — but after the first run the database is the source of truth, and you edit page-type metadata at `/admin/pagetypes`.

---

## Core Components

| Class | File | Role |
|---|---|---|
| `CMSRouteTransformer` | `WebWayCMS.Routing/Routing/CMSRouteTransformer.cs` | Matches the request path to a `CMSRoutes` row and populates `HttpContext.Items` |
| `ICMSRouteService` | `WebWayCMS.Data/Data/Services/CMSRouteService.cs` | Stores and matches route patterns |
| `PageControllerBase<TConfig>` | `WebWayCMS.Core/Controllers/PageControllerBase.cs` | Abstract base class; exposes `CurrentPage` and `PageConfig` |
| `[PageController]` | `WebWayCMS.Forms/Attributes/PageControllerAttribute.cs` | Marks a controller as a page type; drives admin UI metadata |
| `PageControllerRegistry` | `WebWayCMS.Routing/Pages/PageControllerRegistry.cs` | Caches page-type metadata loaded from the database |
| `IRouteRegistrationService` | `WebWayCMS.Core/Services/RouteRegistrationService.cs` | Writes a page's route row on save; unpublishes it on unpublish |
| `GenericPageController` | `WebWayCMS.Core/Controllers/GenericPageController.cs` | Built-in default page type; canonical implementation example |

---

## Creating a Custom Page Type

### Step 1 — (Optional) Create a configuration class

Configuration properties appear as form fields in the admin page-edit UI. Omit this class entirely if the page type needs no configuration.

**`MySite/Pages/MyPageConfiguration.cs`**

```csharp
using WebWayCMS.Attributes;

namespace MySite.Pages;

public class MyPageConfiguration
{
    [FormProperty(Label = "Heading", EditorType = EditorType.Text, Order = 1)]
    public string Heading { get; set; } = string.Empty;

    [FormProperty(Label = "Show Sidebar", EditorType = EditorType.Checkbox, Order = 2)]
    public bool ShowSidebar { get; set; }
}
```

Properties without `[FormProperty]` are ignored by both the admin form generator and the JSON deserialiser.

### Step 2 — Create the controller

**`MySite/Pages/MyPageController.cs`**

```csharp
using WebWayCMS.Attributes;
using WebWayCMS.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace MySite.Pages;

[PageController(
    DisplayName = "My Page",
    Description = "A custom page with a sidebar option.",
    Category = "General",
    ConfigurationType = typeof(MyPageConfiguration),
    Order = 10)]
public class MyPageController : PageControllerBase<MyPageConfiguration>
{
    private readonly Serilog.ILogger _logger = Serilog.Log.ForContext<MyPageController>();

    public override Task<IActionResult> Index()
    {
        _logger.Information("Rendering MyPage: {PageId} - {Title}",
            CurrentPage?.Id,
            CurrentPage?.Title);

        return Task.FromResult<IActionResult>(View(PageConfig));
    }
}
```

- `ConfigurationType` in `[PageController]` must match the generic type parameter on `PageControllerBase<T>`. This tells the route transformer which type to deserialise the stored configuration JSON into, and tells the admin UI which properties to render as form fields.
- Constructor injection works normally — add parameters and they will be resolved from DI.

### Step 3 — Create the Razor view

**`MySite/Views/MyPage/Index.cshtml`**

```cshtml
@model MySite.Pages.MyPageConfiguration

@{
    ViewData["Title"] = ViewContext.RouteData.Values["title"]?.ToString() ?? "Page";
}

<h1>@Model.Heading</h1>

@await Component.InvokeAsync("ContentZone", new { zoneName = "Main" })

@if (Model.ShowSidebar)
{
    @await Component.InvokeAsync("ContentZone", new { zoneName = "Sidebar" })
}
```

The view name must be `Index.cshtml` and the folder name must match the controller name without the `Controller` suffix (i.e. `MyPage` for `MyPageController`).

### Step 4 — No registration required

At startup the CMS scans `Assembly.GetEntryAssembly()` (the Web project) along with its own
assemblies and seeds a `PageControllerRegistrations` row for each `[PageController]` it has not
seen before. The new page type then appears in the admin page-creation UI under the `Category`
specified in the attribute.

> Seeding only *inserts*. Once a page type has been seeded, editing its `[PageController]`
> attribute in code will not update the stored row — change it at `/admin/pagetypes` instead.
> Set `WEBWAYCMS_SKIP_DEFAULTPAGECONTROLLERS=true` to suppress seeding entirely.

### Step 5 — Give the page a URL

Page types are not URLs. Create a page in the admin UI at `/admin/pages`, pick your page type from
the **Controller** dropdown, and set its **Slug**. The CMS derives the route pattern from the slug
and writes it to `CMSRoutes`:

- slug `about` at the root ⇒ `/about`
- slug `team` created under `/about` ⇒ `/about/team`
- the slug `home` is special-cased to the site root, `/`

Unpublishing a page unpublishes its route, which removes the URL without deleting the page. For
URLs that belong to the application rather than to editor-managed content, use `[CmsRoute]` on the
controller instead — see [architecture/03-page-routing.md](architecture/03-page-routing.md#8-cmsroute--code-based-routes).

---

## Accessing Page Data in Your Controller

`PageControllerBase<TConfig>` exposes two read-only properties backed by `HttpContext.Items`:

```csharp
// The full database record for the current page
protected PageDTO? CurrentPage => HttpContext.Items["CMS:PageData"] as PageDTO;

// The deserialised configuration; falls back to new TConfig() if absent
protected TConfig PageConfig => HttpContext.Items["CMS:PageConfig"] as TConfig ?? new TConfig();
```

`PageDTO` is deliberately small — it carries only page-specific fields, plus the shared
`ContentMeta`:

| Property | Type | Description |
|---|---|---|
| `ContentId` | `Guid` | Shared primary key / FK into the `Content` table; equals `ContentMeta.Id` |
| `ContentMeta` | `ContentDTO` | All shared fields (see below) |
| `ViewName` | `string?` | Optional view override; when set, the controller renders this view instead of `Index` |
| `ConfigurationJson` | `string` | Raw JSON for the page's controller configuration |

Shared fields are read through `CurrentPage.ContentMeta`:

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | Primary key of this version |
| `MasterId` | `Guid` | Stable identifier across all versions of the page |
| `Title` | `string` | Page title |
| `Slug` | `string` | URL segment; the page's route pattern is derived from this |
| `IsPublished` | `bool` | Publication state; unpublishing also unpublishes the page's route |
| `IsHidden` | `bool` | Hidden from navigation but still accessible |
| `Version` | `int` | Monotonically increasing version number |

There is **no `Route` or `ControllerName` on `PageDTO`** — both live on the page's `CMSRoutes` row
(`Pattern`, and `controller` inside `DefaultsJson`). Read the matched row from
`HttpContext.Items["CMS:RouteData"]` if a page type needs its own URL at runtime. For the full set
of shared fields, see `ContentDTO` in [`docs/content-system.md`](content-system.md).

**Route value access:** values captured by the route pattern are available as ordinary route values
through `RouteData.Values`:

```csharp
var id = RouteData.Values["id"] as string;
```

---

## Placing Content Zones in Your View

ContentZones are admin-managed widget regions. Invoke them from your view with:

```cshtml
@await Component.InvokeAsync("ContentZone", new { zoneName = "Main" })
```

Each zone name is scoped to the current page's `MasterId` automatically. For zones shared across all pages (e.g. a footer), pass `IsGlobal = true`:

```cshtml
@await Component.InvokeAsync("ContentZone", new { zoneName = "Footer", IsGlobal = true })
```

See [`docs/widget-system.md`](widget-system.md) for full ContentZone documentation including how to create new widget types.

---

## [PageController] Attribute Reference

| Property | Type | Default | Description |
|---|---|---|---|
| `DisplayName` | `string` | Controller name (spaced) | Label shown in the admin page-type dropdown |
| `Description` | `string` | `""` | Help text shown in the admin UI |
| `Category` | `string` | `"General"` | Groups related page types in the dropdown |
| `ConfigurationType` | `Type?` | `null` | Config class whose `[FormProperty]` properties are rendered as form fields; must match the `TConfig` generic parameter |
| `IconClass` | `string` | `""` | CSS class for the icon shown in the admin UI (e.g. `"fa-file"`) |
| `Order` | `int` | `0` | Sort order within the category; lower values appear first |

---

*For architectural reference — the matching algorithm and pattern syntax, `[CmsRoute]`, reserved routes, registry internals, the `HttpContext.Items` contract, `NotReservedConstraint`, and built-in page types — see [docs/architecture/03-page-routing.md](architecture/03-page-routing.md).*
