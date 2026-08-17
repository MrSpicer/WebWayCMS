# Architecture Overview

This document maps the logical architecture of WebWayCMS. The system is a modular ASP.NET Core 10 CMS built as 10 focused class libraries (all prefixed `WebWayCMS.*`) consumed by a host web project (`MySite` is used as the example host name; [WebWayCMS.TestHost](https://github.com/MrSpicer/WebWayCMS.TestHost) is a working one in a separate repo).

The libraries are distributed as NuGet packages: a host references the single umbrella package **`WebWayCMS`**, which transitively pulls the other nine (compiled Razor views and admin CSS/JS ship inside the packages). To stand up a new site, see [getting-started](../getting-started.md).

## Library Structure

| Library | Contents |
|---|---|
| `WebWayCMS.Data` | DTOs, `CmsDbContext`, entity configurations, Services, Migrations |
| `WebWayCMS.Identity` | UserService, SmtpEmailSender, LoggingEmailSender, SmtpOptions, external-auth options |
| `WebWayCMS.Forms` | Attributes (`[FormProperty]`, `[PageController]`, `[ContentZoneComponent]`, `[CmsRoute]`), FormPropertyBuilder, FormFieldsTagHelper |
| `WebWayCMS.Routing` | CMSRouteTransformer, NotReservedConstraint, PageControllerRegistry |
| `WebWayCMS.ContentZones` | WidgetRegistry |
| `WebWayCMS.Core` | Controllers, Domain Models, ViewModels, MappingProfile, RichTextSanitizer, admin handler contracts |
| `WebWayCMS.Presentation` | Public ViewComponents, Views, Identity Areas, wwwroot |
| `WebWayCMS.Admin` | Admin controllers, admin Razor views, AdminHandlerRegistry, admin wwwroot |
| `WebWayCMS.Mcp` | MCP server: toolsets, transport wiring, API-key filter |
| `WebWayCMS` | Bootstrap: ServiceCollectionExtensions, WebWayCmsApplicationBuilderExtensions, SerilogExtensions, CspOptions/CspPolicyBuilder, AuthRateLimiting |
| `WebWayCMS.Startup` | Internal startup classes: CmsMiddlewarePipeline, CmsMigrationRunner, CmsIdentitySeeder, CmsDefaultPageSeeder, CmsWidgetRegistrationSeeder, CmsPageControllerSeeder, CmsRouteSeeder, CmsStartupHelpers, CmsDatabaseRegistration, CmsRenderingRegistration, CmsAdminRegistration, CmsIdentityRegistration, CmsHttpInfrastructureRegistration |

Project references between them (nothing else — dependencies only flow downward):

| Library | References |
|---|---|
| `WebWayCMS.Forms` | *(none)* |
| `WebWayCMS.Identity` | *(none)* |
| `WebWayCMS.Data` | *(none)* |
| `WebWayCMS.Routing` | Data, Forms |
| `WebWayCMS.ContentZones` | Data, Forms |
| `WebWayCMS.Core` | Data, Forms, Routing, ContentZones, Identity |
| `WebWayCMS.Mcp` | Core, Forms |
| `WebWayCMS.Presentation` | Core, ContentZones, Identity |
| `WebWayCMS.Admin` | Core, Presentation, Mcp |
| `WebWayCMS` | all of the above |

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
│  ServiceCollectionExtensions · WebWayCmsApplicationBuilderExtensions    │
│  (Startup/) · SerilogExtensions                                        │
│  Rendering pair · Admin pair · CSP · rate limiting · MCP mapping    │
└──┬──────────────┬──────────────┬──────────────┬─────────────────────┘
   │              │              │              │ registers / configures
   ▼              ▼              ▼              ▼
┌──────────┐ ┌───────────────┐ ┌──────────────────┐ ┌──────────────┐
│ Identity │ │ Admin CRUD    │ │ CMS Routing      │ │ Content Zone │
│ & Auth   │ │ Framework     │ │ Subsystem        │ │ Component    │
│          │ │ (WebWayCMS.   │ │                  │ │ Framework    │
│ Users    │ │  Admin)       │ │ CMSRoute         │ │ ContentZone  │
│ Roles    │ │ AdminContent  │ │ Transformer      │ │ ViewComponent│
│ UserSvc  │ │ Controller    │ │ CMSRouteService  │ │ WidgetRegis- │
│ Email    │ │ IAdminCrud    │ │ PageController   │ │ try (DB)     │
│          │ │ Handler       │ │ Base<TConfig>    │ │ [ContentZone │
│          │ │ AdminHandler  │ │ [PageController] │ │ Component]   │
│          │ │ Registry      │ │ [CmsRoute]       │ │              │
│          │ │ ContentZone   │ │ PageController   │ │              │
│          │ │ ApiController │ │ Registry (DB)    │ │              │
└──────────┘ └───────┬───────┘ └──────┬───────────┘ └──────┬───────┘
                     │                │                     │
                     │ resolves       │ extends / reads      │ renders
                     ▼                ▼                     ▼
┌─────────────────────────────────────────────────────────────────────┐
│  Content Domain Models                                              │
│  PageModel · ContentBlockModel · ArticleListModel · ArticleModel    │
│  ContentZoneModel · WidgetRegistrationModel · CMSRouteModel         │
│  PageControllerRegistrationModel                                    │
│  AdminCrudModel<T> · VersionedModel<T> · RouteRegistrationService   │
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
│  IVersionedContent (has-a ContentVersion) ← PageDTO · ArticleDTO    │
│    · CMSRouteDTO · WidgetRegistrationDTO · ...                      │
│  CmsDbContext (one context, IEntityTypeConfiguration<T> per entity) │
│  IContentStore<T> · IContentZoneService                             │
│  ICMSRouteService · IWidgetRegistrationService                      │
│  IPageControllerRegistrationService                                 │
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
A single unified EF Core `DbContext` (`CmsDbContext`) holds all CMS and Identity tables. It declares no `DbSet`s — entities are discovered through `IEntityTypeConfiguration<T>` classes and reached via `Set<T>()`. Identity (`ContentNode`) is split from version (`ContentVersion`); content types compose a `ContentVersion` via `IVersionedContent`. `IContentStore<T>` provides generic versioned CRUD; `IContentZoneService` manages zones, items, and assignment-based slot resolution with transaction-safe lazy zone creation; `ICMSRouteService` owns URL patterns.

### [Area 2: Form Generation & Configuration Metadata](02-form-generation.md)
Pure-reflection subsystem that drives all admin form rendering from C# attributes. `[FormProperty]` decorates config class properties with editor type, validation hints, and layout options. `FormPropertyBuilder` reflects these into `List<FormPropertyInfo>`. `FormFieldsTagHelper` (`<form-fields for="@Model">`) renders Bulma-styled HTML from that list — no per-type Razor form boilerplate needed.

### [Area 3: CMS Routing Subsystem](03-page-routing.md)
A `DynamicRouteValueTransformer` (`CMSRouteTransformer`) intercepts the `{**slug}` catch-all route and matches the request path against stored route patterns in the `CMSRoutes` table. Routes are owned by pages (derived from Slug), by routable widgets, or declared in code with `[CmsRoute]`. Page data and config are stored in `HttpContext.Items` for the dispatched controller. `PageControllerRegistry` is a singleton that caches page-type metadata from the database.

### [Area 4: Content Zone Component Framework](04-content-zone-framework.md)
Database-backed widget system. Zones are named slots in views; each zone holds ordered `ContentZoneItem` rows referencing a ViewComponent by name plus a JSON config blob. `ContentZoneViewComponent` resolves zones via a priority chain (direct ID → nested → page-scoped → global) and lazily creates zones in transactions on first render. `[ContentZoneComponent]`-decorated ViewComponents are reflected into `WidgetRegistrations` rows at startup; `IWidgetRegistry` serves the runtime lookup from that table.

### [Area 5: Content Domain Models](05-content-domain-models.md)
The business logic tier. `VersionedModel<T>` provides version history assembly. `AdminCrudModel<T>` extends it and implements `IAdminCrudHandler`, giving each model class dual identity: domain orchestrator and admin CRUD handler. Built-in types: `PageModel`, `ArticleListModel`/`ArticleModel` (top-level + child), `ContentBlockModel`, `ContentZoneModel`, `WidgetRegistrationModel`, `PageControllerRegistrationModel`, `CMSRouteModel`. In-house mapping profiles handle DTO-to-ViewModel mapping.

### [Area 6: Admin CRUD Framework](06-admin-crud-framework.md)
Single `AdminContentController` (in `WebWayCMS.Admin`) handles all content type admin routes by delegating to registered `IAdminCrudHandler` implementations via `AdminHandlerRegistry`. Supports top-level CRUD, child resource CRUD (via `IAdminCrudChildHandler`), version history, drag-reorder, and registry endpoints — all routed without per-type controllers. `ContentZoneApiController` provides a JSON API for inline zone editing. The MCP toolsets dispatch through the same registry.

### [Area 7: CMS Bootstrap & Application Startup](07-cms-bootstrap.md)
The composition root. `AddWebWayCmsRendering` registers the single `CmsDbContext`, all services, registries, domain models, the in-house `IMapper`, and MVC application parts; `AddWebWayCmsAdmin` layers the admin surface and MCP on top. `UseWebWayCmsRendering`/`UseWebWayCmsAdmin` migrate the database, run the seeders (default pages, widget registrations, page-type registrations, code-based routes, and — admin only — roles and the admin user), then configure the middleware pipeline and map endpoints.

### [Area 8: Identity & Authentication](08-identity-auth.md)
Three roles: `Admin` (full access), `Editor` (content write access on permitted types), `User` (authenticated, no admin access). `UserService` singleton provides `IsUserAdmin`/`IsUserAuthor` for view-layer role checks. Admin user is seeded from `AdminUser:Email`/`AdminUser:Password` secrets at startup. Password policy requires 12+ characters with digits, upper, lower, and non-alphanumeric characters, backed by account lockout, hardened auth cookies, and per-IP rate limiting on the auth endpoints.

### [Area 9: CMS View Components & Presentation](09-cms-presentation.md)
CMS ships pre-compiled Razor views via `CompiledRazorAssemblyPart`. Built-in ViewComponents: `PageViewComponent`, `ContentBlockViewComponent`, `ArticleViewComponent`, `LayoutViewComponent` (11 column/layout variants). Admin layout partials are in `Views/Shared/`. `IViewDiscoveryService` scans the filesystem to populate `ViewPicker` dropdowns and available controller view lists. Web project views override CMS views by path precedence.

### [Area 10: Web Application Layer](10-web-application.md)
The host project is the top of the dependency graph. It provides five extension surfaces: custom page types (`PageControllerBase<TConfig>` + `[PageController]`), custom widgets (`ViewComponent` + `[ContentZoneComponent]`), custom content types (DTO + entity configuration + `AdminCrudModel<T>`), code-based routes (`[CmsRoute]`), and custom mapping profiles. `ErrorController` handles both exception handler and status code page routes.

### [Area 11: Deployment Modes](11-deployment-modes.md)
The CMS boots either full-stack or rendering-only. `AddWebWayCmsAdmin`/`UseWebWayCmsAdmin` (aliased as `AddWebWayCms`/`UseWebWayCms`) register the `WebWayCMS.Admin` surface, seed roles and the admin user, and map MCP. `AddWebWayCmsRendering`/`UseWebWayCmsRendering` serve published content only. The split is a DI and pipeline boundary, not an assembly boundary.

### [Area 12: MCP Server](12-mcp-server.md)
`WebWayCMS.Mcp` exposes the admin feature set to AI agents over the Model Context Protocol. Opt-in via the `"Mcp"` config section, gated by a bearer API key that is the sole security boundary. Its toolsets dispatch generically through `IAdminHandlerRegistry`, so every content type is covered without per-type tool code.

### [Area 13: Security](13-security.md)
Cross-cutting defences: a configurable Content-Security-Policy plus fixed security headers, server-side rich-text sanitization at the single save choke point, per-IP rate limiting on the Identity auth endpoints, Identity lockout and cookie hardening, and how the CKEditor license key reaches the browser without an inline script.

### [Area 14: Host Extensibility](14-host-extensibility.md)
A package host can add its own EF-backed content type without touching CMS source. `AddWebWayCms(config, cms => …)` contributes `IEntityTypeConfiguration<T>` into `CmsDbContext`'s model and registers host mapping profiles, content stores, and a migrations-only `CmsExtensionDbContext<TSelf>` that owns the host's table while excluding every CMS/Identity table. Host migrations run after the CMS's, keyed by a separate history table.

---

## Dependency Direction Guide

Reading order for newcomers:

```
1. Data Tier            — understand DTOs, versioning, services
2. Form Generation      — understand how admin forms are declared
3. CMS Routing          — understand how URLs map to controllers
4. Content Zone FW      — understand how widgets work
5. Content Domain Models — understand how model classes orchestrate the above
6. Admin CRUD FW        — understand how admin routes are handled
7. CMS Bootstrap        — understand DI wiring and startup sequence
8. Identity & Auth      — understand roles and user service
9. CMS Presentation     — understand embedded views and ViewComponents
10. Web Application     — understand how to extend the CMS in the host project
```

Dependencies only flow downward in this list. A layer only references layers beneath it.

Areas 11–14 (Deployment Modes, MCP Server, Security, Host Extensibility) are cross-cutting rather
than layered — read them when you need them, in any order.

---

## Related How-To Guides

- [docs/page-system.md](../page-system.md) — Creating a custom page type (step-by-step)
- [docs/widget-system.md](../widget-system.md) — Creating a custom widget (step-by-step)
- [docs/content-system.md](../content-system.md) — Creating a custom content type (step-by-step)
- [docs/form-control-system.md](../form-control-system.md) — Adding a new form control (step-by-step)
