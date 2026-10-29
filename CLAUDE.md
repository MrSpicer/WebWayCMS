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
- A host content type relates to other content with **one attribute** (`[ContentRelation("<targetKey>")]`
  on a `Guid`/`Guid?`/`IEnumerable<Guid>` upsert view-model property; see Content Relations below): no FK
  column, no `HasOne<ContentNode>()`, nothing in the host migration. Every `AdminCrudModel<T>` constructor
  passes `(IChangeSetScope, IRelationBinder, IContentDeleteService)` to the base, and implements
  `DeleteSelfAsync` (its own node only) instead of `DeleteAsync` (now the base's, through the relation engine).
- A host `AdminCrudModel<T>` registered as `IAdminCrudHandler` is served by `AdminContentController`
  and MCP-visible for free. Two handlers sharing a `ContentType` key throw at first registry resolution.
- **Containment is the parent–child mechanism** (`IAdminCrudChildHandler` and the `/{parentKey}/{childType}`
  routes are gone). A containment shows on the parent's edit screen as a **children panel**
  (`_RelationChildren.cshtml`, rendered by `_AdminLayout` from `RelationChildrenPanelViewModel`s the Edit GET
  builds via `ContentRelationRegistry.GetContainmentsTargeting`; not in the modal; `ShowInParentAdmin = false`
  opts out): the children (`IAdminCrudHandler.GetRelatedChildrenAsync`), a **New** link that preselects the
  parent (`/wadmin/{child}/create?{RelationName}={parent}` — `RelationPreselect.Apply` fills any relation
  property named in the create query), and Up/Down reorder for an `Ordered` one (`relation-children.js` →
  `POST /wadmin/{child}/relations/{relation}/{parent}/reorder` → `ReorderRelatedAsync`). `AdminCrudModel`
  implements children/reorder/`MoveRelatedAsync` generically. Content zone items are the top-level handler
  `ContentZoneItemModel` (`contentzoneitems`; its `ContentZoneNodeId` is the ordered `Zone` containment,
  hidden and `ShowInParentAdmin = false`); Edit/Delete/history accept a local `returnUrl`, and the top-level
  restore takes `expectedVersionNumber` (`IAdminCrudHandler.RestoreVersionAsync(versionId, expected)`).
- A host content type is a picker target via `[ContentRelation("<contentTypeKey>")]` (which sets the
  picker's `EntityType`): the list endpoint is resolved server-side (`EntityPickerEndpoint.Resolve` over `IAdminHandlerRegistry` —
  exact key, plural, then secondary list key) and emitted as `data-endpoint`; `form-components.js`
  has no endpoint map.
- The TestHost's worked example is `faqtopics` (`FaqTopicDTO`/`FaqTopicModel`): `FaqUpsertViewModel.FaqTopicId`
  is an optional containment (children panel, MCP child tools, visibility, delete cascade), and the Faq widget's /
  configured page's FAQ pickers are `config.*` references.
- See [docs/architecture/14-host-extensibility.md](docs/architecture/14-host-extensibility.md).

## Content Versioning (Node/Version model)

- Content identity and version are **split**. A logical item is one `ContentNode`
  (`Id`, `ContentTypeKey`, `SiteId`, `CreatedUtc`, `CreatedBy`, `IsDeleted/IsArchived/IsHidden`);
  its mutable data lives on `ContentVersion` rows (`NodeId`, `VersionNumber`, `Culture`, `Segment`,
  `State` (`Draft/InReview/Approved/Published/Archived`), `IsCurrentDraft`, `Title`, `Slug`,
  `PublishStartUtc/EndUtc` (scheduling seam, not yet filtered), `ChangeNote`, `ChangeSetId`, `CustomFields`).
  Every cross-entity FK points at `ContentNode.Id`. Content types implement `IVersionedContent`
  (`VersionId` + `Version`, shared PK/FK into `ContentVersion`).
- **One write engine:** `IContentStore<T>` / `ContentStore<T>` (in `WebWayCMS.Data`) replaces the old
  `ContentService<T>`/`PageService`/zone CRUD. Reads are `GetAsync(nodeId)`/`GetAllAsync()`/… (read-context
  aware) plus `GetCurrentDraftAsync`/`GetAllCurrentDraftsAsync` (admin) and `GetAllVersionsAsync` (history).
  Writes are `SaveDraftAsync(entity, expectedVersionNumber)`, `PublishAsync`, `UnpublishAsync`,
  `RestoreAsync`, `DeleteAsync`, `DeleteVersionAsync` and return `ContentWriteResult`; a conflicting write
  maps to the shared `StaleVersionMessage`. There is no draft discard in the store (nothing deletes a draft
  any more; a page "Discard changes" is a revert to what is live, see page-set history). `RevertPublishAsync(nodeId, publishedVersionId,
  displacedVersionId)` undoes one publish (from `PublishAsync`'s `VersionId`/`DisplacedVersionId`) when a
  follow-up step such as route registration fails; `PublishPageAsync` skips it when the page was already live.
- **Two invariants** (enforced in code; filtered unique indexes are the DB backstop): exactly one
  `IsCurrentDraft` per (NodeId, Culture, Segment), and at most one `Published` per variant.
  **Every save mints a new version** (any content type, published or draft): a version is never rewritten in
  place, so every save stays restorable. Each version carries a **change note**: the ambient
  `IChangeSetScope.UseChangeNote(note)` if one is open (zone writes set e.g. "Content edited", "Settings
  changed", "Moved to Side (position 2)", "Position changed (moved by another widget)", "Removed",
  "Restored from version n"), else "Created"/"Edited"/"Restored from version n" — never copied from the
  previous version. `expectedVersionNumber` (hidden form field) detects concurrent edits ("changed by someone else").
- **Content relations** ([doc 17](docs/architecture/17-content-relations.md), all six phases: Article → Article
  List, config pickers, page routes, the zone structure, page → parent page, and the TestHost): a `ContentRelation` row (`SourceVersionId`, `TargetNodeId`, `Name` = the declaring
  property, `Ordinal`, `Qualifier`, `Kind` Containment/Reference/Owned) is **owned by the source's version**
  (auto-included with `ContentVersion.Relations`; no DB FK on `TargetNodeId`), so a relation change mints a
  version and is a draft until publish. Declared with `[ContentRelation(target, Kind, Required, Ordered,
  OnTargetDelete, ShowInParentAdmin)]` (Forms; implies the EntityPicker). Writes go through `ContentVersion.RelationWrites`
  (`SetRelation`/`SetRelations`/`ReplaceAllRelationsFrom` in `ContentVersionRelations`): `ContentStore`
  starts each new version from a baseline (edit = current draft's rows, restore = the historical rows,
  create = none) and replaces only the written names, so a save that never mentions a relation keeps it.
  The **mapper** moves `[ContentRelation]` properties to/from the version for every profile mapping (no
  per-type code); `AdminCrudModel.SaveUpsertAsync` runs `RelationBinder` after `ModelValidator` (target must
  be a current draft of the declared type, `Required`; field error on the property; a containment whose
  target's current-draft chain reaches the source — "can't be placed under itself or one of its descendants" —
  is refused). **Containment
  visibility:** every read-context `ContentStore` read drops an item whose containment chain isn't visible
  (`ContainmentVisibility`, scoped, memoized per request; skipped in Draft; snapshot-governed nodes pass).
  **Delete:** `AdminCrudModel.DeleteAsync`/`TryDeleteAsync` → `ContentDeleteService`: containment descendants
  (deepest first) and owned targets go in one `ICmsTransaction` (a lone node skips the transaction); a
  reference from outside blocks (`Restrict`, the default) or is removed by saving the referrer (`Detach`; a
  `config.*` referrer is saved through `IContentZoneService.UpdateItemAsync` for a zone item, or by editing
  the page's hydrated configuration JSON for a page — `ContentZoneService.MergeUpdate` carries the caller's
  `RelationWrites` onto the reloaded version);
  only **current draft and published** versions count (`IContentRelations`), historical rows resolve to
  nothing. Descendants are deleted by `IContentDeleter` (every `AdminCrudModel`, plus explicit registrations).
  The result's `DeletedDescendants` (containment descendants, leaving out zones and zone items, so a page
  delete counts its child pages) reaches the admin flash and MCP `delete_content`. `ContentRelationRegistry`
  (admin) validates every declaration at boot. Page revert/discard copy relations; `PageSetEquality`
  compares them. **Config relations (phase 2):** a `[ContentRelation]` on a widget or page configuration
  type is stored as `config.*` rows owned by the zone item's or page's version: `WidgetConfigurationJson`
  `ExtractRelations` strips the ids on write, `ApplyRelations` writes them, and `HydrateRelations` (plus
  `RelationProperties.Write(..., ConfigPrefix)`) puts them back for render, the editor form and MCP. The
  EntityPicker is multi-select for `IEnumerable<Guid>`; New/Edit open the target's form in an iframe modal
  (`?modal=1`, `AdminModal`, the minimal `_AdminLayout` branch, `relation-modal.js`, and a `SAMEORIGIN`
  CSP frame-ancestors exception for those responses).
- **Read context is a mode boundary:** `IContentReadContext { Mode (Published|Draft), Culture, Segment }`.
  Rendering-only hosts register a sealed `PublishedContentReadContext` (can never serve a draft); admin hosts
  register `PreviewAwareReadContext` (Draft only for an Admin/Editor whose request is marked with
  `HttpContext.Items[PreviewConstants.DraftRequestItemKey] = true` — set by `CMSRouteTransformer` for a
  resolved `/_preview/{nodeId}` and by the page zone editor action). Draft reads are **per request**; there is
  no preview cookie, so previewing a page never puts the live site into draft mode.
  A third mode, `ContentReadMode.Snapshot`, renders a page-history entry: `/_preview/{pageNodeId}/history/{entryId}`
  (Admin only) stores a `ContentReadSnapshot` via `PreviewConstants.MarkSnapshotRequest`; nodes it governs
  (the page + every item ever in its zone tree) read the captured versions (or nothing), everything else
  reads published. It wins over a draft mark; `PublishedContentReadContext` never enters it.
  Version selection lives in one place: `ContentQueryExtensions.AtReadContext`.
- **Edits are drafts until an explicit Publish.** A page's URLs are **`routes` content items** owned by
  the page (an owned relation, `PageUpsertViewModel.RouteIds`); a new page mints one Derived route.
  Publish publishes those route items and **projects** them into `CMSRoutes` — a draft slug/route change
  doesn't touch the live table until then. `CMSRouteService.UpsertAsync` keys a projected row by
  `RouteNodeId` (manual rows by `(OwningContentNodeId, Pattern)`) and returns
  `(bool Success, string? ErrorMessage, CMSRouteDTO? Route)` — a foreign owner on the same pattern is
  a collision, never a silent steal, and a route item has **one owner** (a row for it under another
  owner, or a page save relating another page's route, is refused). A page's publish, its route items'
  publishes, the projection and the re-projection of **live** descendants' derived routes (from their
  *published* page/route versions) run in **one `ICmsTransaction`**: a collision, a missing route item or a
  colliding child rolls the whole publish back with the reason (`UpsertAsync` joins an ambient
  transaction; `PageModel` re-invalidates `ICMSRouteRegistry` afterwards). A child's derived prefix is its
  parent's **Derived** row (the `RouteMode` data token), not whichever row comes first; explicit routes
  are untouched. Unpublish removes the projection but keeps
  the route items as drafts; deleting the page deletes its route items. Projected `cmsroutes` rows are
  read-only (edit them from their owner). Publish/unpublish are
  `POST /wadmin/{contentType}/publish|unpublish/{nodeId}`. Restore is one-step (`RestoreVersionAsync`,
  DTO-level clone); a zone item restore is one-step too (`RestoreVersion` POST on `contentzoneitems` →
  `ContentZoneItemModel.RestoreVersionAsync` → `IContentZoneService.RestoreItemVersionAsync`: content,
  metadata (List→Single via `WidgetMetadataJson.Restorable`), active state, **zone and ordinal**, re-inserted
  at the old position as a draft; refused when the old zone is gone or on another page). There is no zone item
  restore-edit form; MCP `restore_child_version` uses the same path. **A page's parent is a versioned
  containment** (`[ContentRelation("pages", Kind = Containment)] PageUpsertViewModel.ParentNodeId`, relation
  `PageModel.ParentRelation`; `ContentNode` has no parent column): the page form's **Parent Page** picker
  moves a page as an ordinary edit, a draft until publish, whose publish re-derives its URL and re-projects its
  live descendants'. Siblings are `FindCurrentDraftsRelatedAsync(ParentRelation, parent)` (roots:
  `FindCurrentDraftsWithoutRelationAsync`); the publish-time slug check (`PageModel.IsSlugAvailableAsync`) and
  the admin tree both derive nested URLs from it. A published child of an unpublished parent is hidden by
  containment visibility, so `CMSRouteTransformer` answers **404** when a route's owning page (or a widget
  route's `ParentPageNodeId` page) reads as null. **Deleting a page deletes its subtree** (child pages
  deepest first, each with its zones and routes, one transaction): the Pages list confirm names the count
  (`PageTreeNode.DescendantCount`, all depths, via the shared delete modal's `data-delete-descendants`).
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
  item form, MCP). **Removing an item is never a delete:** it saves an `IsActive=false` version (published at
  once in a global zone), so its history stays and a restore or page revert brings it back (a removed
  container keeps its nested items, hidden). Reorder saves new ordinals only for items that moved; removed items keep their places in the order, so no two
  items share an ordinal. Items in
  **global/name-based zones** (no page) still auto-publish on write. Zone item CRUD/reorder goes through
  `IContentZoneService` (node-keyed). Only a zone/page delete hard-deletes items.
