# CLAUDE.md

## Dev Server

- **URL:** `https://localhost:7046/`

## Commands

- **Build:** `dotnet build`
- **Rebuild Ef Migrations (destructive):** `./scripts/RebuildEFMigrations.sh`
- **Run all tests + coverage gate:** `./scripts/RunTests.sh` (or `dotnet test WebWayCMS.sln`)
- **Run one project's tests:** `dotnet test tests/WebWayCMS.Core.Tests/WebWayCMS.Core.Tests.csproj`
- **Run integration host end-to-end (dev secrets + docker compose; builds the libraries from source via project references, polls `http://localhost:45847`):** `./scripts/StartIntegrationHost.sh`
- **Tear down integration host (add `-v` to also drop the DB volume):** `./scripts/TearDownIntegrationhost.sh`

## Deployment Modes

- The CMS boots in one of two modes, chosen by which pair of extension methods the host calls:
  - **Full / admin:** `AddWebWayCms(config)` + `UseWebWayCms()` (back-compat aliases for
    `AddWebWayCmsAdmin` / `UseWebWayCmsAdmin`). Registers the admin CRUD surface from
    `WebWayCMS.Admin`, seeds roles + the admin user + the `/wadmin` page, and maps MCP.
  - **Rendering-only:** `AddWebWayCmsRendering(config)` + `UseWebWayCmsRendering()`. Same database,
    same public routing and content zones, but no admin controllers, no `IAdminHandlerRegistry`,
    no role/admin-user seeding, and no MCP endpoint.
- The split is a DI/pipeline boundary, not an assembly boundary: the umbrella `WebWayCMS` package
  still ships `WebWayCMS.Admin`, and the startup seeders scan its assembly in both modes.
- See [docs/architecture/11-deployment-modes.md](docs/architecture/11-deployment-modes.md).

## Host Extensibility (EF tables + migrations from a package host)

- A package host can define a content type with its own EF table entirely in its own project — no CMS
  source changes. The entry point is a builder callback on the `Add*` overloads:
  `AddWebWayCms(config, cms => …)` / `AddWebWayCmsAdmin` / `AddWebWayCmsRendering`.
- `IWebWayCmsBuilder` methods: `AddApplicationAssembly(asm)` (EF model scan + the four seeders + MVC
  parts), `AddModelConfiguration`/`ConfigureModel`, `AddContentType<T>(key)` (registers
  `IContentStore<T>`), `AddMappingProfile(Profile)`, `AddMigrationsContext<TContext>(historyTable)`.
- Host model contribution flows through `ICmsModelExtension` singletons (shipped:
  `AssemblyModelExtension`, `DelegateModelExtension`), applied by `CmsDbContext.OnModelCreating` in
  registration order. `CmsModelCacheKeyFactory` keeps distinct extension sets from sharing a stale model.
- Host migrations use `CmsExtensionDbContext<TSelf>` (a migrations-only subclass that marks every
  CMS/Identity table `ExcludeFromMigrations`); the host's `IEntityTypeConfiguration<T>` feeds both the
  runtime model and the migrations model. The host context gets its own history table and is migrated
  **after** `CmsDbContext` (so its FK to `ContentVersions` resolves). CMS migrations stay CMS-only
  (`CmsDbContextFactory` passes an empty extension list; `RebuildEFMigrations.sh` is unaffected).
- A host `AdminCrudModel<T>` registered as `IAdminCrudHandler` is served by `AdminContentController`
  and MCP-visible for free. Two handlers sharing a `ContentType` key throw at first registry resolution.
- See [docs/architecture/14-host-extensibility.md](docs/architecture/14-host-extensibility.md).

## Content Versioning (Node/Version model)

- Content identity and version are **split**. A logical item is one `ContentNode`
  (`Id`, `ContentTypeKey`, `ParentNodeId`, `SiteId`, `CreatedUtc`, `CreatedBy`, `IsDeleted/IsArchived/IsHidden`);
  its mutable data lives on `ContentVersion` rows (`NodeId`, `VersionNumber`, `Culture`, `Segment`,
  `State` (`Draft/InReview/Approved/Published/Archived`), `IsCurrentDraft`, `Title`, `Slug`,
  `PublishStartUtc/EndUtc` (scheduling seam, not yet filtered), `ChangeNote`, `ChangeSetId`, `CustomFields`).
  Every cross-entity FK points at `ContentNode.Id`. Content types implement `IVersionedContent`
  (`VersionId` + `Version`, shared PK/FK into `ContentVersion`).
