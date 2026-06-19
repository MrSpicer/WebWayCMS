# Area 7: CMS Bootstrap & Application Startup

**Namespaces:**
- `WebWayCMS` — `ServiceCollectionExtensions`, `CMSExtensions`
- `WebWayCMS.Logging` — `SerilogExtensions`

**Depends on:** All 7 other CMS libraries (composition root)
**Consumed by:** Web project `Program.cs` exclusively

---

## 1. `AddWebWayCms(services, configuration)` — DI Registration Catalog

Called once in `Program.cs`. Performs the following registrations in order:

**Forwarded headers:**
- Configures `ForwardedHeadersOptions` to trust `X-Forwarded-For` and `X-Forwarded-Proto` from all upstream proxies (cleared known networks/proxies for Docker internal networking)

**DbContexts** (5 contexts, all pointing to `DefaultConnection`):

| Context | Migration Table |
|---------|----------------|
| `ApplicationDbContext` | `__EFMigrationsHistory_Application` |
| `ArticleContext` | `__EFMigrationsHistory_Article` |
| `ContentBlockContext` | `__EFMigrationsHistory_ContentBlock` |
| `ContentZoneContext` | `__EFMigrationsHistory_ContentZone` |
| `PageContext` | `__EFMigrationsHistory_Page` |

Database developer page exception filter is added in `DEBUG` builds.

**Utility services:**
- `IHttpContextAccessor` — needed by `UserService`
- `UserService` — singleton
- `IViewDiscoveryService` → `ViewDiscoveryService` — scoped

**Registries (singletons):**
- `IContentZoneComponentRegistry` → `ContentZoneComponentRegistry` — scans `CMS.Presentation` assembly + entry assembly
- `IPageControllerRegistry` → `PageControllerRegistry` — scans `CMS.Core` assembly + entry assembly
- `PageRouteTransformer` — scoped (because it injects `IPageService`)
- Route constraint: `"notreserved"` → `NotReservedConstraint`

**Content services (scoped, bound to correct DbContexts):**
- `IContentService<ArticleDTO>` → `ContentService<ArticleDTO>` (uses `ArticleContext`)
- `IContentService<ArticleListDTO>` → `ContentService<ArticleListDTO>` (uses `ArticleContext`)
- `IContentService<ContentBlockDTO>` → `ContentService<ContentBlockDTO>` (uses `ContentBlockContext`)
- `IContentZoneService` → `ContentZoneService`
- `IPageService` → `PageService`

**Domain models (scoped, registered as both domain interface and `IAdminCrudHandler`):**
- `ContentBlockModel` / `IContentBlockModel` / `IAdminCrudHandler`
- `PageModel` / `IPageModel` / `IAdminCrudHandler`
- `ArticleListModel` / `IArticleListModel` / `IAdminCrudHandler`
- `ContentZoneModel` / `IContentZoneModel` / `IAdminCrudHandler`
- `IArticleModel` → `ArticleModel`

**Admin framework:**
- `IAdminHandlerRegistry` → `AdminHandlerRegistry` — scoped (built from all `IAdminCrudHandler` per request)

**Dev email sender** (DEBUG builds only):
- `IEmailSender` → `DevEmailSender`

**Object mapper (in-house `IMapper`):**
- Adds `MappingProfile` from the `CMS.Core` assembly

**MVC application parts (3 assemblies registered):**
- `AssemblyPart(CMS.Core)` — registers controllers and ViewComponents
- `AssemblyPart(CMS.Forms)` — registers tag helpers (`FormFieldsTagHelper`)
- `AssemblyPart(CMS.Presentation)` + `CompiledRazorAssemblyPart(CMS.Presentation)` — registers ViewComponents and exposes pre-compiled Razor views

**Identity:**
- `AddDefaultIdentity<IdentityUser>` with password policy (see [Area 8](08-identity-auth.md))
- `.AddRoles<IdentityRole>()`
- `.AddEntityFrameworkStores<ApplicationDbContext>()`
- `.AddDefaultUI()` — embeds Identity Razor Pages

---

## 2. `EnsureCMS(app)` — Startup Task Sequence

