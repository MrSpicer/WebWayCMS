# Architecture Overview

This document maps the logical architecture of WebWayCMS. The system is a modular ASP.NET Core 10 CMS built as 8 focused class libraries (all prefixed `WebWayCMS.*`) consumed by a host web project (`MySite` is used as the example host name).

The libraries are distributed as NuGet packages: a host references the single umbrella package **`WebWayCMS`**, which transitively pulls the other seven (compiled Razor views and admin CSS/JS ship inside the packages). To stand up a new site, see [getting-started](../getting-started.md).

## Library Structure

| Library | Contents |
|---|---|
| `WebWayCMS.Data` | DTOs, DbContexts, Services, Migrations |
| `WebWayCMS.Identity` | UserService, DevEmailSender |
| `WebWayCMS.Forms` | Attributes, FormPropertyBuilder, FormFieldsTagHelper |
| `WebWayCMS.Routing` | PageRouteTransformer, PageControllerRegistry |
| `WebWayCMS.ContentZones` | ContentZoneComponentRegistry |
| `WebWayCMS.Core` | Controllers, Domain Models, ViewModels, MappingProfile |
| `WebWayCMS.Presentation` | ViewComponents, Views, Areas, wwwroot |
| `WebWayCMS` | Bootstrap: ServiceCollectionExtensions, CMSExtensions, SerilogExtensions |

---

## Architecture Map

