# Area 10: Web Application Layer

**Namespaces:**
- `MySite` — `Program.cs`, `MappingProfile` (the example host supplies only branding + startup)

**Depends on:** the `WebWayCMS` NuGet package (Bootstrap: `AddWebWayCms`, `EnsureCMS`) and all CMS extension points
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
| Site-specific content types | Content type framework (admin CRUD, versioning) |
| Site CSS/JS/fonts/icons | Admin UI CSS/JS (served from CMS library's wwwroot) |
| `Program.cs` startup | All service registrations, middleware, seeding |
| Branding views (`_Layout`, nav/footer) | Generic chrome: error view, validation/login partials, `ErrorController` |
| Mapping profiles for Web-specific types | CMS built-in type mappings |

When a feature is purely about this site's content or design, it goes in the Web project. When a feature is reusable across any site running this CMS, it belongs in the CMS library.

---

## 2. The Four Extension Surfaces

The CMS provides four integration points for the Web project to customize behavior:

### 1. Custom Page Types
Extend `PageControllerBase<TConfig>` and decorate with `[PageController]`:
```csharp
[PageController("Blog", typeof(BlogPageConfiguration))]
public class BlogPageController : PageControllerBase<BlogPageConfiguration>
{
    public override async Task<IActionResult> Index()
    {
        var subRoute = HttpContext.Items["CMS:SubRoute"] as string;
        // ...
    }
}
```
No registration required — `PageControllerRegistry` discovers it at startup. See [Area 3](03-page-routing.md).

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
No registration required — `ContentZoneComponentRegistry` discovers it. See [Area 4](04-content-zone-framework.md).

### 3. Custom Content Types
Create a DTO, DbContext, domain model, and register in DI:
```csharp
// In Program.cs MapTypes():
services.AddDbContext<MyThingContext>(options => options.UseNpgsql(...));
services.AddScoped<IContentService<MyThingDTO>>(sp =>
    new ContentService<MyThingDTO>(sp.GetRequiredService<MyThingContext>()));
services.AddScoped<MyThingModel>();
services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<MyThingModel>());
```
See [Area 5](05-content-domain-models.md) and [Area 6](06-admin-crud-framework.md).

### 4. Custom Mappings
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

app.EnsureCMS();                                         // (8) Migrations, seeding, middleware, route mapping

app.Run();
```

**Step 1** must happen before step 2 so Web-project DI registrations can be overridden or extended by the CMS.

Route registration now lives inside `EnsureCMS()` (specifically its `ConfigureMiddleware` step), so the Web project no longer maps the dynamic page route or the conventional fallback itself — it just calls `EnsureCMS()`. The dynamic catch-all `{**slug}` matches everything; if the transformer returns `null!`, routing falls through to the conventional `{controller=Home}/{action=Index}/{id?}` route. See [07-cms-bootstrap](07-cms-bootstrap.md) for the registration details and ordering.

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

`wwwroot/` structure:

```
wwwroot/
├── css/
│   ├── site.css          ← compiled from site.scss (run ./Scripts/HotReloadRun.sh)
│   ├── animations.css
│   └── print.css
├── js/
│   ├── site.js           ← main site JavaScript
│   ├── admin.js          ← admin UI interactions (zone editing, drag-reorder, CKEditor init)
│   ├── animations.js
│   └── typewriter.js
├── fonts/
│   ├── InterVariable.woff2
│   └── FiraCode-VF.woff2
├── icons/
│   └── sprite.svg        ← SVG icon sprite (reference via <use href="/icons/sprite.svg#icon-name">)
├── favicon.ico
├── favicon.svg
└── robots.txt
```

**Sass compilation:** `site.css` is generated from a `.scss` source file. The hot-reload script (`./Scripts/HotReloadRun.sh`) runs both `dotnet watch run` and a Sass watcher in parallel. Run this script for development — do not edit `site.css` directly.

**JS conventions:** No jQuery. Vanilla JS only. `admin.js` handles inline zone editing (drag-to-reorder, add/remove widgets, CKEditor initialization for RichText fields). `site.js` is the public-facing entry point.

**Icon sprite:** SVG symbols bundled into `sprite.svg`. Reference icons in Razor views with:
```html
<svg><use href="/icons/sprite.svg#icon-name" /></svg>
```

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