Called once in `Program.cs` after `builder.Build()`. Executes four tasks in order:

```
1. ApplyCmsPendingMigrations()
2. EnsureCmsRolesAndAdminSeeded()
3. EnsureDefaultHomePage()
4. ConfigureMiddleware()
```

Each step is idempotent — calling `EnsureCMS` on a fully-initialized database is safe and fast.

---

## 3. Migration Retry Logic

`ApplyCmsPendingMigrations` applies pending migrations for all five contexts. It retries up to 10 times with exponential backoff (starting at 3s, capping at 30s) when a `SocketException` is detected in the exception chain — the signal that the database container is not yet available.

Retry is limited to transient network errors (`SocketException`); other exceptions terminate startup immediately unless `throwOnError = false`.

Migrations are applied in a deterministic order: Application → Article → ContentBlock → ContentZone → Page.

---

## 4. Environment Variable Overrides

| Variable | Effect |
|----------|--------|
| `WEBWAYCMS_SKIP_MIGRATIONS=true` | Skip migration application entirely (for read-only replicas or integration tests) |
| `WEBWAYCMS_SKIP_ROLESEED=true` | Skip role creation and admin user seeding |
| `WEBWAYCMS_SKIP_DEFAULTPAGE=true` | Skip default home page seeding |

All comparisons are case-insensitive. These variables are checked at startup, not cached.

---

## 5. Serilog Configuration

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

Container detection: `DOTNET_RUNNING_IN_CONTAINER == "true"` (set automatically by the .NET Docker base image). In containers, stdout logging is preferred and file sinks are skipped.

Configuration overrides take precedence — add `Serilog:` keys to `appsettings.json` to change minimum levels, add sinks, etc.

---

## 6. Middleware Pipeline Order

`ConfigureMiddleware` configures the ASP.NET Core pipeline in this order:

```
UseForwardedHeaders()          — must be first; rewrites Request.Scheme/IP from proxy headers
UseHsts()                      — adds Strict-Transport-Security header
UseHttpsRedirection()          — redirect HTTP → HTTPS
                               — custom security headers middleware:
                                   X-Content-Type-Options: nosniff
                                   X-Frame-Options: DENY
                                   Referrer-Policy: strict-origin-when-cross-origin
                                   Permissions-Policy: geolocation=(), microphone=(), camera=()
UseStaticFiles()               — serve wwwroot assets
UseRouting()                   — match routes
UseAuthentication()
UseAuthorization()
MapRazorPages()                — Identity UI pages
MapDynamicControllerRoute<PageRouteTransformer>("{**slug}")  — dynamic page routing
MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}")  — fallback MVC
```

Route mapping is performed by `ConfigureMiddleware` (the final `EnsureCMS()` step), so the host
project does not register CMS routes itself. The dynamic catch-all `{**slug}` is mapped before the
conventional route; if `PageRouteTransformer` returns `null!`, routing falls through to the
conventional `{controller}/{action}/{id?}` route. Keeping both registrations inside the CMS makes
the package self-contained — the Web project only calls `EnsureCMS()`.

---

## 7. Minimal `Program.cs` Template

Required call sequence with placeholder comments for Web-project additions:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Web-project-specific service registrations:
// services.AddScoped<MyService>();
// services.AddSingleton<IMapper>(new MapperConfiguration(cfg => cfg.AddProfile(new MappingProfile())).CreateMapper());
MapTypes(builder.Services);  // Web project's local DI registrations

builder.Services.AddWebWayCms(builder.Configuration);  // CMS DI

builder.Host.UseCmsSerilog(builder.Configuration);  // Serilog

var mvc = builder.Services.AddControllersWithViews();
if (builder.Environment.IsDevelopment())
    mvc.AddRazorRuntimeCompilation();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseStatusCodePagesWithReExecute("/Error/{0}");
}

app.EnsureCMS();  // Migrations, seeding, middleware, route mapping

// Web-project-specific route mappings (optional) go here. The CMS dynamic page route
// and the conventional fallback route are already registered inside EnsureCMS().

app.Run();
```

Do not call `AddControllersWithViews` before `AddWebWayCms` — the CMS extension also calls it and merges the application parts.
