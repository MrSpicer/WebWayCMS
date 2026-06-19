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

On every request, `PageRouteTransformer` (a `DynamicRouteValueTransformer`) intercepts the catch-all route `{**slug}` registered in `Program.cs`. It:

1. Normalises the request path (lowercase, strips trailing slash).
2. Looks up the path in the `PageContext` database via `IPageService`. If no exact match, it progressively strips trailing segments to find the nearest parent page and stores the remainder as `CMS:SubRoute` in `HttpContext.Items`.
3. Resolves the matching page's `ControllerName` against `IPageControllerRegistry` (which holds every class decorated with `[PageController]`).
4. Deserialises the page's `ConfigurationJson` into the controller's declared config type and stores both the `PageDTO` and the config object in `HttpContext.Items`.
5. Returns `{ controller = ControllerName, action = "Index" }` — ASP.NET Core dispatches to `{ControllerName}Controller.Index()`.

The controller extends `PageControllerBase<TConfig>`, which exposes `CurrentPage` (the `PageDTO`) and `PageConfig` (the typed config) as read-only properties backed by `HttpContext.Items`. The `Index()` action typically renders a Razor view with `PageConfig` as the model, and the view places one or more **ContentZones** for admin-managed widget regions.

`PageControllerRegistry` scans both the CMS assembly and `Assembly.GetEntryAssembly()` (the Web project) at startup, so any controller decorated with `[PageController]` is discovered automatically — no manual registration is needed.

---

## Core Components

| Class | File | Role |
|---|---|---|
| `PageRouteTransformer` | `WebWayCMS.Routing/Routing/PageRouteTransformer.cs` | Resolves request path to a page record and populates `HttpContext.Items` |
| `PageControllerBase<TConfig>` | `WebWayCMS.Core/Controllers/PageControllerBase.cs` | Abstract base class; exposes `CurrentPage` and `PageConfig` |
| `[PageController]` | `WebWayCMS.Forms/Attributes/PageControllerAttribute.cs` | Marks a controller as a page type; drives admin UI metadata |
| `PageControllerRegistry` | `WebWayCMS.Routing/Pages/PageControllerRegistry.cs` | Scans assemblies at startup and caches page type metadata |
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

- `ConfigurationType` in `[PageController]` must match the generic type parameter on `PageControllerBase<T>`. This tells the route transformer which type to deserialise `ConfigurationJson` into, and tells the admin UI which properties to render as form fields.
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

`PageControllerRegistry` scans `Assembly.GetEntryAssembly()` (the Web project) automatically at startup. The new page type will appear in the admin page-creation UI under the `Category` specified in the attribute.

---

## Accessing Page Data in Your Controller

`PageControllerBase<TConfig>` exposes two read-only properties backed by `HttpContext.Items`:

```csharp
// The full database record for the current page
protected PageDTO? CurrentPage => HttpContext.Items["CMS:PageData"] as PageDTO;

// The deserialised configuration; falls back to new TConfig() if absent
protected TConfig PageConfig => HttpContext.Items["CMS:PageConfig"] as TConfig ?? new TConfig();
```

`PageDTO` fields available via `CurrentPage`:

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | Primary key of this version |
| `MasterId` | `Guid` | Stable identifier across all versions of the page |
| `Title` | `string` | Page title |
| `Slug` | `string` | URL-safe slug derived from the title |
| `Route` | `string` | Full URL path (e.g. `/about/team`) |
| `ControllerName` | `string` | Registered controller name |
| `ConfigurationJson` | `string` | Raw JSON used to populate `PageConfig` |
| `IsPublished` | `bool` | Publication state |
| `IsHidden` | `bool` | Hidden from navigation but still accessible |
| `Version` | `int` | Monotonically increasing version number |

For the full set of shared fields (exposed via `PageDTO.ContentMeta`), see `ContentDTO` in [`docs/content-system.md`](content-system.md).

**Sub-route access:** if the request path extends beyond the matched page route, the remainder is stored as a string in `HttpContext.Items["CMS:SubRoute"]`. Read it directly in your action when the page type handles its own child routing:

```csharp
var subRoute = HttpContext.Items["CMS:SubRoute"] as string;
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

*For architectural reference — routing algorithm, registry internals, `HttpContext.Items` contract, `NotReservedConstraint`, and built-in page types — see [docs/architecture/03-page-routing.md](architecture/03-page-routing.md).*