- **Page publish cascades:** `PageModel.PublishPageAsync` (every publish path: toolbar, Pages list, page
  form, MCP `publish_content`) publishes the page, then `IContentZoneService.PublishPageZonesAsync`
  publishes every pending item under it in the same change set, and registers each item's widget routes.
  Widget routes for page-zone items change **only** at publish (never on a draft save/delete);
  global-zone items register on write. A zone failure after the page went live returns
  `"Page published; some zone changes failed: …"` and those items stay pending. "Pending changes" (Pages list
  "Live · Changes pending", editor status, Discard button) compares **content**:
  `IPageHistoryService.GetPagesWithUnpublishedChangesAsync` (page fields vs. the live page, plus
  `PageZoneTree.GetPagesWithPendingZoneChangesAsync`), so drafts equal to what is live — left by a discard or a
  revert — are not pending. (The publish set `GetPendingItemNodeIdsAsync` stays state-based.)
- **Page-set history:** an entry (`PageSetSnapshot`: a page version + every **active** zone item version in its
  tree, nested included; `Kind`, `Note`, content `StateHash`) is recorded by `IPageHistoryRecorder` after
  **every change** to the page set — a **Draft** entry (current drafts) after each page save/restore, each
  zone item add/edit/settings/remove/move/reorder/restore (`ContentZoneService` records once per write, after
  it committed, for every page whose tree holds the zone — none for a global zone), each revert and discard
  — and a **Published** entry (what went live) after each publish (`PageModel.PublishPageAsync` /
  `PublishZoneItemsAsync` → `IPageHistoryService.CaptureAsync`). An entry identical by content to the latest
  (Draft: latest of either kind; Published: latest Published) is skipped; a failed write is logged and never
  fails the change; no backfill. Zone-tree queries live in `PageZoneTree` (shared, so `PageHistoryService`
  does not depend on `ContentZoneService`). Deleting a single captured page/item version is refused by the
  handlers (`TryDeleteVersionAsync` / `TryDeleteChildVersionAsync`, used by the admin and MCP) — with an entry
  per change that is most versions; any store-level hard delete (page delete, zone delete) removes whole
  referencing snapshots. The editor toolbar's **History** button → `AdminPageHistoryController`
  (`/wadmin/contentzones/page/{id}/history`, Admin only): list (Draft/Published tag + note; paged by
  `skip`/`take`, default 50, max 200, via `IPageHistoryService.ListPageAsync`, as is MCP `list_page_history`), entry detail
  (diff vs. previous), compare with current drafts (by content, `PageSetEquality`/`PageSetComparer`),
  preview, delete entry, and **revert** — the page + every item as new drafts in one transaction (later
  items become inactive drafts, never deleted), guarded by a draft state token (`StaleStateMessage`), and
  itself recorded. **Discard changes** = `IPageHistoryService.DiscardUnpublishedChangesAsync`: the same
  mechanics with what is live as the target (never-live items become inactive drafts), recorded, so it can
  be undone; refused (button hidden) when the page is not published. Each zone item's toolbar has a History
  link to its history page (`/wadmin/contentzoneitems/versions/{id}`) with a local `returnUrl`. MCP: `list_page_history`,
  `get_page_history_entry`, `revert_page_to_entry`, `delete_page_history_entry`.
