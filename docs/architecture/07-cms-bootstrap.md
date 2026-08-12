# Area 7: CMS Bootstrap & Application Startup

**Namespaces:**
- `WebWayCMS` — `ServiceCollectionExtensions`, `WebWayCmsApplicationBuilderExtensions`, `CspOptions`, `CspPolicyBuilder`, `AuthRateLimiting`
- `WebWayCMS.Startup` — `CmsMiddlewarePipeline`, `CmsMigrationRunner`, `CmsIdentitySeeder`, `CmsDefaultPageSeeder`, `CmsWidgetRegistrationSeeder`, `CmsPageControllerSeeder`, `CmsRouteSeeder`, `CmsStartupHelpers`, `CmsDatabaseRegistration`, `CmsRenderingRegistration`, `CmsAdminRegistration`, `CmsIdentityRegistration`, `CmsHttpInfrastructureRegistration`
- `WebWayCMS.Logging` — `SerilogExtensions`

**Depends on:** All 9 other CMS libraries (composition root)
**Consumed by:** Web project `Program.cs` exclusively

---

## 1. Entry Points

The CMS exposes two bootstrap pairs plus back-compat aliases. A host calls one `Add*` in
`Program.cs` before `builder.Build()`, and the matching `Use*` after it.

| DI method | Pipeline method | Mode |
|---|---|---|
| `AddWebWayCmsRendering(services, config)` | `UseWebWayCmsRendering(app)` | Render published content only |
| `AddWebWayCmsAdmin(services, config)` | `UseWebWayCmsAdmin(app)` | Full stack: rendering + admin UI + MCP |
| `AddWebWayCms(services, config)` | `UseWebWayCms(app)` | Aliases that delegate to the admin pair |

There is also a parameterless `AddWebWayCms(services)` overload used by tests: it registers the core
types, authorization, rate limiting, and the admin types, but **skips the database, `CspOptions`
binding, and MCP** — it is not a host entry point.

See [11-deployment-modes](11-deployment-modes.md) for what the split does and does not buy you.

---

## 2. `AddWebWayCmsRendering` — DI Registration Catalog

Runs in this order:

**Database** (`ConfigureDatabaseServices`):
- Reads `ConnectionStrings:DefaultConnection`; throws `InvalidOperationException` if absent
- `AddDbContext<CmsDbContext>` over Npgsql, with `MigrationsHistoryTable("__EFMigrationsHistory")`
- `AddDatabaseDeveloperPageExceptionFilter()` in `DEBUG` builds only

**Forwarded headers** (`ConfigureForwardedHeaders`):
- Trusts `X-Forwarded-For` and `X-Forwarded-Proto`, clearing known networks/proxies for Docker
  internal networking

**Core types** (`AddRenderingCoreTypes`):

| Registration | Lifetime |
|---|---|
| `IEmailSender` → `DevEmailSender` (`DEBUG` only) | singleton |
| `IHttpContextAccessor` | — |
| `UserService` | singleton |
| `IViewDiscoveryService` → `ViewDiscoveryService` | scoped |
| `IWidgetRegistry` → `WidgetRegistry` | **singleton** |
| `IPageControllerRegistry` → `PageControllerRegistry` | **singleton** |
| `IContentService<T>` for `ArticleDTO`, `ArticleListDTO`, `ContentBlockDTO`, `WidgetRegistrationDTO`, `PageControllerRegistrationDTO`, `CMSRouteDTO` | scoped |
| `IContentZoneService` → `ContentZoneService` | scoped |
| `IPageService` → `PageService` | scoped |
| `IWidgetRegistrationService` → `WidgetRegistrationService` | scoped |
| `IPageControllerRegistrationService` → `PageControllerRegistrationService` | scoped |
| `ICMSRouteService` → `CMSRouteService` | scoped |
| `IRouteRegistrationService` → `RouteRegistrationService` | scoped |
| `IDefaultContentSeeder` → `DefaultContentSeeder` | scoped |
| `CMSRouteTransformer` | scoped (it injects scoped services) |
| `ContentBlockModel` / `IContentBlockModel` | scoped |
| `PageModel` / `IPageModel` | scoped |
| `ArticleListModel` / `IArticleListModel` | scoped |
| `ContentZoneModel` / `IContentZoneModel` | scoped |
| `WidgetRegistrationModel`, `PageControllerRegistrationModel`, `CMSRouteModel` | scoped |
| `ArticleViewComponent` / `IRoutableViewComponent` | scoped |
| `IArticleModel` → `ArticleModel` | scoped |
| `IMapper` from `new MapperConfiguration(cfg => cfg.AddProfile(new MappingProfile()))` | singleton |

