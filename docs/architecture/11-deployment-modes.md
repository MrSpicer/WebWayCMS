# Area 11: Deployment Modes

**Namespaces:**
- `WebWayCMS` — `ServiceCollectionExtensions`, `WebWayCmsApplicationBuilderExtensions`
- `WebWayCMS.Admin` — admin controllers, admin Razor views, `AdminHandlerRegistry`, admin `wwwroot`

**Depends on:** every other area (this is a bootstrap concern)
**Consumed by:** Web project `Program.cs`

---

## 1. Two Modes, One Codebase

A host chooses how much of the CMS to switch on by picking a pair of extension methods:

| Mode | DI | Startup | What it serves |
|---|---|---|---|
| **Full / admin** | `AddWebWayCmsAdmin(config)` | `UseWebWayCmsAdmin()` | Public site **and** `/wadmin`, plus MCP |
| **Rendering-only** | `AddWebWayCmsRendering(config)` | `UseWebWayCmsRendering()` | Public site only |
| *(alias)* | `AddWebWayCms(config)` | `UseWebWayCms()` | Delegates to the admin pair |

The typical reason to run rendering-only is to put a public front-end in front of the same database
while the editing instance lives somewhere less exposed — no admin routes, no admin assets, and no
MCP endpoint on the public host.

Both modes talk to the same `CmsDbContext` and the same tables. There is no separate "published"
store; rendering-only hosts simply have no write surface.

### The read context is a mode boundary (security, not convenience)

Version selection is driven by `IContentReadContext`, registered **per mode**:

- `AddWebWayCmsRendering` registers a sealed `PublishedContentReadContext` that hard-codes
  `ContentReadMode.Published`. A rendering-only host is therefore **physically incapable of serving a
  draft** — there is no preview cookie and no code path to a draft read.
- `AddWebWayCmsAdmin` replaces it with `PreviewAwareReadContext`, which serves `Draft` only when the
  request carries a valid `wwcms_preview` cookie **and** the user is authenticated as `Admin`/`Editor`.

This is the same DI boundary described in §5: the rendering path resolves a context that can only ever
return published rows.

### Cache staleness on split deployments

Publishing is now an explicit action users expect to take effect immediately, but the route registry
and widget caches are per-process (60s / 5min TTLs) with per-process invalidation. On a split
admin + rendering-only deployment, a publish will **not** appear on the rendering instance until its
TTL expires. Not solved here — plan around it if you run a split topology.

---

## 2. What Each Mode Registers

`AddWebWayCmsAdmin` calls `AddWebWayCmsRendering` first, so the admin mode is strictly additive.