- **One write engine:** `IContentStore<T>` / `ContentStore<T>` (in `WebWayCMS.Data`) replaces the old
  `ContentService<T>`/`PageService`/zone CRUD. Reads are `GetAsync(nodeId)`/`GetAllAsync()`/… (read-context
  aware) plus `GetCurrentDraftAsync`/`GetAllCurrentDraftsAsync` (admin) and `GetAllVersionsAsync` (history).
  Writes are `SaveDraftAsync(entity, expectedVersionNumber)`, `PublishAsync`, `UnpublishAsync`,
  `RestoreAsync`, `DeleteAsync`, `DeleteVersionAsync` and return `ContentWriteResult`.
- **Two invariants** (enforced in code; filtered unique indexes are the DB backstop): exactly one
  `IsCurrentDraft` per (NodeId, Culture, Segment), and at most one `Published` per variant.
  `SaveDraftAsync` mints a new version only when editing a published item; repeated edits of a draft update
  in place. `expectedVersionNumber` (hidden form field) detects concurrent edits ("changed by someone else").
- **Read context is a mode boundary:** `IContentReadContext { Mode (Published|Draft), Culture, Segment }`.
  Rendering-only hosts register a sealed `PublishedContentReadContext` (can never serve a draft); admin hosts
  register `PreviewAwareReadContext` (Draft only with a `wwcms_preview` cookie + Admin/Editor role).
  Version selection lives in one place: `ContentQueryExtensions.AtReadContext`.
- **Edits are drafts until an explicit Publish.** Routes are written by Publish (never Save) via
  `ICMSRouteService`; a draft slug change doesn't touch the route table until published.
  `CMSRouteService.UpsertAsync` keys the replace on `(OwningContentNodeId, Pattern)` and returns
  `(bool Success, string? ErrorMessage, CMSRouteDTO? Route)` — a foreign owner on the same pattern is
  a collision, never a silent steal. Publish/unpublish are
  `POST /wadmin/{contentType}/publish|unpublish/{nodeId}`. Restore is one-step (`RestoreVersionAsync`,
  DTO-level clone). A page's parent is persisted on `ContentNode.ParentNodeId` (not a route prefix);
  the publish-time slug-collision check (`PageModel.IsSlugAvailableAsync`) and the admin tree both
  derive nested URLs from it.
- **Preview renders the current draft with no route row.** `GET /wadmin/{contentType}/preview/{nodeId}`
  (gated on `IAdminCrudHandler.SupportsPreview`, `true` only for `PageModel`) sets the `wwcms_preview`
  cookie and redirects to `/_preview/{nodeId}`. `CMSRouteTransformer` special-cases that path: it
  requires an authenticated Admin/Editor, loads the draft via `IContentStore<PageDTO>.GetCurrentDraftAsync`,
  and dispatches to the page's controller — so a never-published page can preview. The cookie is what
  makes `PreviewAwareReadContext` serve drafts for the zones/widgets rendered inside the previewed page.
- Zone **items** auto-publish on write (no separate publish surface), preserving the inline editor's
  immediately-visible behaviour. Zone item CRUD/reorder goes through `IContentZoneService` (node-keyed).
- See [docs/architecture/01-data-tier.md](docs/architecture/01-data-tier.md).

## Content Seeding (JSON)

- A host (or a satellite content DLL) can ship a JSON file of serialized content that is discovered
  at startup and created/updated in the database, then published. **Admin mode only** (it dispatches
  through `IAdminHandlerRegistry`, which a rendering-only host lacks).
- Three sources: embedded resources named `*.contentseed.json` in the scanned assemblies, `*.json`
  files under the `ContentSeed:Path` directory (default `contentseed`, content-root-relative), and
  explicit files via `IWebWayCmsBuilder.AddContentSeedFile` / `AddContentSeedAssembly`.
- Each item carries a stable Guid `id` (the *seed key*), `contentType`, optional `publish` (default
  true), and a camelCase `fields` object matching the type's upsert view model (`describe_content_type`
  is the authoring reference). `nodeId`/`expectedVersionNumber` are stripped from `fields`.
- Reuses the MCP dispatch generically (`IAdminHandlerRegistry` → `CreateEmptyUpsertViewModel` /
  `GetUpsertViewModelAsync` → `ContentFieldMerger.TryMerge` → `SaveUpsertAsync` → `PublishAsync`),
  so it inherits rich-text sanitization, required-field validation, and route writing on publish.