Note the models are registered here, in the *rendering* path, under their domain interfaces only.
They become admin handlers in `MapAdminTypes` (§3).

**MVC application parts** added by `AddRenderingCoreTypes`:
- `AssemblyPart(WebWayCMS.Core)` — controllers and ViewComponents
- `AssemblyPart(WebWayCMS.Forms)` — tag helpers (`FormFieldsTagHelper`)
- `AssemblyPart(WebWayCMS.Presentation)` + `CompiledRazorAssemblyPart(WebWayCMS.Presentation)`

**Identity** (`ConfigureAuthorization`) — see [Area 8](08-identity-auth.md) for the policy values.

**Rate limiting** (`ConfigureRateLimiting`):
- `AddRateLimiter` with `RejectionStatusCode = 429` and a global partitioned limiter driven by
  `AuthRateLimiting.GetPartition`

**Options binding:**
- `services.Configure<CspOptions>(configuration.GetSection("Csp"))`

---

## 3. What `AddWebWayCmsAdmin` Adds

It calls `AddWebWayCmsRendering` first, then:

**`MapAdminTypes`:**
- Route constraint `"notreserved"` → `NotReservedConstraint`
- `IAdminHandlerRegistry` → `AdminHandlerRegistry` (scoped)
- Seven `IAdminCrudHandler` forwards, each resolving the already-registered scoped model:
  `ContentBlockModel`, `PageModel`, `ArticleListModel`, `ContentZoneModel`,
  `WidgetRegistrationModel`, `PageControllerRegistrationModel`, `CMSRouteModel`
- `AssemblyPart(WebWayCMS.Admin)` + `CompiledRazorAssemblyPart(WebWayCMS.Admin)`

**MCP:**
- `services.AddWebWayCmsMcp(configuration)` — binds `McpOptions` and registers the toolsets.
  See [Area 12](12-mcp-server.md).

---

## 4. `UseWebWayCms*` — Startup Task Sequence

```
UseWebWayCmsRendering                 UseWebWayCmsAdmin
─────────────────────                 ─────────────────────
1. ApplyCmsPendingMigrations          1. ApplyCmsPendingMigrations
                                      2. EnsureCmsRolesAndAdminSeeded
2. EnsureDefaultHomePage              3. EnsureDefaultHomePage
3. EnsureWidgetRegistrationsSeeded    4. EnsureWidgetRegistrationsSeeded
4. EnsurePageControllerRegistrations   5. EnsurePageControllerRegistrations
     Seeded                                Seeded
5. EnsureCodeBasedRoutesSeeded        6. EnsureCodeBasedRoutesSeeded
6. ConfigureRenderingPipeline         7. ConfigureAdminPipeline
```

`UseWebWayCms(app)` simply calls `UseWebWayCmsAdmin(app)`.

Every step is idempotent — calling `UseWebWayCms*` on a fully-initialized database is safe and fast. The
three registration seeders **only insert**: a widget, page type, or route pattern that already
exists is skipped and never updated, so after the first run the database is authoritative.

Each method takes `bool throwOnError = true`. Migrations honour it directly; the seeders log and
continue by default.

> `EnsureDefaultHomePage` accepts a `seedAdminPage` flag. The rendering path passes `false` and
> the admin path passes `true`. `DefaultContentSeeder.SeedDefaultPagesAsync` honours the flag —
> when `false`, only the Home page is seeded; when `true`, the Admin page (at `/wadmin` with
> `GenericAdminPageController`) is also seeded with its own independent guard.

---

## 5. Migration Retry Logic

`ApplyCmsPendingMigrations` applies pending migrations for the unified context. It retries up to 10
times with exponential backoff (starting at 3s, capping at 30s) when a `SocketException` is detected
in the exception chain — the signal that the database container is not yet available.

Migrations are applied in a single pass; the unified context owns all tables and does not require
ordering.

---

## 6. Environment Variable Overrides

| Variable | Effect |
|----------|--------|
| `WEBWAYCMS_SKIP_MIGRATIONS=true` | Skip migration application entirely (read-only replicas, integration tests) |
| `WEBWAYCMS_SKIP_ROLESEED=true` | Skip role creation and admin user seeding |
| `WEBWAYCMS_SKIP_DEFAULTPAGE=true` | Skip default Home/Admin page seeding |
| `WEBWAYCMS_SKIP_DEFAULTWIDGETS=true` | Skip seeding widget registrations from `[ContentZoneComponent]` |
| `WEBWAYCMS_SKIP_DEFAULTPAGECONTROLLERS=true` | Skip seeding page-type registrations from `[PageController]` |
| `WEBWAYCMS_SKIP_CODEBASEDROUTES=true` | Skip seeding routes from `[CmsRoute]` |

All comparisons are case-insensitive. These variables are read at startup, not cached.