- **Page zone editor:** `GET /wadmin/contentzones/page/{pageNodeId}` (Pages list → page title or **Edit zones**) stacks
  every zone assigned to the page (`IContentZoneModel.GetPageZoneSlotsAsync`) in the unchanged zone
  editor, under a toolbar (status, Publish, Discard changes, Unpublish, Edit page, Back) and two tabs:
  Edit zones / Preview (an iframe of the existing preview action; `#preview` hash remembers the tab).
  Publish/Unpublish post to the existing actions with a `returnUrl` (local URLs only). Discard is
  `POST /wadmin/contentzones/page/{pageNodeId}/discard` → `IPageHistoryService.DiscardUnpublishedChangesAsync`. Slots are created
  lazily by the page template, so an empty page loads the preview in the background and reloads once.
  Below the slots, an **Unplaced content** panel (editor only, never live) lists zones owned by the page's
  widgets that nothing rendered (e.g. a Layout's columns stranded by an old view change):
  `ContentZoneViewComponent` logs every edit-mode zone and widget in `ZoneEditRenderLog`
  (`HttpContext.Items`), `IContentZoneModel.GetUnplacedCandidatesAsync` lists the candidates
  (`IContentZoneService.GetItemOwnedZonesForPageAsync`), and the view renders each unrendered one by
  `zoneId` in edit mode (looping, since a stranded zone can hold a container) so its widgets can be dragged
  back or removed. Removing a widget that holds widgets confirms with the nested count (`edit.js`).
- **The zone structure is relations (phase 4; `ContentZoneAssignments` and the items' `ContentZoneNodeId`/
  `Ordinal` columns are gone).** An item's version sits in a zone through an ordered **`Zone` containment**
  (`ZoneRelations.Zone`, named `ContentZoneNodeId` after the item form property that declares it; the row's `Ordinal` is its position), and a page's or container item's zone names
  its owner through an **`Owner` containment** on the zone's version (`ZoneRelations.Owner`; `Qualifier` =
  slot). `ContentZoneItemDTO.ContentZoneNodeId`/`Ordinal` are **in-memory accessors** over the `Zone`
  relation, ignored by EF: queries use `ZoneRelations.InZone/InZones/ZoneOf/ZoneOrdinal`, writes
  `ZoneRelations.Place`/`Placed` (an update's `MergeUpdate` drops a `Zone` write — placement changes only
  through add/reorder/move/restore/remap). Ownership is read from current drafts by
  `PageZoneTree.ZoneOwners()` (an `OwnedZone` per zone: `ParentPageNodeId` XOR `ParentItemNodeId`; the owner is
  an item when its node is a `contentzoneitems` node, else a page — a vanished owner keeps the zone
  page-owned, never global). An owned zone's node id is **deterministic**, `ZoneNodeIds.For(owner, slot)`
  (RFC 4122 v5, slot case-sensitive), created with `IContentStore.CreateWithNodeIdAsync`, so two concurrent
  first renders collide on the `ContentNodes` key and the loser reads the winner's zone. A zone has exactly
  one owner (no shared zones). Placement and ownership are part of the version, so page-history revert/
  discard restore them through `ReplaceAllRelationsFrom`; `PageSetEquality` compares placement as fields and
  leaves the `Zone` relation out of content equality. Zones and items are **containment children**, so
  containment visibility hides a zone (and its items) under an unpublished page in Published store reads,
  and deleting a page cascades through `ContentDeleteService` (zones via `ContentZoneModel`, items via the
  registered `ZoneItemDeleter` → `IContentZoneService.DeleteItemAsync`), deepest first, in one transaction;
  `DeletedDescendants` counts those zones and items.
- **Nested zones are owned by items:** the `Owner` relation keys a nested zone (e.g. a Layout's columns) to
  the zone item rendering it — `WidgetViewComponent.ModeView` sets
  `ViewData["ContentZone:ParentItemId"]` from `WidgetConfiguration.ZoneItemNodeId` (attached by
  `ContentZoneModel`; the shipped zone views also set it per item), and `GetOrCreateByItemSlotAsync`
  resolves it. A zone rendered inside a zone with no owning item renders nothing (it would otherwise alias
  the enclosing zone and recurse). A container therefore moves with its content; hard-deleting one deletes
  its nested zone tree first (`DeleteItemAsync`; a visited set stops a nested-zone cycle).
- **A container's view change carries its content.** Nothing declares a container's slots: they are
  **discovered by a dry render** (`IWidgetSlotDiscovery` → `ViewComponentSlotDiscovery`, Presentation),
  which invokes the widget with a `SlotDiscoveryRecorder` in ViewData — `ContentZoneViewComponent` records
  each top-level zone it owns and renders nothing, and `WidgetViewComponent.GetContextMode` uses the public
  `view.*`/`list.*` variant. `ContentZoneModel.UpdateItemAsync`/`RestoreItemVersionAsync` (every write path:
  zone editor config + cog, item form, MCP `update_child`/`restore_child_version`, the seeder) plan a
  remap (`SlotRemapPlanner`: same slot name keeps its content, the rest pair by position, leftovers append
  to the new view's last slot) when the item owns a zone holding widgets (a fast path skips the render
  otherwise) and is still active, then call the service's `SlotRemapPlan` overloads: the container's save
  and the moves (every current draft, removed ones too, "Moved to {slot} (layout changed)") commit in one
  transaction (target zones created inside it, after the version-checked container save, so a stale or failed save
  leaves no empty zones; zone creation joins an open transaction since Npgsql has none nested), as **one** page-history
  entry. Failed discovery saves without moving (the content shows in the Unplaced panel). Page revert and
  discard never remap. A container's history page is a **combined timeline**: its versions plus every
  version of every widget ever inside it (`GetDescendantItemNodeIdsAsync`), newest first, each row labelled
  with its widget (`VersionHistoryViewModel.IsCombined`) and restored against its own current version.
- **Moving items:** `IContentZoneService.MoveItemAsync(item, targetZone, position, expectedVersionNumber)` (API
  `POST /api/contentzones/items/{id}/move`, MCP `move_child` via `ContentZoneItemModel.MoveRelatedAsync`).
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
  (or when the store's own check rejects a write that raced past the pre-check). The zone item handler's update
  (`ContentZoneItemModel`: admin item form, MCP `update_child`, the seeder) does the same with `ExpectedVersionNumber`. The edit view renders
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
  `"{}"`, null/whitespace on an item update keeps the existing value). Every write is validated and
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
- **Routable widgets and display mode:** `IRoutableViewComponent.GenerateRoutesAsync(parentRoute,
  itemNodeId, displayMode, ct)` receives the item's *effective* display mode (a stored `List` counts only
  when the registration `SupportsList`; `RouteRegistrationService` computes it from the item's
  `MetadataJson`), so a widget can give its list items different routes (the TestHost `Faq` registers
  none for a list; the Article widget registers the same `{slug}` in both modes). Every CMS request carries a `slug` route value (the `{**slug}` catch-all puts the page
  path there), so a widget reads route values only via `WidgetViewComponent.IsOwnWidgetRoute(config)`
  (the matched `CMS:RouteData` route is owned by this item), and a list yields to another item's route
  with `IsWidgetRouteRequest()` (the matched route is a widget route, marked by its `ParentPageNodeId`
  data token). `ArticleViewComponent` (core) and the TestHost `Faq` are the reference implementations.
  A route parameter may not use an ASP.NET reserved name (`action`/`area`/`controller`/`handler`/`page`):
  the transformer passes matched values through as route values, so e.g. `{page}` makes MVC look for a
  Razor Page and 404s. `CMSRouteService.UpsertAsync` rejects such patterns (use `{pageNumber}`).
- See [docs/widget-system.md](docs/widget-system.md) → Render Modes.

## Form Field Render Modes

- Form controls (`[CMSFormComponent]` view components) use the same `{mode}.{variant}.cshtml` convention
  with four fixed modes (`FormFieldRenderMode`): `write.*`, `read.*`, `adminwrite.*`, `adminread.*`.
  Read vs. write comes from `<form-fields mode>`; admin vs. public from the request path
  (`AdminPathPrefix.Matches`). It is derived, never passed by the caller (the tag helper invokes a
  control with just `field`).
- The shipped controls ship only `adminwrite.default` / `adminread.default` (Bulma). A site styles public
  forms by adding `write.default` / `read.default`. A field picks a variant with
  `[FormProperty(FieldViewName = "…")]`, which is unrelated to the ViewPicker's `ViewComponentName`/`RenderMode`.
- Fallback (`FormFieldViewResolver`, pure; each step tries `{mode}.{variant}` then `{mode}.default`):
  write → adminwrite; read → write → adminread → adminwrite; adminwrite → write;
  adminread → adminwrite → read → write. No view on the chain **throws** (a missing input would drop the
  value on save).
- `FormFieldViewComponentBase.FieldView(field, model)` is the render call for a control that builds its own
  view model. The resolver comes from `HttpContext.RequestServices`, so constructors stay as they are.
- **Hard break:** un-prefixed `Write.cshtml`/`Read.cshtml` are ignored, and the registry's
  `WriteViewName`/`ReadViewName` (attribute, DTO, columns) are gone.
- See [docs/form-control-system.md](docs/form-control-system.md) → Render Modes.

## Articles

- **Articles are a top-level content type** (`ArticleModel : AdminCrudModel<ArticleDTO>`, key `articles`,
  draft → publish like everything else; `WriteRoles` Admin + Editor). Each belongs to exactly one
  **article list** (`ArticleListModel`, key `articlelists`) through a **containment relation**:
  `[ContentRelation("articlelists", Kind = Containment, Required = true)] ArticleUpsertViewModel.ArticleListId`
  (relation name `ArticleModel.ListRelation`; there is no `ArticleListNodeId` column). Moving an article to
  another list is a new version, a draft until published. The Article List edit screen lists its articles (the
  generic children panel) and its **New Article** link preselects the list (`?ArticleListId=`).
- The relation binder rejects an empty/unknown list (field error on `ArticleListId`); the article save path
  rejects a slug another article's current draft already uses (slugs are global: the widget resolves
  `/{page}/{slug}` across every list).