- Update rule: `JsonContentSeeder` hashes the item and re-applies only when the hash differs from the
  `ContentSeedRecords` ledger (keyed by seed id → generated `ContentNode.Id`). Admin edits survive
  reboots; a shipped content change takes effect. A failed save doesn't record the hash, so it retries.
- A string field (or a token inside a serialized-JSON string such as `configurationJson`) may reference
  another seeded item's generated node id via `@seed:{guid}`; `SeedReferenceResolver` resolves it through
  the ledger, and `SeedAsync` runs a deferred-retry pass so ordering never matters. Unresolved references
  (including cycles) are not saved or hash-recorded and retry next boot.
- Gate it with `WEBWAYCMS_SKIP_CONTENTSEED=true` or `ContentSeed:Enabled=false`.
- See [docs/architecture/15-content-seeding.md](docs/architecture/15-content-seeding.md).

## Testing

- Test projects live under `tests/`, one per source project (NUnit + NSubstitute). Each references
  only its own source project and mocks cross-project dependencies, so it runs independently.
- Coverlet enforces **100% line + branch** coverage per project on every `dotnet test` run
  (configured in `tests/Directory.Build.props`); the build fails if a project drops below 100%.
- EF-backed data services are tested against the EF Core InMemory provider; everything else uses
  NSubstitute mocks. Coverage excludes generated EF migrations, the scaffolded ASP.NET Identity
  Razor Pages, and the database/Identity-seeding + migration orchestration in
  `WebWayCMS/Startup/` and `WebWayCMS/WebWayCmsApplicationBuilderExtensions.cs` (`[ExcludeFromCodeCoverage]`; validated by running the app).

## MCP Server

- `WebWayCMS.Mcp` exposes the admin feature set (content CRUD, child entities, version history,
  registries) to AI agents over MCP. Its tools delegate to the same `IAdminHandlerRegistry` /
  `IAdminCrudHandler` dispatch the admin UI uses, so every current and future content type is covered
  generically — there is no per-type tool code.
- Wired into the host in `WebWayCMS/ServiceCollectionExtensions.cs` (`AddWebWayCmsMcp`, called from
  `AddWebWayCmsAdmin`) and mapped in `WebWayCMS/Startup/CmsMiddlewarePipeline.cs` (`MapWebWayCmsMcp`, called from the
  admin pipeline only — a rendering-only host never maps it). Built on the official
  `ModelContextProtocol.AspNetCore` SDK.