A seventh variable applies to the EF design-time tooling rather than the running app:
`WEBWAYCMS_DESIGNTIME_CONNECTION` overrides the connection string `CmsDbContextFactory` uses when
scaffolding migrations.

### Assemblies each seeder scans

| Seeder | Assemblies |
|---|---|
| Widget registrations | `WebWayCMS.Presentation`, entry assembly |
| Page-controller registrations | `WebWayCMS.Core`, `WebWayCMS.Admin`, entry assembly |
| Code-based routes | `WebWayCMS.Core`, `WebWayCMS.Admin`, `WebWayCMS.Presentation`, entry assembly |

---

## 7. Serilog Configuration

`builder.Host.UseCmsSerilog(configuration)` configures Serilog via `SerilogExtensions.UseCmsSerilog`:

```csharp
loggerConfig
    .ReadFrom.Configuration(context.Configuration)  // Serilog overrides from appsettings.json
    .ReadFrom.Services(services)                     // Enrichers from DI
    .Enrich.FromLogContext();

// Defaults (overridable via config)
loggerConfig.MinimumLevel.Override("Microsoft", LogEventLevel.Information);
loggerConfig.WriteTo.Console();

// File sink only outside containers
if (!runningInContainer)
    loggerConfig.WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day);
```

Container detection: `DOTNET_RUNNING_IN_CONTAINER == "true"` (set automatically by the .NET Docker
base image). In containers, stdout logging is preferred and file sinks are skipped.

Configuration overrides take precedence — add `Serilog:` keys to `appsettings.json` to change
minimum levels, add sinks, etc.

---

## 8. Middleware Pipeline Order

Both pipelines share `ConfigureSharedMiddleware`:

```
UseForwardedHeaders()          — must be first; rewrites Request.Scheme/IP from proxy headers
UseHsts()                      — adds Strict-Transport-Security header
UseHttpsRedirection()          — redirect HTTP → HTTPS
                               — custom security headers middleware:
                                   X-Content-Type-Options: nosniff
                                   X-Frame-Options: DENY
                                   Referrer-Policy: strict-origin-when-cross-origin
                                   Permissions-Policy: geolocation=(), microphone=(), camera=()
                                   Content-Security-Policy (see below)
UseStaticFiles()               — serve wwwroot and _content assets
UseRouting()                   — match routes
UseRateLimiter()               — per-IP throttling on the Identity auth endpoints
UseAuthentication()
UseAuthorization()
```

The CSP header name and value are computed **once at startup** from `IOptions<CspOptions>` via
`CspPolicyBuilder.HeaderName` / `CspPolicyBuilder.Build`, then written on every response. An empty
built value (i.e. `Csp:Enabled = false`) means no header is emitted. See [Area 13](13-security.md).

Then `ConfigureAdminPipeline` calls `app.MapWebWayCmsMcp()` — the rendering pipeline does not —
and both call `MapCmsEndpoints`:

```
MapRazorPages()                — Identity UI pages
MapControllers()               — attribute-routed controllers (incl. all /wadmin routes)
MapDynamicControllerRoute<CMSRouteTransformer>("{**slug}")  — database-backed CMS routing
MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}")  — fallback MVC
```

Order matters: attribute-routed controllers are mapped **before** the dynamic catch-all, so
`AdminContentController`'s `wadmin/{contentType}` out-ranks it. If `CMSRouteTransformer` returns
`null!`, routing falls through to the conventional route and then to a 404. Keeping both
registrations inside the CMS makes the package self-contained — the Web project only calls
`UseWebWayCms()`.

---

## 9. Minimal `Program.cs` Template

```csharp
var builder = WebApplication.CreateBuilder(args);

// Web-project-specific service registrations go here, before AddWebWayCms:
// services.AddScoped<MyService>();
MapTypes(builder.Services);

builder.Services.AddWebWayCms(builder.Configuration);  // CMS DI (admin + rendering)

builder.Host.UseCmsSerilog(builder.Configuration);     // Serilog

var mvc = builder.Services.AddControllersWithViews();
if (builder.Environment.IsDevelopment())
    mvc.AddRazorRuntimeCompilation();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseStatusCodePagesWithReExecute("/Error/{0}");
}

app.UseWebWayCms();  // Migrations, seeding, middleware, route mapping

// Web-project-specific route mappings (optional) go here. The CMS dynamic route
// and the conventional fallback route are already registered inside UseWebWayCms().

app.Run();
```

Do not call `AddControllersWithViews` before `AddWebWayCms` — the CMS extension also calls it and
merges the application parts.

A working version of this file is in the
[WebWayCMS.TestHost](https://github.com/MrSpicer/WebWayCMS.TestHost) repo (`Program.cs`).
