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
- A host content type is an `EntityPicker` target via `EntityType = "<contentTypeKey>"`: the list
  endpoint is resolved server-side (`EntityPickerEndpoint.Resolve` over `IAdminHandlerRegistry` —
  exact key, plural, then secondary list key) and emitted as `data-endpoint`; `form-components.js`
  has no endpoint map.
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
  `RestoreAsync`, `DeleteAsync`, `DeleteVersionAsync`, `DiscardDraftAsync` and return `ContentWriteResult`.
  `DiscardDraftAsync` throws away an unpublished current draft: the published version (else the newest
  earlier one) becomes current again; a node whose only version is the draft is hard-deleted. The
  discard's delete + fallback-promotion run in one DB transaction, and a conflicting write maps to the
  shared `StaleVersionMessage` like every other write path.
- **Two invariants** (enforced in code; filtered unique indexes are the DB backstop): exactly one
  `IsCurrentDraft` per (NodeId, Culture, Segment), and at most one `Published` per variant.
  `SaveDraftAsync` mints a new version only when editing a published item; repeated edits of a draft update
  in place. `expectedVersionNumber` (hidden form field) detects concurrent edits ("changed by someone else").
- **Read context is a mode boundary:** `IContentReadContext { Mode (Published|Draft), Culture, Segment }`.
  Rendering-only hosts register a sealed `PublishedContentReadContext` (can never serve a draft); admin hosts
  register `PreviewAwareReadContext` (Draft only for an Admin/Editor whose request is marked with
  `HttpContext.Items[PreviewConstants.DraftRequestItemKey] = true` — set by `CMSRouteTransformer` for a
  resolved `/_preview/{nodeId}` and by the page zone editor action). Draft reads are **per request**; there is
  no preview cookie, so previewing a page never puts the live site into draft mode.
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
  (gated on `IAdminCrudHandler.SupportsPreview`, `true` only for `PageModel`) only redirects to
  `/_preview/{nodeId}` — Admins only, since the controller requires the Admin role; an **Editor**
  previews via the `/_preview/{nodeId}` URL directly. `CMSRouteTransformer` special-cases that path: it
  requires an authenticated Admin/Editor, loads the draft via
  `IContentStore<PageDTO>.GetCurrentDraftAsync`, marks the request with
  `PreviewConstants.DraftRequestItemKey` (so the zones/widgets rendered inside read drafts), and dispatches to
  the page's controller — so a never-published page can preview. Links clicked inside a preview go to the
  published site. Preview is **admin-mode only** (`CmsDeploymentOptions.IsAdminMode`, set by
  `AddWebWayCmsAdmin`; a required `CMSRouteTransformer` dependency, so an unregistered mode fails closed;
  a rendering-only host never resolves `/_preview`). Every draft-read mark goes through
  `PreviewConstants.MarkDraftRequest(HttpContext)`, which also sends `Cache-Control: no-store` — the
  only gate is the per-request role check, so a cached draft could otherwise be replayed without it.
  The single-zone editor (`/wadmin/contentzones/zone-edit/{id}`) marks its request too, so it renders
  (and version-checks against) current drafts like the page zone editor. `PreviewConstants` and the `AdminRole`/`EditorRole` constants live in
  `WebWayCMS.Routing` (namespace `WebWayCMS`).
- Zone **items in a page's zones** (directly or via nested zones — `GetParentPageNodeForZoneAsync`
  resolves to a page) are **drafts until the page is published**, whichever path wrote them (zone editor,
  child CRUD, MCP). Delete of a once-published item saves an `IsActive=false` draft; a never-published
  item is hard-deleted; reorder saves draft ordinals. Items in **global/name-based zones** (no page)
  still auto-publish on write. Zone item CRUD/reorder goes through `IContentZoneService` (node-keyed).
- **Page publish cascades:** `PageModel.PublishPageAsync` (every publish path: toolbar, Pages list, page
  form, MCP `publish_content`) publishes the page, then `IContentZoneService.PublishPageZonesAsync`
  publishes every pending item under it in the same change set, and registers each item's widget routes.
  Widget routes for page-zone items change **only** at publish (never on a draft save/delete);
  global-zone items register on write. A zone failure after the page went live returns
  `"Page published; some zone changes failed: …"` and those items stay pending. The Pages list
  "Pending" tag includes pending zone drafts (`GetPagesWithPendingZoneChangesAsync`).
