# Area 10: Web Application Layer

**Namespaces:**
- `MySite` — `Program.cs`, `MappingProfile` (the example host supplies only branding + startup)

**Depends on:** the `WebWayCMS` NuGet package (Bootstrap: `AddWebWayCms`, `UseWebWayCms`) and all CMS extension points
**Consumed by:** Nothing (top of the dependency graph)

> The host references the CMS as a NuGet package, not by project reference. Generic
> chrome (the `ErrorController`, error view, validation/login partials, admin JS) now
> lives in the CMS libraries; the host overrides any CMS view by adding a file of the
> same name. See [getting-started](../getting-started.md).

---

## 1. Web Project vs CMS Library — What Belongs Where

| Belongs in Web project | Belongs in CMS library |
|------------------------|------------------------|
| Page types specific to this site | Generic page types (GenericPage, GenericAdminPage) |
| Site-specific widgets | Widget framework infrastructure |
| Site-specific code-based routes (`[CmsRoute]`) | The route table, transformer, and seeders |
| Content-type models, ViewModels, admin views | Content type framework (admin CRUD, versioning) — **and the DTO + EF configuration**, which must live in `WebWayCMS.Data` |
| Site CSS/JS/fonts/icons | Admin UI CSS/JS (served from `_content/WebWayCMS.Admin`) |
| `Program.cs` startup | All service registrations, middleware, seeding |
| Branding views (`_Layout`, nav/footer) | Generic chrome: error view, validation/login partials, `ErrorController` |
| Mapping profiles for Web-specific types | CMS built-in type mappings |

When a feature is purely about this site's content or design, it goes in the Web project. When a feature is reusable across any site running this CMS, it belongs in the CMS library.

---

## 2. The Extension Surfaces

The CMS provides five integration points for the Web project to customize behavior:

### 1. Custom Page Types
Extend `PageControllerBase<TConfig>` and decorate with `[PageController]`:
```csharp
[PageController("Blog", typeof(BlogPageConfiguration))]
public class BlogPageController : PageControllerBase<BlogPageConfiguration>
{
    public override async Task<IActionResult> Index()
    {
        // Route values captured from the URL pattern are in RouteData.Values
        // ...
    }
}
```
No registration required — the CMS reflects over the entry assembly at startup and seeds a
`PageControllerRegistrations` row for it. After that first run the database row is authoritative;
edit page-type metadata at `/wadmin/pagetypes`. See [Area 3](03-page-routing.md).

A page type is not a URL. Create a page at `/wadmin/pages`, pick the type, and give it a Slug — the
CMS derives the route pattern and writes it to `CMSRoutes`.

### 2. Custom Widgets
Extend `ViewComponent` and decorate with `[ContentZoneComponent]`:
```csharp
[ContentZoneComponent("My Widget", typeof(MyWidgetConfiguration))]
public class MyWidgetViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(MyWidgetConfiguration? configuration)
    {
        // ...
    }
}
```
No registration required — the widget is seeded into `WidgetRegistrations` at startup and served
thereafter by `IWidgetRegistry`. Manage it at `/wadmin/widgets`. See
[Area 4](04-content-zone-framework.md).

### 3. Custom Content Types
Create a domain model over the **unified `CmsDbContext`** and register it in DI:
```csharp
// In Program.cs MapTypes():
AddContentStore<MyThingDTO>(services, "mythings");
services.AddScoped<MyThingModel>();
services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<MyThingModel>());
```
The host does **not** create its own `DbContext` — there is one context for the whole CMS.