- **Opt-in via config** (`"Mcp"` section): set `Enabled: true` and supply an `ApiKey` (user-secrets
  or environment — real deployments should never commit the key). The integration host
  (a separate repo at [WebWayCMS.TestHost](https://github.com/MrSpicer/WebWayCMS.TestHost))
  deliberately commits a throwaway localhost key for convenience. The endpoint
  is mapped at `Path` (default `/mcp`) and gated by a
  `Authorization: Bearer <ApiKey>` check — that token is the security boundary (the endpoint runs with
  effective admin authority).
- To connect Claude Code to a running instance, add to `.mcp.json` once the server is enabled:
  `{ "mcpServers": { "webwaycms": { "type": "http", "url": "https://localhost:7046/mcp",
  "headers": { "Authorization": "Bearer <key>" } } } }`.
- The SDK/transport wiring (`McpServiceCollectionExtensions`, `McpApplicationBuilderExtensions`,
  the API-key endpoint filter) is `[ExcludeFromCodeCoverage]` and validated by running; the toolset
  logic is unit-tested to the 100% gate.

## CKEditor License

- The admin rich-text editor is CKEditor 5, loaded from `https://cdn.ckeditor.com/ckeditor5/46.1.1/`
  by `WebWayCMS.Admin/Views/Shared/_AdminLayout.cshtml`. The CDN stylesheet and UMD bundle are only
  emitted when a view defines the `CKEditor` Razor section, so non-editor admin pages don't pay for it.
- **The license key is supplied by the host**, from the `"CKEditor"` config section:
  `CKEditor:LicenseKey`. There is no options class and no DI wiring — `_AdminLayout.cshtml` injects
  `IConfiguration` and emits the value into a `<meta name="ckeditor-license-key">` tag, which
  `WebWayCMS.Admin/wwwroot/js/admin.js` reads client-side (falling back to
  `window.__APP_CONFIG__.ckEditorLicenseKey`). Empty or missing ⇒ CKEditor evaluation mode.
- The meta tag exists specifically so the key never needs an inline `<script>`, which would force
  `'unsafe-inline'` into the CSP `script-src`.
- A CKEditor license key is a JWT that ships to the browser regardless, so it is not a server-side
  secret in the way the MCP `ApiKey` is.

## Security

- **Rich-text sanitization:** CKEditor HTML is sanitized server-side on save (`RichTextSanitizer` in
  `WebWayCMS.Core`, using `HtmlSanitizer`/`Ganss.Xss`). It runs generically at the top-level save choke
  point (`AdminCrudModel.SaveUpsertAsync` → `SaveUpsertCoreAsync`) and at the article child-entity save
  path (`ArticleModel.SaveUpsertAsync`), covering both the admin UI and the MCP tools, on every `string`
  property marked `[FormProperty(EditorType = EditorType.RichText)]`.
  Stored content is therefore safe to render with `@Html.Raw`. **Required-field validation**
  (`ModelValidator`, also `WebWayCMS.Core/Security/`) joins sanitization at
  `AdminCrudModel.SaveUpsertAsync` (plus the CMS-route, article-child, and zone-item save paths), so a
  field `describe_content_type` advertises as `required` is actually enforced on MCP writes.
- **Form attribute encoding:** all HTML input attributes are built by `FormAttributeBuilder`
  (`WebWayCMS.Forms/Forms/FormAttributeBuilder.cs`), which encodes every value exactly once via
  `HtmlEncoder.Default` across 16 view components. Views emit the result with `@Html.Raw`, so
  double-encoding bugs cannot creep in through per-view string concatenation.
- **Content-Security-Policy** is emitted by the middleware in `WebWayCMS/Startup/CmsMiddlewarePipeline.cs` and is
  **host-configurable via the `"Csp"` config section** (`CspOptions`): `Enabled` (default true),
  `ReportOnly` (default false), and a `Directives` map. The CMS ships secure defaults that keep the
  admin UI working (CKEditor/Bulma/FontAwesome CDNs); a host overrides or adds individual directives,
  and directives it does not mention keep the CMS default (set a directive to empty to drop it). The
  default `script-src` allows no `'unsafe-inline'`, so keep admin scripts in files, not inline
  `<script>` blocks. The policy string is built by the unit-tested `CspPolicyBuilder`.
- **Auth rate limiting:** the Identity login/register/password-reset/external-login/passkey endpoints are
  throttled per client IP **and per endpoint family** (`AuthRateLimiting`, wired via
  `AddRateLimiter`/`UseRateLimiter`); returns HTTP 429 over the limit.
- **Identity hardening:** explicit account lockout (5 attempts / 15 min) and auth-cookie flags
  (`HttpOnly`, `Secure=Always`, `SameSite=Lax`) are set in `ConfigureAuthorization`. `Lax` (not
  `Strict`) is required so OAuth sign-in redirect chains are not treated cross-site; it still withholds
  the cookie on cross-site POSTs, which is what keeps the passkey minimal-API endpoints safe without
  antiforgery middleware.

## Code Conventions

- File-scoped namespaces, nullable reference types enabled
- Private fields: `_camelCase`; async methods: suffix `Async`
- ViewModels: `{Name}ViewModel.cs`; DTOs: `{Name}DTO.cs` (in `Data/Models/`)
- Constructor injection with `?? throw new ArgumentNullException(nameof(...))`
- Fallible operations return `(bool Success, string? ErrorMessage)` tuples
- Async methods include `CancellationToken ct = default`
- Logging: `Serilog.Log.ForContext<ClassName>()`
- Controller routing: attribute-based with `[Authorize]`, `[ValidateAntiForgeryToken]`
- Test naming: `MethodName_Scenario_ExpectedBehavior`, NUnit constraint model (`Assert.That(...)`)
- Import order: System > Microsoft > Third-party > Project
- Configuration form fields use `[FormProperty]` attribute with `EditorType` enum

## rules
 - after finishing work check to see if documentation needs to be updated to reflect the changes
 - Do not Remove todo notes from the code unless the todo has been completed. If you are unsure. ask
 - If Tests fail that were previously passing, do not modify those tests without permission from a human
 - When multiple good options exist ask the user which they would prefer
 - always ask clarifying questions when planning if you have any uncertainty.
 - Do not use JQuery.
 - Get confirmation from a human before using any external library or code.
 - Do not commit work
 - The very last thing you should do before existing work is reread the plan and ensure that all steps have been completed and all verification prescribed by the plan was actually done.
 - ALWAYS call cognitive_store after discovering anything worth remembering across sessions. Do not wait for permission.
