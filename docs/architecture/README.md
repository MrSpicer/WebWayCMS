# Architecture Overview

This document maps the logical architecture of WebWayCMS. The system is a modular ASP.NET Core 10 CMS built as 8 focused class libraries (all prefixed `WebWayCMS.*`) consumed by a host web project (`MySite` is used as the example host name).

The libraries are distributed as NuGet packages: a host references the single umbrella package **`WebWayCMS`**, which transitively pulls the other seven. The view layer is **Blazor SSR** — `WebWayCMS.Presentation` is a Razor Class Library, so its Blazor components and admin CSS/JS ship inside the packages (served under `_content/WebWayCMS.Presentation/`). To stand up a new site, see [getting-started](../getting-started.md).

## Library Structure

| Library | Contents |
|---|---|
| `WebWayCMS.Data` | DTOs, DbContexts, Services, Migrations |
| `WebWayCMS.Identity` | UserService, DevEmailSender |
| `WebWayCMS.Forms` | Attributes, FormPropertyBuilder |
| `WebWayCMS.Routing` | PageRouteTransformer, PageControllerRegistry |
| `WebWayCMS.ContentZones` | ContentZoneComponentRegistry |
| `WebWayCMS.Core` | Controllers, Domain Models, ViewModels, MappingProfile |
| `WebWayCMS.Presentation` | Blazor components (Razor SSR): layout/hosts, widgets, admin pages, account components, Rendering services, wwwroot |
| `WebWayCMS` | Bootstrap: ServiceCollectionExtensions, CMSExtensions, SerilogExtensions |

---

## Architecture Map