- Admin entry point is **Article Lists** (`/wadmin/articlelists`, the navbar "Articles" item); a list links
  to `/wadmin/articles?list={nodeId}` (`ArticleModel.ListQueryKey`), which filters the Articles index and
  preselects the list on **New Article**. Deleting a list deletes its articles in one transaction (the
  relation engine's containment cascade; an article moved to another list, live there, is not touched).
- **Public visibility follows the list:** the store's containment filter hides an article while its list is
  not visible in the same read context, so unpublishing a list hides its articles.
- The **Article widget** is list-capable: **Single** = the article named by the URL slug on the item's own
  `{slug:regex(^[a-z0-9-]+$)}` route, else the configured `ArticleId`, else nothing (`_NotFound` when a
  lookup was attempted and failed); **List** = the configured list (`ArticleListId`, `MaxItems`,
  `SortOrder` newest/oldest/title). A List item **owns the same `{slug}` route** and, on it, renders that
  article (only one from its own list; else `_NotFound`) with the `view.*` views — so a lone List widget's
  card links (`ArticleListViewModel.DetailBaseUrl` + `/{slug}`) work. It yields on another item's widget
  route. A List and a Single Article on one page claim the same pattern, so the page publish reports a
  route collision; use one per page. In a draft preview the cards link to the published page URL (no link
  if the page was never published). Views: `view.default`, `list.default`, `_NotFound` (Presentation),
  `edit.default` (Admin).

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
- **Zone items** are seeded too: an item with a `zone` locator (`{ "page": "@seed:…", "slot": "Main" }`,
  `{ "item": "@seed:…", "slot": "Left" }` for a nested zone, or `{ "name": "SiteFooter" }`) and
  `contentType: "contentzoneitems"` is saved through the ordinary top-level dispatch with the locator's zone in
  its `contentZoneNodeId` field (created on demand; a `zone` locator on any other content type, the old
  `"contentzones"` form included, is skipped with a warning).
  Zone items apply after all top-level items (one shared retry loop), in file order per zone. **The page's
  own publish state is respected:** the seeder never publishes a page; on a live page only the
  `publish: true` items are published (`IPageModel.PublishZoneItemsAsync`, so admin drafts stay pending),
  on an unpublished page they stay drafts. A `publish: false` item in a global zone is skipped (global
  writes go live). Owners are validated (existing page / zone item); a locator moved to another page is
  skipped with a warning. Fixtures must set `"isActive": true`.
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
  chars, so the Image widget stores a **`config.ImageNodeId` relation** (owned by the zone item's
  version), not a hash and not an id inside the config JSON.
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
  registries) to AI agents over MCP. The child tools are generic over containment: `list_children`,
  `create_child`, `reorder_children`, `move_child` take `parentType` + `parentKey` (node id) + `childType`
  (+ `relation` when the child type has several containments to that parent); `get_child`, `update_child`,
  `delete_child`, `list_child_versions`, `restore_child_version`, `delete_child_version` take `childType` + id.
  `list_content_types` reports each type's `childTypes`. Its tools delegate to the same `IAdminHandlerRegistry` /
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
  point (`AdminCrudModel.SaveUpsertAsync` → `SaveUpsertCoreAsync`), covering both the admin UI and the
  MCP tools, on every `string`
  property marked `[FormProperty(EditorType = EditorType.RichText)]`.
  Stored content is therefore safe to render with `@Html.Raw`. Zone-item widget config JSON is outside
  that choke point, so every zone-item write (zone editor API + `ContentZoneItemModel`) runs
  `WidgetConfigurationJson.Sanitize`: it rewrites only the config type's RichText values in place
  (other keys kept, unreadable JSON fails closed to `{}`). The same write runs `ExtractRelations`,
  which removes the configuration type's `[ContentRelation]` ids and stores them as `config.*` version
  relations; page configuration JSON goes through the same extraction in `PageModel`. The raw page-head TextArea fields
  (`GenericPageConfiguration.Meta`/`Style`) are neutralized at render time by `HeadMarkupSanitizer`
  via `SanitizedForRender()` (`<meta>`/`<link>` allow-list; CSS verbatim except `</` → `<\/`).
  **Required-field validation**
  (`ModelValidator`, also `WebWayCMS.Core/Security/`) joins sanitization at
  `AdminCrudModel.SaveUpsertAsync` (plus the CMS-route and zone-item save paths), so a
  field `describe_content_type` advertises as `required` is actually enforced on MCP writes.
