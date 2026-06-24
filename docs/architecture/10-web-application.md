# Area 10: Web Application Layer

**Namespaces:**
- `MySite` — `Program.cs`, `MappingProfile` (the example host supplies only branding + startup)

**Depends on:** the `WebWayCMS` NuGet package (Bootstrap: `AddWebWayCms`, `EnsureCMS`) and all CMS extension points
**Consumed by:** Nothing (top of the dependency graph)

> The host references the CMS as a NuGet package, not by project reference. The view
> layer is Blazor SSR: the CMS owns the public document shell (`CmsLayout`) and admin
> chrome, and the host brands/customizes the public site with attribute-marked Blazor
> components (`[CmsChrome]`, `[CmsPageView]`, `[ContentZoneView]`) discovered by
> convention from the host assembly. See [getting-started](../getting-started.md).

---

## 1. Web Project vs CMS Library — What Belongs Where

| Belongs in Web project | Belongs in CMS library |
|------------------------|------------------------|
| Page types specific to this site | Generic page types (GenericPage, GenericAdminPage) |
| Site-specific widgets | Widget framework infrastructure |
| Site-specific content types | Content type framework (admin CRUD, versioning) |
| Site CSS/JS/fonts/icons | Admin UI CSS/JS (served from CMS library's wwwroot) |
| `Program.cs` startup | All service registrations, middleware, seeding |
| Public branding (`[CmsChrome]`), page/widget views | Public document shell (`CmsLayout`), admin chrome, `ErrorController` |
| Mapping profiles for Web-specific types | CMS built-in type mappings |

When a feature is purely about this site's content or design, it goes in the Web project. When a feature is reusable across any site running this CMS, it belongs in the CMS library.

---

## 2. The Extension Surfaces

The CMS provides these integration points for the Web project to customize behavior. All
are discovered by convention (assembly scan at startup) — no registration call required:

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
Create a Blazor component decorated with `[ContentZoneComponent]` that accepts its typed
configuration through a `Config` parameter:
```razor
@attribute [ContentZoneComponent("My Widget", typeof(MyWidgetConfiguration))]

<div>…</div>

@code {
    [Parameter] public MyWidgetConfiguration? Config { get; set; }
}
```
No registration required — `ContentZoneComponentRegistry` (admin metadata) and
`ContentZoneWidgetRegistry` (render dispatch) discover it. See [Area 4](04-content-zone-framework.md).

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

### 5. Public Branding & Views
Public pages render through `GenericPageController → ICmsPageRenderer → CmsPageHost → CmsLayout`
(Blazor SSR, not the Router). The CMS owns the document shell (`CmsLayout`: `<html>`/`<head>`/
`<HeadOutlet>` + `blazor.web.js`); the host customizes the public site with attribute-marked Blazor
components in its assembly:

- **`[CmsChrome]`** — header/nav/footer wrapped around every public page body. Inherit `CmsChromeBase`
  and render around `@ChildContent`; add `<head>` assets via `<HeadContent>`. Resolved by
  `ICmsChromeRegistry` (lowest `Order` wins); no chrome ⇒ the body renders in the default `<main>`.
- **`[CmsPageView(ForController, Name)]`** — an alternate page body for a page type, selected per page
  via the admin "View Name" dropdown. Resolved by `ICmsPageViewRegistry`; falls back to the page's
  `Main` content zone.
- **`[ContentZoneView(ForComponent, Name)]`** — an alternate rendering ("sub-view") of an existing
  widget sharing its `Config` model, selected per zone item via the admin "View" dropdown. Resolved by
  `IContentZoneViewRegistry`; falls back to the default widget.

Admin chrome (`AdminLayout` / `AdminPageHost`) is CMS-owned and intentionally not host-branded.
Working examples live in `WebWayCMS.TestHost/Components/`. See [getting-started §6](../getting-started.md).

---

## 3. `Program.cs` Walkthrough

```csharp
var builder = WebApplication.CreateBuilder(args);

MapTypes(builder.Services);                              // (1) Web-project DI registrations

builder.Services.AddWebWayCms(builder.Configuration);  // (2) CMS DI

builder.Host.UseCmsSerilog(builder.Configuration);       // (3) Serilog

builder.Services.AddControllersWithViews();              // (4) optional; AddWebWayCms already registers MVC + Blazor

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");                   // (5) Exception handler
    app.UseStatusCodePagesWithReExecute("/Error/{0}");   // (6) Status code handler (404, etc.)
}

app.EnsureCMS();                                         // (7) Migrations, seeding, middleware, route mapping

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

Both actions return a **self-contained inline HTML page** (`ContentResult` from the private
`ErrorContent()`), showing a generic message and the request ID. There is no `Error.cshtml` view and no
`ErrorViewModel` — the fallback intentionally has no Razor view/layout dependency so it renders even
when component rendering is what failed. Error handling is only active outside development (development
shows the detailed exception page).

---

## 5. Frontend Assets

**Host site assets** live in the Web project's `wwwroot/` (`css/site.css`, `js/site.js`, fonts, icons,
favicon, …) and are **static, host-maintained files** — there are no `.scss` sources or CMS build
targets that compile them (the former `CompileSass` / `CopyViewScripts` targets and the
`Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation` package were removed in the Blazor migration).
Branding/markup is supplied by the host's `[CmsChrome]`/`[CmsPageView]` components (§2.5), which pull in
their assets via `<HeadContent>`.

**CMS admin/editor assets** ship from the CMS library's `wwwroot/` (served as RCL content under
`_content/WebWayCMS.Presentation/`):
- `css/admin.css` — a **static, hand-maintained** stylesheet (no `.scss`), referenced by `AdminPageHost`
  / `AdminLayout`.
- `js/richtext.js` — the CKEditor 5 JS-interop wrapper for the `RichTextEditor` component. The licensed
  cloud-CDN build is gated on the `CKEDITOR_LICENSE_KEY` environment variable (read via the
  `CKEditor:LicenseKey` config key); when empty it falls back to the GPL/esm.sh build. The key must not
  be committed.
- `js/admin.js` — small admin-chrome interactions only. Inline content-zone editing
  (add/remove/drag-reorder, config forms) is **Blazor Interactive Server** (`ContentZoneEditor.razor`),
  not JavaScript.

**JS conventions:** No jQuery; vanilla JS only.

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