```
┌─────────────────────────────────────────────────────────────────────┐
│  Web Application Layer  (MySite — example host)                     │
│  Program.cs · custom page types · widgets                           │
│  MappingProfile · [CmsChrome] branding · wwwroot                    │
└─────────────────────────────────┬───────────────────────────────────┘
                                  │ calls
                                  ▼
┌─────────────────────────────────────────────────────────────────────┐
│  CMS Bootstrap & Application Startup                                │
│  ServiceCollectionExtensions · CMSExtensions · SerilogExtensions    │
└──┬──────────────┬──────────────┬──────────────┬─────────────────────┘
   │              │              │              │ registers / configures
   ▼              ▼              ▼              ▼
┌──────────┐ ┌───────────────┐ ┌──────────────────┐ ┌──────────────┐
│ Identity │ │ Admin CRUD    │ │ Page Routing     │ │ Content Zone │
│ & Auth   │ │ Framework     │ │ Subsystem        │ │ Component    │
│          │ │               │ │                  │ │ Framework    │
│ Users    │ │ Blazor admin  │ │ PageRoute        │ │ ContentZone  │
│ Roles    │ │ pages         │ │ Transformer      │ │ .razor       │
│ UserSvc  │ │ AdminUpsert   │ │ PageController   │ │ Resolver +   │
│ DevEmail │ │ IAdminCrud    │ │ Base<TConfig>    │ │ Widget       │
│ /Account │ │ Handler +     │ │ [PageController] │ │ Registry     │
│ comps    │ │ AdminHandler  │ │ PageController   │ │ [ContentZone │
│          │ │ Registry      │ │ Registry         │ │ Component]   │
└──────────┘ └───────┬───────┘ └──────┬───────────┘ └──────┬───────┘
                     │                │                     │
                     │ resolves       │ extends / reads      │ renders
                     ▼                ▼                     ▼
┌─────────────────────────────────────────────────────────────────────┐
│  Content Domain Models                                              │
│  PageModel · ContentBlockModel · ArticleListModel · ArticleModel    │
│  ContentZoneModel · AdminCrudModel<T> · VersionedModel<T>           │
│  ViewModels · ContentZoneConfigurations · MappingProfiles           │
└────────────────────────────────────┬────────────────────────────────┘
                                     │ uses
                                     ▼
┌─────────────────────────────────────────────────────────────────────┐
│  Form Generation & Configuration Metadata                           │
│  [FormProperty] · EditorType · FormPropertyBuilder                  │
│  FormPropertyInfo · InteractiveFormFields.razor                     │
│  [PageController] · [ContentZoneComponent]                          │
└─────────────────────────────────────────────────────────────────────┘
                                     │ reads type metadata
                                     ▼
┌─────────────────────────────────────────────────────────────────────┐
│  Data Tier                                                          │
│  IContent (has-a ContentDTO) ← PageDTO · ArticleDTO · etc.          │
│  ApplicationDbContext · ArticleContext · PageContext · etc.          │
│  IContentService<T> · IPageService · IContentZoneService            │
└─────────────────────────────────────────────────────────────────────┘
                                     │
                                     ▼
                              PostgreSQL Database

┌─────────────────────────────────────────────────────────────────────┐
│  Blazor Presentation Layer  (cross-cutting rendering layer)         │
│  CmsLayout · CmsPageHost · AdminPageHost · ContentZone.razor        │
│  Widgets (ContentBlock/Article/Layout/PageNavigation)               │
│  Admin Blazor pages · chrome/page-view/zone-view registries         │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Area Summaries

### [Area 1: Data Tier](01-data-tier.md)
Five independent EF Core `DbContext` classes share one PostgreSQL connection string with separate migration history tables. The shared `ContentDTO` (composed via `IContent`, persisted to one `Content` table owned by `ArticleContext`) defines the universal versioning pattern (Id/MasterId/Version). `IContentService<T>` provides generic versioned CRUD; `IPageService` adds route-specific logic; `IContentZoneService` manages zones, items, and assignment-based slot resolution with transaction-safe lazy zone creation.

### [Area 2: Form Generation & Configuration Metadata](02-form-generation.md)
Pure-reflection subsystem that drives all admin form rendering from C# attributes. `[FormProperty]` decorates config class properties with editor type, validation hints, and layout options. `FormPropertyBuilder` reflects these into `List<FormPropertyInfo>`. `InteractiveFormFields.razor` renders that list as a Bulma-styled Blazor form — no per-type Razor form boilerplate needed.

### [Area 3: Page Routing Subsystem](03-page-routing.md)
A `DynamicRouteValueTransformer` (`PageRouteTransformer`) intercepts the `{**slug}` catch-all route and resolves URLs against the `Pages` table using a five-step algorithm (exact match → progressive parent match → root fallback → registry lookup). Page data and config are stored in `HttpContext.Items` for the dispatched controller, which returns a Blazor root (`CmsPageHost`) via `ICmsPageRenderer`. `PageControllerRegistry` is a startup singleton that scans assemblies for `[PageController]`-decorated controllers.

### [Area 4: Content Zone Component Framework](04-content-zone-framework.md)
Database-backed widget system. Zones are named slots placed with `<ContentZone />`; each zone holds ordered `ContentZoneItem` rows referencing a widget component by name plus a JSON config blob. `IContentZoneResolver` resolves zones via a priority chain (direct ID → nested → page-scoped → global) and lazily creates zones in transactions on first render; `ContentZone.razor` dispatches each item to its Blazor widget via `<DynamicComponent>`. `ContentZoneComponentRegistry` scans for `[ContentZoneComponent]`-decorated Blazor components at startup.

### [Area 5: Content Domain Models](05-content-domain-models.md)
The business logic tier. `VersionedModel<T>` provides version history assembly. `AdminCrudModel<T>` extends it and implements `IAdminCrudHandler`, giving each model class dual identity: domain orchestrator and admin CRUD handler. Built-in types: `PageModel`, `ArticleListModel`/`ArticleModel` (top-level + child), `ContentBlockModel`, `ContentZoneModel`. In-house mapping profiles handle DTO-to-ViewModel mapping.

### [Area 6: Admin CRUD Framework](06-admin-crud-framework.md)
Interactive Server Blazor admin pages drive all content administration. The generic `AdminUpsert` / `AdminChildUpsert` / `AdminVersionHistoryPage` components resolve a registered `IAdminCrudHandler` via `AdminHandlerRegistry` to render create/edit/version flows — no per-type controllers or views. `ContentZoneEditor.razor` handles inline zone editing. (The former MVC `AdminContentController`/`AdminContentZoneController`/`ContentZoneApiController` are deleted.)

### [Area 7: CMS Bootstrap & Application Startup](07-cms-bootstrap.md)
The composition root. `AddWebWayCms` registers all five DbContexts, services, singletons, domain models (as both interfaces and handlers), the in-house `IMapper`, MVC controllers, and the Blazor Web App services (`AddRazorComponents().AddInteractiveServerComponents()`). `EnsureCMS` runs four startup tasks in sequence: migrate all contexts (with retry), seed roles and admin user, seed default pages, configure the middleware pipeline (including `MapRazorComponents<App>()`).

### [Area 8: Identity & Authentication](08-identity-auth.md)
Three roles: `Admin` (full access), `Editor` (content write access on permitted types), `User` (authenticated, no admin access). `UserService` singleton provides `IsUserAdmin`/`IsUserAuthor` for role checks in components. Admin user is seeded from `AdminUser:Email`/`AdminUser:Password` secrets at startup. Password policy requires 12+ characters with digits, upper, lower, and non-alphanumeric characters. The Identity UI is Blazor components at `/Account/*` (`AddCmsBlazorIdentity()`), not scaffolded Razor Pages.

### [Area 9: Blazor Presentation Layer](09-cms-presentation.md)
The CMS view layer is Blazor SSR shipped as a Razor Class Library — no `.cshtml` or compiled-view application parts. `CmsLayout` is the public document shell; `CmsPageHost`/`AdminPageHost` are the page roots returned by the controllers; `ContentZone.razor` renders zones; the built-in widgets (`ContentBlockWidget`, `ArticleWidget`, `LayoutWidget`, `PageNavigationWidget`) replace the former ViewComponents. Host customization flows through three convention-scanned registries (`ICmsChromeRegistry`, `ICmsPageViewRegistry`, `IContentZoneViewRegistry`), which replaced the removed `IViewDiscoveryService`.

### [Area 10: Web Application Layer](10-web-application.md)
The host project is the top of the dependency graph. It provides four extension surfaces: custom page types (`PageControllerBase<TConfig>` + `[PageController]`), custom widgets (`.razor` Blazor component + `[ContentZoneComponent]`), custom content types (DTO + DbContext + `AdminCrudModel<T>`), and custom mapping profiles — plus public branding via `[CmsChrome]`/`[CmsPageView]`/`[ContentZoneView]`. `ErrorController` returns a self-contained inline HTML page. Frontend assets are static (no Sass build step); the CMS's `admin.css` is hand-maintained and CKEditor loads via `richtext.js`.

---

## Dependency Direction Guide

Reading order for newcomers:

```
1. Data Tier            — understand DTOs, versioning, services
2. Form Generation      — understand how admin forms are declared
3. Page Routing         — understand how URLs map to controllers
4. Content Zone FW      — understand how widgets work
5. Content Domain Models — understand how model classes orchestrate the above
6. Admin CRUD FW        — understand how admin pages are handled
7. CMS Bootstrap        — understand DI wiring and startup sequence
8. Identity & Auth      — understand roles and user service
9. Blazor Presentation  — understand components, widgets, host extension points
10. Web Application     — understand how to extend the CMS in the host project
```

Dependencies only flow downward in this list. A layer only references layers beneath it.

---

## Related How-To Guides

- [docs/page-system.md](../page-system.md) — Creating a custom page type (step-by-step)
- [docs/widget-system.md](../widget-system.md) — Creating a custom widget (step-by-step)
- [docs/content-system.md](../content-system.md) — Creating a custom content type (step-by-step)