- **Form attribute encoding:** all HTML input attributes are built by `FormAttributeBuilder`
  (`WebWayCMS.Forms/Forms/FormAttributeBuilder.cs`), which encodes every value exactly once via
  `HtmlEncoder.Default` across 16 view components. Views emit the result with `@Html.Raw`, so
  double-encoding bugs cannot creep in through per-view string concatenation.
- **Content-Security-Policy** is emitted by the middleware in `WebWayCMS/Startup/CmsMiddlewarePipeline.cs` and is
  **host-configurable via the `"Csp"` config section** (`CspOptions`): `Enabled` (default true),
  `ReportOnly` (default false), and a `Directives` map. The CMS ships secure defaults that keep the
  admin UI working (CKEditor/FontAwesome CDNs; Bulma is served locally from
  `_content/WebWayCMS.Presentation/css/bulma.min.css`); a host overrides or adds individual directives,
  and directives it does not mention keep the CMS default (set a directive to empty to drop it). The
  default `script-src` allows no `'unsafe-inline'`, so keep admin scripts in files, not inline
  `<script>` blocks. Framing is denied everywhere (`X-Frame-Options: DENY`, `frame-ancestors 'none'`)
  **except** responses under `/_preview` (`PreviewConstants.PathPrefix`), which send `SAMEORIGIN` /
  `frame-ancestors 'self'` (`CspPolicyBuilder.BuildForPreview`; a host `frame-ancestors` override still
  wins) so the page zone editor can embed the draft preview. The policy string is built by the unit-tested `CspPolicyBuilder`.