```
┌─────────────────────────────────────────────────────────────────────┐
│  Web Application Layer  (MySite — example host)                     │
│  Program.cs · custom page types · widgets                           │
│  MappingProfile · branding views · wwwroot                          │
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
│ Users    │ │ AdminContent  │ │ PageRoute        │ │ ContentZone  │
│ Roles    │ │ Controller    │ │ Transformer      │ │ ViewComponent│
│ UserSvc  │ │ IAdminCrud    │ │ PageController   │ │ Registry     │
│ DevEmail │ │ Handler       │ │ Base<TConfig>    │ │ [ContentZone │
│          │ │ AdminHandler  │ │ [PageController] │ │ Component]   │
│          │ │ Registry      │ │ PageController   │ │              │
│          │ │ ContentZone   │ │ Registry         │ │              │
│          │ │ ApiController │ │                  │ │              │
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
│  FormPropertyInfo · FormFieldsTagHelper                             │
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
│  CMS View Components & Presentation  (cross-cutting rendering layer)│
│  PageViewComponent · ContentBlockViewComponent · ArticleViewComponent│
│  LayoutViewComponent · Admin Razor views · IViewDiscoveryService    │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Area Summaries

### [Area 1: Data Tier](01-data-tier.md)
Five independent EF Core `DbContext` classes share one PostgreSQL connection string with separate migration history tables. The shared `ContentDTO` (composed via `IContent`, persisted to one `Content` table owned by `ArticleContext`) defines the universal versioning pattern (Id/MasterId/Version). `IContentService<T>` provides generic versioned CRUD; `IPageService` adds route-specific logic; `IContentZoneService` manages zones, items, and assignment-based slot resolution with transaction-safe lazy zone creation.

### [Area 2: Form Generation & Configuration Metadata](02-form-generation.md)
Pure-reflection subsystem that drives all admin form rendering from C# attributes. `[FormProperty]` decorates config class properties with editor type, validation hints, and layout options. `FormPropertyBuilder` reflects these into `List<FormPropertyInfo>`. `FormFieldsTagHelper` (`<form-fields for="@Model">`) renders Bulma-styled HTML from that list — no per-type Razor form boilerplate needed.

### [Area 3: Page Routing Subsystem](03-page-routing.md)
A `DynamicRouteValueTransformer` (`PageRouteTransformer`) intercepts the `{**slug}` catch-all route and resolves URLs against the `Pages` table using a five-step algorithm (exact match → progressive parent match → root fallback → registry lookup). Page data and config are stored in `HttpContext.Items` for the dispatched controller. `PageControllerRegistry` is a startup singleton that scans assemblies for `[PageController]`-decorated controllers.

### [Area 4: Content Zone Component Framework](04-content-zone-framework.md)
Database-backed widget system. Zones are named slots in views; each zone holds ordered `ContentZoneItem` rows referencing a ViewComponent by name plus a JSON config blob. `ContentZoneViewComponent` resolves zones via a priority chain (direct ID → nested → page-scoped → global) and lazily creates zones in transactions on first render. `ContentZoneComponentRegistry` scans for `[ContentZoneComponent]`-decorated ViewComponents at startup.

### [Area 5: Content Domain Models](05-content-domain-models.md)
The business logic tier. `VersionedModel<T>` provides version history assembly. `AdminCrudModel<T>` extends it and implements `IAdminCrudHandler`, giving each model class dual identity: domain orchestrator and admin CRUD handler. Built-in types: `PageModel`, `ArticleListModel`/`ArticleModel` (top-level + child), `ContentBlockModel`, `ContentZoneModel`. In-house mapping profiles handle DTO-to-ViewModel mapping.

### [Area 6: Admin CRUD Framework](06-admin-crud-framework.md)
Single `AdminContentController` handles all content type admin routes by delegating to registered `IAdminCrudHandler` implementations via `AdminHandlerRegistry`. Supports top-level CRUD, child resource CRUD (via `IAdminCrudChildHandler`), version history, drag-reorder, and registry endpoints — all routed without per-type controllers. `ContentZoneApiController` provides a JSON API for inline zone editing.

### [Area 7: CMS Bootstrap & Application Startup](07-cms-bootstrap.md)
The composition root. `AddWebWayCms` registers all five DbContexts, services, singletons, domain models (as both interfaces and handlers), the in-house `IMapper`, and MVC application parts (including compiled Razor views). `EnsureCMS` runs four startup tasks in sequence: migrate all contexts (with retry), seed roles and admin user, seed default pages, configure the middleware pipeline.

### [Area 8: Identity & Authentication](08-identity-auth.md)
Three roles: `Admin` (full access), `Editor` (content write access on permitted types), `User` (authenticated, no admin access). `UserService` singleton provides `IsUserAdmin`/`IsUserAuthor` for view-layer role checks. Admin user is seeded from `AdminUser:Email`/`AdminUser:Password` secrets at startup. Password policy requires 12+ characters with digits, upper, lower, and non-alphanumeric characters.

### [Area 9: CMS View Components & Presentation](09-cms-presentation.md)
CMS ships pre-compiled Razor views via `CompiledRazorAssemblyPart`. Built-in ViewComponents: `PageViewComponent`, `ContentBlockViewComponent`, `ArticleViewComponent`, `LayoutViewComponent` (11 column/layout variants). Admin layout partials are in `Views/Shared/`. `IViewDiscoveryService` scans the filesystem to populate `ViewPicker` dropdowns and available controller view lists. Web project views override CMS views by path precedence.

### [Area 10: Web Application Layer](10-web-application.md)
The host project is the top of the dependency graph. It provides four extension surfaces: custom page types (`PageControllerBase<TConfig>` + `[PageController]`), custom widgets (`ViewComponent` + `[ContentZoneComponent]`), custom content types (DTO + DbContext + `AdminCrudModel<T>`), and custom mapping profiles. `ErrorController` handles both exception handler and status code page routes. Frontend assets live in `wwwroot/`; CSS is compiled from Sass.

---

## Dependency Direction Guide

Reading order for newcomers:

```
1. Data Tier            — understand DTOs, versioning, services
2. Form Generation      — understand how admin forms are declared
3. Page Routing         — understand how URLs map to controllers
4. Content Zone FW      — understand how widgets work
5. Content Domain Models — understand how model classes orchestrate the above
6. Admin CRUD FW        — understand how admin routes are handled
7. CMS Bootstrap        — understand DI wiring and startup sequence
8. Identity & Auth      — understand roles and user service
9. CMS Presentation     — understand embedded views and ViewComponents
10. Web Application     — understand how to extend the CMS in the host project
```

Dependencies only flow downward in this list. A layer only references layers beneath it.

---

## Related How-To Guides

- [docs/page-system.md](../page-system.md) — Creating a custom page type (step-by-step)
- [docs/widget-system.md](../widget-system.md) — Creating a custom widget (step-by-step)
- [docs/content-system.md](../content-system.md) — Creating a custom content type (step-by-step)