**`AddWebWayCmsRendering`** — the database, forwarded headers, all content services and registries,
all domain models under their **domain interfaces**, the mapper, Identity + cookie hardening, rate
limiting, `CspOptions`, and the `WebWayCMS.Core` / `.Forms` / `.Presentation` application parts.
Full catalog in [07-cms-bootstrap](07-cms-bootstrap.md#2-addwebwaycmsrendering--di-registration-catalog).

**`AddWebWayCmsAdmin` adds:**
- the `"notreserved"` route constraint
- `IAdminHandlerRegistry` → `AdminHandlerRegistry`
- seven `IAdminCrudHandler` registrations forwarding the already-registered scoped models
- the `WebWayCMS.Admin` assembly as an `AssemblyPart` **and** a `CompiledRazorAssemblyPart`
- `AddWebWayCmsMcp(configuration)`

So in rendering-only mode the domain models still exist and still serve view components — they are
simply never exposed as admin handlers, and no controller can reach them.

---

## 3. What Each Mode Does at Startup

```
UseWebWayCmsRendering                  UseWebWayCmsAdmin
─────────────────────                  ─────────────────────
ApplyCmsPendingMigrations              ApplyCmsPendingMigrations
                                       EnsureCmsRolesAndAdminSeeded
EnsureDefaultHomePage                  EnsureDefaultHomePage
EnsureWidgetRegistrationsSeeded        EnsureWidgetRegistrationsSeeded
EnsurePageControllerRegistrationsSeeded EnsurePageControllerRegistrationsSeeded
EnsureCodeBasedRoutesSeeded            EnsureCodeBasedRoutesSeeded
ConfigureRenderingPipeline             ConfigureAdminPipeline
```

The only differences: admin mode seeds roles and the admin user, and its pipeline calls
`MapWebWayCmsMcp()` before mapping endpoints. Both pipelines run the identical
`ConfigureSharedMiddleware` and `MapCmsEndpoints`.

Note that **both modes run all three registration seeders**, and both apply migrations. A
rendering-only instance is therefore not read-only against the database — it will migrate the
schema and insert registration rows on startup. If that is not what you want (for example, several
rendering replicas behind a load balancer), set the skip variables:

```
WEBWAYCMS_SKIP_MIGRATIONS=true
WEBWAYCMS_SKIP_DEFAULTPAGE=true
WEBWAYCMS_SKIP_DEFAULTWIDGETS=true
WEBWAYCMS_SKIP_DEFAULTPAGECONTROLLERS=true
WEBWAYCMS_SKIP_CODEBASEDROUTES=true
```

---

## 4. What Lives in `WebWayCMS.Admin`

`WebWayCMS.Admin` is a `Microsoft.NET.Sdk.Razor` class library (`AddRazorSupportForMvc`). It
contains MVC views only — no Razor Pages; the Identity pages stayed in `WebWayCMS.Presentation`.

**Controllers**
| Type | Route |
|---|---|
| `AdminContentController` | `[Route("wadmin")]` — the generic CRUD dispatcher for every content type |
| `AdminContentZoneController` | `[Route("wadmin/contentzones")]` — the inline zone editor |
| `ContentZoneApiController` | `[Route("api/contentzones")]` — JSON API for inline zone editing |
| `GenericAdminPageController` | `[PageController]` page type behind `[Authorize(Roles = "Admin")]` |

**Handlers** — `AdminHandlerRegistry` (the implementation only; the interfaces and
`AdminCrudModel<T>` stay in `WebWayCMS.Core` so the domain models don't depend on this assembly).

**Views** — `AdminArticle/`, `AdminCMSRoute/`, `AdminContentBlock/`, `AdminContentZone/`,
`AdminPage/`, `AdminShared/`, `GenericAdminPage/` (including `Dashboard.cshtml`),
`PageControllerRegistration/`, `WidgetRegistration/`, and `Shared/` (`_AdminLayout`,
`_AdminNavbar`, `_DeleteConfirmModal`).

**Assets** — `wwwroot/css/{admin,bulma.min,content-zone-edit}.css`,
`wwwroot/js/{admin,content-zone-edit,page-upsert}.js`, served as
`~/_content/WebWayCMS.Admin/...`.

---

## 5. The Split Is a DI Boundary, Not an Assembly Boundary

This is the important caveat. Rendering-only mode does not *remove* the admin code from the
deployment:

- `WebWayCMS.csproj` has a `<ProjectReference>` to `WebWayCMS.Admin` (and to `WebWayCMS.Mcp`), so
  the umbrella package always brings both along.
- `EnsurePageControllerRegistrationsSeeded` and `EnsureCodeBasedRoutesSeeded` reference
  `typeof(AdminContentController).Assembly` to decide what to scan — and both run in the rendering
  path. The admin assembly is loaded either way.

What rendering-only actually guarantees is that **no admin route is mapped and no admin handler is
resolvable**: `MapAdminTypes` never runs, so the `WebWayCMS.Admin` application part is never added
and `AdminContentController`'s attribute routes are never discovered by `MapControllers()`.

Treat it as defence in depth for a public host, not as a way to ship a smaller artifact.

In a rendering-only host, `EnsureDefaultHomePage(seedAdminPage: false)` suppresses the `/wadmin`
page seed — only the Home page is created. `DefaultContentSeeder.SeedDefaultPagesAsync` checks
the flag and skips the admin page when it is `false`.

---

## 6. Choosing a Mode

Use **full/admin** unless you have a specific reason not to — it is the default and the alias
`AddWebWayCms`/`UseWebWayCms` points at it.

Use **rendering-only** when the host is internet-facing and editing happens elsewhere. Pair it with
the skip environment variables from §3 so the public replicas do not race each other to migrate and
seed.

```csharp
// Public front-end
builder.Services.AddWebWayCmsRendering(builder.Configuration);
// ...
app.UseWebWayCmsRendering();
```

---

*See also:* [07-cms-bootstrap](07-cms-bootstrap.md) for the full registration catalog and pipeline
order, [06-admin-crud-framework](06-admin-crud-framework.md) for what the admin surface does, and
[12-mcp-server](12-mcp-server.md) for the MCP endpoint that only admin mode maps.