- **Forwarded headers** are trusted from loopback only by default; a host behind a remote proxy sets
  `ForwardedHeaders:KnownProxies`/`KnownIPNetworks`, or `TrustAllProxies` (the shipped
  docker-compose sets it — nginx is a separate container).
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
- Admin scripts exist in two places: the `Views/**/{edit,drag,PageZonesEdit,PageHistory,PageUpsert,form-components}.js`
  sources and the `wwwroot/js/*` copies the `CopyViewScripts` MSBuild target generates on every build.
  Edit only the `Views/**` source — a hand-edit to a `wwwroot` copy is silently reverted by the next build.
- The admin is deliberately **dense**: `_AdminLayout` sets `<html class="admin-html">` (14px base) and
  `admin.css` → "Density" tightens Bulma's variables under `.admin-shell`. An admin page header is one row:
  `.admin-page-header` > `.admin-page-title-row` > `.admin-page-heading` (the ancestor crumbs in a `.breadcrumb`,
  no `is-active` crumb, then `<h1 class="title is-5">`; CSS draws the separator) plus any `is-small` actions.
  The body is `<div class="container[ is-fluid] admin-page-body">`, not a `<section class="section">`.
- `TempData["Success"]`/`TempData["Error"]` flashes are rendered once by `_AdminLayout` (above the page header,
  not in a modal frame) for every admin view; views must not render them again.

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