- **Page zone editor:** `GET /wadmin/contentzones/page/{pageNodeId}` (Pages list → **Edit Zones**) stacks
  every zone assigned to the page (`IContentZoneModel.GetPageZoneSlotsAsync`) in the unchanged zone
  editor, under a toolbar (status, Publish, Discard changes, Unpublish, Edit page, Back) and two tabs:
  Edit zones / Preview (an iframe of the existing preview action; `#preview` hash remembers the tab).
  Publish/Unpublish post to the existing actions with a `returnUrl` (local URLs only). Discard is
  `POST /wadmin/contentzones/page/{pageNodeId}/discard` → `DiscardPageZoneDraftsAsync`. Slots are created
  lazily by the page template, so an empty page loads the preview in the background and reloads once.
- **Nested zones are owned by items:** `ContentZoneAssignments.ParentItemNodeId` (page XOR item) keys a
  nested zone (e.g. a Layout's columns) to the zone item rendering it — `WidgetViewComponent.ModeView` sets
  `ViewData["ContentZone:ParentItemId"]` from `WidgetConfiguration.ZoneItemNodeId` (attached by
  `ContentZoneModel`; the shipped zone views also set it per item), and `GetOrCreateByItemSlotAsync`
  resolves it. A zone rendered inside a zone with no owning item renders nothing (it would otherwise alias
  the enclosing zone and recurse). A container therefore moves with its content; hard-deleting one deletes
  its nested zone tree first — the `ParentItemNodeId` FK is `Restrict`, so a delete that skips that fails.
- **Moving items:** `IContentZoneService.MoveItemAsync(item, targetZone, position, expectedVersionNumber)` (API
  `POST /api/contentzones/items/{id}/move`, MCP `move_child` via optional `IAdminCrudChildHandler.SupportsMove`).
  Same zone = reorder; cross-zone only within one page's zone tree and never into the item's own nested
  zones (a cross-zone move also renumbers the source zone's remaining ordinals); a draft until the page
  publishes. `expectedVersionNumber` (the moved item's version as the client rendered it, `data-version` on
  the edit view) rejects the move with `StaleVersionMessage` when the widget changed since the page loaded;
  every sibling the reorder rewrites carries the version it was read with, so a concurrent edit fails the
  move (409 from the API) instead of being silently reverted. A reorder or move commits in one DB
  transaction (both zones of a cross-zone move together), and a failure clears the scoped change tracker. The zone editor's `ContentZone/drag.js` (→
  `content-zone-drag.js`) drives it with Pointer Events (grip handle, mouse/touch) plus ArrowUp/ArrowDown
  on a focused handle; the edit view's `data-page-node-id` (owner page, `ViewData["ContentZone:OwnerPageId"]`)
  tells the client which zones accept a drop.
- **Zone-item writes carry an expected version.** `UpdateItemAsync(item, expectedVersionNumber)` backs the
  config save (`POST /api/contentzones/items`) and the metadata save (`PUT .../metadata`); the controllers
  pre-check the version against the loaded item and answer 409 with `StaleVersionMessage` when it mismatched
  (or when the store's own check rejects a write that raced past the pre-check). The edit view renders
  `data-version`; `content-zone-edit.js`/`content-zone-drag.js` read it via `czItemVersion` and send it as
  `expectedVersionNumber` on the config save, the metadata save and the move.
 - See [docs/architecture/01-data-tier.md](docs/architecture/01-data-tier.md).

## Widget Render Modes

- Widget views are split by **render mode**: files are named `{mode}.{variant}.cshtml` (mode
  lowercase). Five fixed modes: `view.*` (public single item, the default), `list.*` (public list),
  `admin.*` (single item on an admin page), `adminlist.*` (list on an admin page), and `edit.*` (the
  admin zone editor, plain Bulma on a light canvas). Un-prefixed files are ignored by discovery.
  The point: the site can be styled independently of the admin while the same content still renders
  correctly inside the admin.
- **The mode is derived, never picked by the widget or a caller** (`WidgetViewComponent.GetContextMode`):
  `Edit` when the zone is in edit mode (the `ContentZone:RenderMode` ViewData set by
  `ContentZoneViewComponent`); otherwise admin vs. public from the request path
  (`AdminPathPrefix.Matches` — `/wadmin` on a segment boundary), and single vs. list from the item's
  **display mode**. There is no `config.RenderMode` override and no `Summary` mode.
- **Display mode** (`WidgetDisplayMode`: `Single` | `List`) is a per-item choice stored in
  `WidgetMetadata.DisplayMode`. Only widgets declaring `[ContentZoneComponent(SupportsList = true)]`
  may be `List`: the zone editor shows a Single/List toggle in the Add Widget modal and a Display
  Mode field in the settings (cog) dialog for them only, and `WidgetMetadataJson.Normalize` rejects
  `List` for any other widget (zone API, MCP, content seed). A stored `List` on a widget that no longer
  supports it renders as `Single`, and is grandfathered on writes: an update of an item already stored
  as `List` keeps it (only *newly* choosing `List` is rejected), and restoring a historical `List`
  version restores it as `Single`. `SupportsList` is a `WidgetRegistrations` column that
  `CmsWidgetRegistrationSeeder` syncs from the attribute on every boot for every registration, active
  or not, matching names case-insensitively (read-only in the admin).
- **Single and List have different configurators:** tag config fields with
  `[FormProperty(DisplayModes = WidgetDisplayMode.Single | List)]` (default both). The form
  endpoint `POST /wadmin/widgets/registry/{name}/form?displayMode=single|list` renders only that
  mode's fields; the zone editor merges the rendered fields over the stored JSON, so the other mode's
  values survive a switch. A list-capable widget branches its data loading on `DisplayModeFor(config)`.
- Mode-aware widgets derive from `WidgetViewComponent` and return `ModeView(config, model)`
  instead of `View(...)`; `RenderModeFor(config)` reports the active mode. The base resolves the view
  through `IWidgetViewResolver`, which it pulls from `HttpContext.RequestServices` on first use
  (no constructor plumbing). The derived class name drives the component name (strip
  `ViewComponent`), which must match the `[ContentZoneComponent]` registration and view directory.
- The per-mode fallback chain lives in `WidgetViewResolver.Resolve` (pure, unit-tested). Admin and
  edit modes **mirror the public variant name** (no admin/edit pickers): **View** `view.{ViewName}`→
  `view.default`→empty; **List** `list.{ListViewName}`→`list.default`→View; **Admin**
  `admin.{ViewName}`→`admin.default`→View; **AdminList** `adminlist.{ListViewName}`→
  `adminlist.default`→`admin.{ViewName}`→`admin.default`→List→View; **Edit** `edit.{ViewName}` (or
  `edit.{ListViewName}` in list mode)→`edit.default`→generic edit card. Empty/`"default"` always
  means the mode's default view.
- Widget configs derive from `WidgetConfiguration` (abstract base: the `ViewName` (Single) /
  `ListViewName` (List) view pickers + an attached `WidgetMetadata`; do not re-declare `ViewPicker`
  properties). View pickers list only the views that exist for the field's render mode; a stored
  value for a missing/renamed view stays selectable rather than being dropped.
- **Per-item metadata** is a `MetadataJson` column on the zone item (4000 chars, normalized to
  `"{}"`, null/whitespace on a child update keeps the existing value). Every write is validated and
  re-serialized through `WidgetMetadataJson.Normalize` (never stored verbatim). The zone editor's settings (cog)
  button edits it via `POST /wadmin/widgets/registry/{name}/metadata-form` (render the form) +
  `PUT /api/contentzones/items/{id}/metadata` (save); `ContentZoneModel` deserializes it into
  `WidgetMetadata` and attaches it to `config.Metadata` at render time.
- `ViewDiscoveryService` scans host view locations *and* compiled package application parts, so a
  host `{mode}.{variant}` file **shadows** a package view of the same name (host views are searched
  before RCLs) — how a host overrides a built-in widget's rendering. Only
  `/Views/Shared/Components/{name}/` is scanned (no `Areas/*`, no sibling folders): rendering trusts a
  discovery hit, so it must only report views the view engine can load. Scans are cached per instance —
  one shared instance outside Development, one per request in Development.
- The widget component name is MVC's (`ViewComponentConventions.GetComponentName`), so
  `[ViewComponent(Name = ...)]` is honoured by the base class, the seeder, and the view directory.
- The admin navbar renders `RouteNavigation` with `ViewName = "Navbar"` → `admin.Navbar.cshtml`. Don't
  ship navbar markup as `admin.default`: it would take over every admin-page `RouteNavigation` widget.
- **Hard break, no compat shim:** un-prefixed view files are ignored (a host override not renamed to
  `view.*` silently stops overriding), list rendering reads `ListViewName`, not a legacy `ViewName`,
  and old metadata keys (`EditViewName`/`SummaryViewName`/`AdminViewName`) are ignored on parse.
- **Known exception:** `ArticleViewComponent` predates render modes and still calls `View(...)`
  with un-prefixed names (`Article`/`List`/`UpsertForm`); migrating it is a known follow-up.
- See [docs/widget-system.md](docs/widget-system.md) → Render Modes.

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

## Media Library and Images (bytea, content-addressed)

- Image bytes live in **Postgres `bytea`**, content-addressed by **SHA-256**, behind an
  `IMediaBlobStore` seam (`AddMediaStore<TStore>()` on `IWebWayCmsBuilder` swaps it; the default is
  registered with `TryAdd` so a host choice wins). Chosen because admin and rendering-only hosts
  share a *database*, never a filesystem, and the shipped `docker-compose.yml` mounts no volume.
- Three tables: `MediaBlobs` (catalog metadata, unversioned, `Hash` PK), `MediaBlobBytes`
  (`bytea`, split out so catalog queries never materialize megabytes), and `Images` (versioned
  `IVersionedContent` + `BlobHash`/`AltText`/`Caption`).
- **Bytes never touch a view model.** `ContentFieldMerger` JSON round-trips the whole upsert view
  model on every MCP update and seed apply, so `ImageUpsertViewModel` carries a `BlobHash` string
  and upload goes through `POST /api/media/upload` separately. Widget config is capped at 4000
  chars, so the Image widget stores a **node id**, not a hash.
- `ImageValidator` (`WebWayCMS.Core/Security/`) sniffs **magic bytes** (PNG/JPEG/GIF/WebP), rejects
  **SVG** outright, enforces the size cap, and parses dimensions from file headers — which is how
  the CMS stores width/height with **no imaging-library dependency**. EXIF is **not** stripped
  (deferred; GPS in phone photos is served verbatim).
- Serving is `GET /media/{hash}/{fileName?}` (`MediaController`, in Core so rendering-only hosts
  serve it), `immutable` cache + ETag + 304. No CSP change needed — `img-src 'self'` covers it. The
  literal path outranks the `{**slug}` catch-all; `[CmsRoute("/media", IsReserved = true)]` also
  reserves the pattern.
- The admin picker is `FormImagePicker`, selected by **name** (`FormComponent = "ImagePicker"`) like
  `FormEntityPicker` — no `EditorType` enum change. `media-picker.js` uploads and writes back only
  the hash, keeping the admin form url-encoded.
- Seed files reference images with `@media:{path}` (`SeedMediaResolver`), resolved against the seed
  file's directory or its assembly's resources. MCP gets one read-only `list_media` tool; bytes
  deliberately do not travel over MCP.
- Config section `"Media"`: `MaxUploadBytes` (default 10 MB), `AllowedContentTypes` (matched against
  the *sniffed* type).
- See [docs/architecture/16-media-and-images.md](docs/architecture/16-media-and-images.md).

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
  `<script>` blocks. Framing is denied everywhere (`X-Frame-Options: DENY`, `frame-ancestors 'none'`)
  **except** responses under `/_preview` (`PreviewConstants.PathPrefix`), which send `SAMEORIGIN` /
  `frame-ancestors 'self'` (`CspPolicyBuilder.BuildForPreview`; a host `frame-ancestors` override still
  wins) so the page zone editor can embed the draft preview. The policy string is built by the unit-tested `CspPolicyBuilder`.
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
- Admin scripts exist in two places: the `Views/**/{edit,drag,PageZonesEdit,PageUpsert,form-components}.js`
  sources and the `wwwroot/js/*` copies the `CopyViewScripts` MSBuild target generates on every build.
  Edit only the `Views/**` source — a hand-edit to a `wwwroot` copy is silently reverted by the next build.

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