> The DTO and its `IEntityTypeConfiguration<T>` must live in `WebWayCMS.Data`, because
> `CmsDbContext` only scans its own assembly for configurations. For host-specific fields on an
> existing type, use `ContentVersion.CustomFields` (JSONB) instead. See
> [Area 1](01-data-tier.md#8-how-to-add-a-new-content-types-data-layer).

See [Area 5](05-content-domain-models.md) and [Area 6](06-admin-crud-framework.md).

### 4. Code-Based Routes
For URLs that belong to the application rather than to editor-managed content, decorate a
controller with one or more `[CmsRoute]` attributes:
```csharp
[CmsRoute("/search/{query?}", Order = 10, Action = "Search")]
[CmsRoute("/product/{id:int}", Order = 20, Action = "Product")]
public class CatalogController : Controller { /* ... */ }
```
The entry assembly is scanned at startup and each pattern is seeded into `CMSRoutes`. Seeding is
idempotent by pattern and never updates an existing row. See
[Area 3](03-page-routing.md#8-cmsroute--code-based-routes) and the worked example in the
[WebWayCMS.TestHost](https://github.com/MrSpicer/WebWayCMS.TestHost)
repo (`Controllers/CodeTestController.cs`).

### 5. Custom Mappings
Add to `MySite/MappingProfile.cs`:
```csharp
using WebWayCMS.Mapping;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<MyThingDTO, MyThingViewModel>(s => new MyThingViewModel { Id = s.Id, /* ... */ });
        CreateMap<MyThingViewModel, MyThingDTO>(s => new MyThingDTO { Id = s.Id, /* ... */ });
    }
}
```
Registered in `Program.cs` alongside CMS mappings.

---

## 3. `Program.cs` Walkthrough

```csharp
var builder = WebApplication.CreateBuilder(args);

MapTypes(builder.Services);                              // (1) Web-project DI registrations

builder.Services.AddWebWayCms(builder.Configuration);  // (2) CMS DI

builder.Host.UseCmsSerilog(builder.Configuration);       // (3) Serilog

var mvc = builder.Services.AddControllersWithViews();    // (4) MVC
if (builder.Environment.IsDevelopment())
    mvc.AddRazorRuntimeCompilation();                    // (5) Hot reload in dev

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");                   // (6) Exception handler
    app.UseStatusCodePagesWithReExecute("/Error/{0}");   // (7) Status code handler (404, etc.)
}

app.UseWebWayCms();                                         // (8) Migrations, seeding, middleware, route mapping

app.Run();
```

**Step 1** must happen before step 2 so Web-project DI registrations can be overridden or extended by the CMS.

Route registration lives inside `UseWebWayCms()` (specifically its `MapCmsEndpoints` step), so the Web project never maps the dynamic CMS route or the conventional fallback itself — it just calls `UseWebWayCms()`. The dynamic catch-all `{**slug}` matches everything not already claimed by an attribute-routed controller; if `CMSRouteTransformer` returns `null!`, routing falls through to the conventional `{controller=Home}/{action=Index}/{id?}` route. See [07-cms-bootstrap](07-cms-bootstrap.md) for the registration details and ordering.

A host that should render published content but never serve the admin UI calls
`AddWebWayCmsRendering` / `UseWebWayCmsRendering` instead — see [Area 11](11-deployment-modes.md).

---

## 4. `ErrorController`

```csharp
[Route("/Error")]
public IActionResult Index()
// Handles UseExceptionHandler — logs unhandled exceptions at Error level

[Route("Error/{statusCode}")]
public IActionResult StatusCodeHandler(int statusCode)
// Handles UseStatusCodePagesWithReExecute — logs status codes (like 404) at Warning level
```

Both actions render `Views/Shared/Error.cshtml` (must be provided by the Web project) with an `ErrorViewModel` containing the `RequestId`. Error handling is only active outside development (development shows the detailed exception page).

---

## 5. Frontend Assets

**The host owns only its public assets.** Everything the admin UI needs ships inside the packages
and is served from the library `wwwroot`s over the RCL `_content` convention — the host adds no
files for it:

| Asset | Served from |
|---|---|
| `admin.css`, `admin.js`, `bulma.min.css`, `content-zone-edit.css/js`, `page-upsert.js` | `~/_content/WebWayCMS.Admin/...` |
| `bulma.min.css`, `validation.js` for the public/Identity pages | `~/_content/WebWayCMS.Presentation/...` |
| CKEditor 5, Bulma, Font Awesome | CDNs, allow-listed by the default CSP |

So a host's `wwwroot/` contains only its own branding — site CSS/JS, fonts, icons, favicon,
`robots.txt`. There is no CMS-mandated structure and no CMS-provided Sass pipeline for it; compile
site styles however the host prefers.

**Sass inside the CMS.** `WebWayCMS.Admin.csproj` has a `CompileSass` build target that runs
`npx sass` over `Views/Shared/Components/ContentZone/edit.scss`, plus a `CopyViewScripts` target
that copies view-adjacent JS into `wwwroot/js/`. That is a CMS build concern, not a host one.

**JS conventions:** No jQuery. Vanilla JS only. `admin.js` handles inline zone editing
(drag-to-reorder, add/remove widgets, CKEditor initialization for RichText fields) and reads the
CKEditor license key from the `ckeditor-license-key` meta tag rather than an inline `<script>`, so
the CSP `script-src` needs no `'unsafe-inline'`.

**Dev loop.** The [WebWayCMS.TestHost](https://github.com/MrSpicer/WebWayCMS.TestHost)
repo's `Scripts/HotReloadRun.sh` runs `dotnet watch run` in Development, which picks up both C# and
Razor edits.

---

## 6. When to Add to Web Project vs CMS Library

**Add to the Web project when:**
- The feature is site-specific (content types, page types, widgets unique to this site)
- The feature needs direct access to Web project views or assets
- It's a customization of CMS defaults (override a view, extend a mapping)

**Add to the CMS library when:**
- The feature is generically useful to any site running this CMS
- It's part of the admin infrastructure (new admin controller, new service, new framework feature)
- It should be versioned and deployed independently of site content

When in doubt, start in the Web project. Extract to the CMS library only when the need for reuse is clear.
