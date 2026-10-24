# Area 1: Data Tier

**Namespaces:**
- `WebWayCMS.Data.Models`
- `WebWayCMS.Data.DbContexts`
- `WebWayCMS.Data.EntityConfiguration` (one `IEntityTypeConfiguration<T>` per entity)
- `WebWayCMS.Data.Services`
- `WebWayCMS.Data.DesignTime` (`IDesignTimeDbContextFactory<CmsDbContext>`, for the EF tooling)
- `WebWayCMS.Data.Migrations` (auto-generated, do not edit)

**Depends on:** PostgreSQL/Npgsql, EF Core 10, ASP.NET Identity (`CmsDbContext`)
**Consumed by:** Content Domain Models, Admin CRUD Framework, CMS Bootstrap, Routing, Tests

---

## 1. Single Unified DbContext

The CMS uses a single EF Core `DbContext`, `CmsDbContext`, which inherits from `IdentityDbContext`
(non-generic — users are `IdentityUser`, roles `IdentityRole`). It covers the Identity tables, the
content node/version/change-set tables, and every content-type-specific table. All tables live in one
PostgreSQL database connected via `DefaultConnection`, and a single `__EFMigrationsHistory` table
tracks migration history.

The context declares **no `DbSet<>` properties at all**:

```csharp
public class CmsDbContext : IdentityDbContext
{
    public CmsDbContext(DbContextOptions<CmsDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
```

Entities are discovered through `IEntityTypeConfiguration<T>` classes in
`WebWayCMS.Data/Data/EntityConfiguration/` and reached at runtime through `context.Set<T>()`. Adding a
content type means adding a configuration class, not editing the context.

The shared helper in `ContentModelConfiguration` is `ConfigureContentLink<T>()`, an
`EntityTypeBuilder<T>` extension that wires the 1:1 shared-primary-key relationship every
`IVersionedContent` type needs:

```csharp
entity.HasKey(e => e.VersionId);
entity.HasOne(e => e.Version)
      .WithOne()
      .HasForeignKey<T>(e => e.VersionId)
      .OnDelete(DeleteBehavior.Cascade);
entity.Navigation(e => e.Version).AutoInclude();
```

---

## 2. `ContentNode` / `ContentVersion` / `ChangeSet` — identity split from version

Identity and version are **split**. A logical content item is one `ContentNode`; its mutable,
per-version data lives on one or more `ContentVersion` rows. Content types **compose** a
`ContentVersion` rather than inheriting — each implements `IVersionedContent` and is linked to its
version row by a shared-primary-key 1:1 relationship (`VersionId` is both PK and FK, and equals
`Version.Id`).

```csharp
public interface IVersionedContent
{
    Guid VersionId { get; set; }
    ContentVersion Version { get; set; }
}
```

### `ContentNode` — one row per logical item, never versioned

```csharp
public record ContentNode
{
    public Guid Id { get; set; }              // stable identity (replaces the old MasterId)
    public string ContentTypeKey { get; set; } // "pages", "articles", "contentzones", ...
    public Guid? ParentNodeId { get; set; }
    public Guid? SiteId { get; set; }          // multi-site seam; null = default site
    public DateTime CreatedUtc { get; set; }
    public Guid? CreatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsArchived { get; set; }
    public bool IsHidden { get; set; }
}
```

Every cross-entity foreign key points at `ContentNode.Id` (never at a version row).

### `ContentVersion` — one row per version, per variant

```csharp
public enum ContentVersionState { Draft = 0, InReview = 1, Approved = 2, Published = 3, Archived = 4 }

public record ContentVersion
{
    public Guid Id { get; set; }
    public Guid NodeId { get; set; }
    public ContentNode Node { get; set; } = null!;
    public int VersionNumber { get; set; }

    // Variant axes — NON-NULLABLE with "" sentinels (see PostgreSQL note below)
    public string Culture { get; set; } = string.Empty;   // "" = invariant
    public string Segment { get; set; } = string.Empty;   // "" = default

    public ContentVersionState State { get; set; }
    public bool IsCurrentDraft { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;

    public Guid? CreatedBy { get; set; }
    public DateTime CreatedUtc { get; set; }
    public Guid? PublishedBy { get; set; }
    public DateTime? PublishedUtc { get; set; }
    public DateTime? PublishStartUtc { get; set; }   // scheduling seam — NOT filtered yet
    public DateTime? PublishEndUtc { get; set; }

    public string? ChangeNote { get; set; }
    public Guid ChangeSetId { get; set; }
    public List<CustomField> CustomFields { get; set; } = new();  // JSONB
}
```

**Why `Culture`/`Segment` are non-nullable `""` sentinels:** PostgreSQL treats NULLs as distinct in a
unique index (`NULLS DISTINCT` is the default), so two invariant rows with `Culture = NULL` would not
collide and the invariant indexes below would silently fail to enforce anything.

### `ChangeSet` — groups versions written by one operation

```csharp
public enum ChangeSetKind { Save = 0, Publish = 1, Unpublish = 2, Restore = 3, Delete = 4, Discard = 5 }

public record ChangeSet
{
    public Guid Id { get; set; }
    public DateTime CreatedUtc { get; set; }
    public Guid? CreatedBy { get; set; }
    public ChangeSetKind Kind { get; set; }
    public Guid? RootNodeId { get; set; }
    public string? Note { get; set; }
}
```

`IChangeSetScope` provides ambient scoping: `AdminCrudModel.SaveUpsertAsync` (and publish/restore)
opens a scope; every `IContentStore<T>` write inside stamps `ChangeSetId = scope.Current`. A version's
`ChangeSetId` records only the **last** operation that wrote it (a publish restamps it), so it is not a
history of a page set: that is recorded explicitly as page-set snapshots (below).

**Change notes.** Every version carries a `ChangeNote` saying what wrote it. The note is the ambient
`IChangeSetScope.UseChangeNote(note)` when one is open (it nests and restores the previous note, and opens
no change set), else the store's default — "Created", "Edited", or "Restored from version n". It is never
copied from the version the save started from.

### `PageSetSnapshot` — page-set history

One row per change to a page set: a page version plus every **active** zone item version in the page's
zone tree (nested zones included), as `PageSetSnapshotItem` rows (`ItemNodeId`, `ItemVersionId`,
denormalized `ZoneNodeId`/`Ordinal`), with `Kind`, `Note` and `StateHash` (a content hash, no version ids).
Tables `PageSetSnapshots` / `PageSetSnapshotItems` (items cascade with their snapshot; the FKs to
`ContentNodes`/`ContentVersions` are **restrict**). Written by `IPageHistoryRecorder`:

- **Draft** entries (`RecordDraftAsync`: the page's current draft + the active current drafts, descending
  only through active containers) after every page save/restore (`PageModel`), every zone item
  add/edit/settings/remove/move/reorder/restore (`ContentZoneService`, once per write after it committed,
  for every page whose tree holds the zone — `PageZoneTree.GetPagesForZoneAsync`; none for a global zone),
  and every revert/discard.
- **Published** entries (`RecordPublishedAsync`, via `IPageHistoryService.CaptureAsync`: the published page
  + the active published items) after `PageModel.PublishPageAsync` and `PublishZoneItemsAsync`.
- An entry whose `StateHash` equals the latest one (Draft: latest of either kind; Published: latest
  Published) is skipped. A failed write is logged and never fails the change.

The zone-tree queries (walk up, walk down, live/draft tree items, content-aware pending) live in
`PageZoneTree`, shared by `ContentZoneService`, `PageHistoryService` and the recorder so none depends on
another.

- **Versions are immutable** (every save mints one), so an entry always shows exactly what it captured.
- **Deletes.** A user deleting a single captured page or item version is refused by the handlers
  (`TryDeleteVersionAsync` / `TryDeleteChildVersionAsync`, message from `PageHistoryMessages`) — with an entry
  per change that covers most versions. Any hard delete inside the store (`DeleteAsync`,
  `DeleteVersionAsync` — page delete, zone delete) removes **whole** snapshots that reference the deleted
  versions first.
- **Revert** (`IPageHistoryService.RevertAsync`): one transaction, one `Restore` change set rooted at the
  page. The page and each captured item get the captured content and placement as **new drafts** (items
  already content-equal are skipped); every other item in the tree becomes an inactive draft (never
  deleted), moved past the zone's last ordinal if it would share a captured one. Guarded by a draft
  **state token** (`PageSetEquality.StateToken` over the current drafts' version numbers and content) and
  refused with `StaleStateMessage` when the page changed since. Metadata is normalized through a
  Core-supplied `RestorableMetadata` delegate (List→Single grandfathering). The revert is recorded as a
  Draft entry.
- **Discard** (`IPageHistoryService.DiscardUnpublishedChangesAsync`, the page editor's "Discard changes"):
  the revert mechanics with what is live as the target — edited items get their live content and placement
  back, never-live items (or live as removed) become inactive drafts, the page returns to its published
  fields. Nothing is deleted, it is recorded, so it can be undone; refused (`NotPublishedMessage`) when the
  page is not published.
- **Pending changes** (`GetPagesWithUnpublishedChangesAsync`) compare by content: the page draft vs. the
  live page, plus `PageZoneTree.GetPagesWithPendingZoneChangesAsync` (a current draft that differs from its
  published row, or an active one never live). Drafts equal to what is live are not pending.
- **Comparison** is by content (`PageSetEquality`), never by version id, so a restored identical copy
  reads as unchanged.

### The two state invariants

- **Exactly one `IsCurrentDraft = true` per (NodeId, Culture, Segment), always.**
- **At most one `State = Published` per (NodeId, Culture, Segment).**

A version can be both — that is the steady state for published content with no pending edit, and it
makes both reads a single-row index seek:

| Operation | Effect |
|---|---|
| Create | v0: `Draft`, `IsCurrentDraft = true` |
| Publish (no separate draft) | v0: `Published`, `IsCurrentDraft = true` |
| Edit a published item | v1: `Draft`, `IsCurrentDraft = true`; v0: `Published`, `IsCurrentDraft = false` |
| Publish v1 | v1: `Published`, `IsCurrentDraft = true`; v0: `Archived`, `IsCurrentDraft = false` |
| Unpublish (no separate draft) | published row → `Draft` (stays `IsCurrentDraft`) |
| Unpublish (separate draft exists) | published row → `Archived`; draft untouched |

These are enforced **in code** by `ContentStore<T>` as the primary mechanism (so the unit-test suite,
which runs on the EF InMemory provider, still validates them) and **by filtered unique indexes** as the
DB backstop:

- `UX_ContentVersion_PublishedVariant` — unique on `(NodeId, Culture, Segment)` where `"State" = 3`
- `UX_ContentVersion_DraftVariant` — unique on `(NodeId, Culture, Segment)` where `"IsCurrentDraft"`
- `UX_ContentVersion_Number` — unique on `(NodeId, Culture, Segment, VersionNumber)`

> **Testing caveat:** the InMemory provider enforces neither filtered unique indexes nor check
> constraints and ignores transactions, so the indexes themselves can only be verified against real
> PostgreSQL (via `./scripts/StartIntegrationHost.sh`), not unit tests. See the note in
> `tests/WebWayCMS.Data.Tests/TestContexts.cs`.

### `CustomField`

```csharp
public record CustomField
{
    public string FieldName { get; set; }
    public string TypeName { get; set; }
    public string Value { get; set; }
}
```

---

## 3. Built-in DTOs

Each implements `IVersionedContent` (`VersionId` + `Version`). Type-specific fields:

### `PageDTO`
- `ControllerName` — the selected page controller (page type), persisted here so Publish can write the route
- `ViewName` — optional Razor view override
- `ConfigurationJson` — JSON page config (type determined by the controller's `ConfigurationType`)

A page's URL lives on its `CMSRouteDTO`; routes are written on **Publish** (never Save).

### `ArticleListDTO`
Parent container for articles. No type-specific fields.

### `ArticleDTO`
`Body`, `AuthorName`, `Summary`, and `ArticleListNodeId` — a **real FK to `ContentNode.Id`** (replaces the soft `ArticleListMasterId`).

### `ContentBlockDTO`
`Content` (the reusable block body).

### `CMSRouteDTO` — **not versioned**
Plain entity, no `IVersionedContent`, no version history (matching `CMSRouteModel.SupportsVersionHistory => false` and the hard-delete-replace behaviour):
- `Id` — plain `Guid`
- `Pattern` — normalized URL pattern (unique index)
- `NavigationName` — human-readable link text for navigation widgets (nullable, max
  `CMSRouteDTO.NavigationNameMaxLength` = 256, which the EF configuration and the upsert view model both
  read). Seeded from the page title on the **first** publish only; after that whatever is on the existing
  row is carried forward verbatim — including a blank, which is how an admin takes a page out of the
  widgets. `RouteNavigationViewComponent` omits routes that have none
- `DefaultsJson`, `ConstraintsJson`, `DataTokensJson` — route metadata (`DataTokens` stores a widget route's `ParentPageNodeId`)
- `Order`, `OwningContentNodeId`, `OwningContentType`, `IsReserved`

Routes are written by Publish, never by Save, so a draft slug change does not touch the route table
until published.

### `WidgetRegistrationDTO` / `PageControllerRegistrationDTO`
Registration records (seeded at startup), each `IVersionedContent` with an `IsActive` type field and a
published/draft `Version.State`.

### `FormComponentRegistrationDTO`
Registration records (seeded at startup). **Not versioned** — a plain row keyed by `Id` with an
`IsActive` type field. Seeded and re-synced in place by `CmsFormComponentSeeder`, and edited in place
via `/wadmin/formcomponents`.

### `ContentSeedRecordDTO` — **not versioned**
The JSON content-seed ledger. **Not versioned** — a plain row keyed by `SeedId` (the stable Guid a
JSON item carries) mapping it to the CMS-generated `NodeId` it produced, plus the `ContentHash`
applied last time. Written by `JsonContentSeeder` and read back on every boot to decide whether an
item is unchanged (skip) or must be re-applied. See [Area 15](15-content-seeding.md).

### `ContentZoneDTO`
`Name`, `Description`. **There is no `Items` collection** — which item *versions* belong to a zone
depends on the read context, so items are resolved through `IContentZoneService.GetItemsAsync(zoneNodeId)`.

### `ContentZoneItemDTO`
- `ContentZoneNodeId` — FK to the zone's `ContentNode.Id`
- `Ordinal`, `ComponentName`, `ComponentPropertiesJson`, `IsActive`

### `ContentZoneAssignmentDTO`
Join record scoping a zone node to a page slot or a nested zone slot:
- `Id`, `SlotName`
- `ContentZoneNodeId` — FK to `ContentNode.Id`
- `ParentPageNodeId?` / `ParentItemNodeId?` — exactly one non-null (check constraint). A nested zone
  is owned by the zone *item* that renders it (e.g. a Layout widget), so it moves with the item.
  `ParentItemNodeId` is an FK to `ContentNode.Id` with **restrict** delete: the service deletes an item's
  nested zone tree explicitly before hard-deleting it, and a hard delete that skips that step fails
  instead of silently orphaning the nested zones, their items, and their routes. Unique filtered indexes on
  `(ParentPageNodeId, SlotName)` and `(ParentItemNodeId, SlotName)`.

---

## 4. Tables and Entity Configurations

`CmsDbContext` covers the Identity tables plus the content tables. Each CMS entity has exactly one
sealed configuration class in `WebWayCMS.Data/Data/EntityConfiguration/`:

| Configuration class | Table |
|---|---|
| *(inherited from `IdentityDbContext`)* | `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetRoleClaims`, `AspNetUserLogins`, `AspNetUserTokens` |
| `ContentNodeEntityConfiguration` | `ContentNodes` |
| `ContentVersionEntityConfiguration` | `ContentVersions` |
| `ChangeSetEntityConfiguration` | `ChangeSets` |
| `PageSetSnapshotEntityConfiguration` / `PageSetSnapshotItemEntityConfiguration` | `PageSetSnapshots` / `PageSetSnapshotItems` |
| `ArticleDTOEntityConfiguration` | `Articles` |
| `ArticleListDTOEntityConfiguration` | `ArticleLists` |
| `ContentBlockDTOEntityConfiguration` | `ContentBlocks` |
| `ContentZoneDTOEntityConfiguration` | `ContentZones` |
| `ContentZoneItemDTOEntityConfiguration` | `ContentZoneItems` |
| `ContentZoneAssignmentDTOEntityConfiguration` | `ContentZoneAssignments` |
| `PageDTOEntityConfiguration` | `Pages` |
| `CMSRouteDTOEntityConfiguration` | `CMSRoutes` |
| `WidgetRegistrationDTOEntityConfiguration` | `WidgetRegistrations` |
| `PageControllerRegistrationDTOEntityConfiguration` | `PageControllerRegistrations` |
| `FormComponentRegistrationDTOEntityConfiguration` | `FormComponentRegistrations` |
| `ContentSeedRecordDTOEntityConfiguration` | `ContentSeedRecords` |

Migrations live in `WebWayCMS.Data/Migrations/`; `./scripts/RebuildEFMigrations.sh` wipes and
regenerates a single `InitialCreate` (destructive, not additive). Because the schema is kept as one
regenerated `InitialCreate`, a regenerated migration carries a **new timestamp** — any database that
already applied the previous `InitialCreate` holds a stale `__EFMigrationsHistory` row and must be
rebuilt (or its history row updated by hand) before `Database.Migrate()` can run again; the rebuild
script is the supported path.

---

## 5. `IContentStore<T>` — the single write/read engine

`ContentStore<T>` is the single generic engine that replaced the old `ContentService<T>`,
`PageService`, and the zone CRUD. It is registered once per content type with its `contentTypeKey`,
and is read-context aware.

**Reads (read-context aware — the public rendering path):**

| Method | Behaviour |
|---|---|
| `GetAsync(nodeId)` | The single row selected by the read context for that node |
| `GetAllAsync()` | One row per node, at the read context |
| `GetBySlugAsync(slug)` | Row at the read context matching the slug |
| `GetChildrenAsync(parentNodeId)` | Rows whose `Node.ParentNodeId` matches |
| `GetRootsAsync()` | Rows whose `Node.ParentNodeId` is null |

Version selection lives in exactly one place, `ContentQueryExtensions.AtReadContext(ctx)`:

```csharp
q.Where(e => e.Version.Culture == ctx.Culture
          && e.Version.Segment == ctx.Segment
          && !e.Version.Node.IsDeleted
          && (ctx.Mode == ContentReadMode.Draft
                 ? e.Version.IsCurrentDraft
                 : e.Version.State == ContentVersionState.Published));
```

Both branches are single-row index seeks against the filtered unique indexes. (Scheduled-publishing
predicates are intentionally deferred — `PublishStartUtc`/`PublishEndUtc` ship as columns, unfiltered.)

A third mode, `ContentReadMode.Snapshot`, renders a page-history entry: when the context carries a
`ContentReadSnapshot(GovernedNodeIds, VersionIds)` (the `IContentReadContext.Snapshot` default member;
null everywhere but an admin page-history preview), a governed node reads only a version in
`VersionIds` — or nothing, which is how an item added after the entry is hidden — and every other node
reads `Published`. `IPageHistoryService.GetReadSnapshotAsync` governs the page plus every item that
has sat in its zone tree. Snapshot mode without a snapshot falls back to `Published`.

**Reads (version-explicit / admin):**

| Method | Behaviour |
|---|---|
| `GetVersionAsync(versionId)` | Exact version row |
| `GetAllVersionsAsync(nodeId)` | All versions newest-first |
| `GetCurrentDraftAsync(nodeId)` | The `IsCurrentDraft` row (invariant variant) — **excludes `Node.IsDeleted`** (matching `AtReadContext`), so soft-deleted items never leak into admin lists |
| `GetCurrentDraftBySlugAsync(slug)` | The `IsCurrentDraft` row by slug (invariant variant, `IsDeleted` excluded); returns null for blank slugs |
| `GetAllCurrentDraftsAsync()` | Every current-draft row (admin lists) — **excludes `Node.IsDeleted`** |
| `GetCurrentDraftChildrenAsync(parentNodeId)` | Current-draft rows whose `Node.ParentNodeId` matches (null ⇒ root drafts) — **excludes `Node.IsDeleted`** — the sibling-scoped query behind the page slug-collision check |
| `GetPublishedNodeIdsAsync()` | The set of node ids that have a `Published` version at the default variant — **excludes `Node.IsDeleted`** — the "has a published version" signal used by the page nav |

**Writes** (return `ContentWriteResult(bool Success, string? ErrorMessage, Guid VersionId)`):

| Method | Behaviour |
|---|---|
| `SaveDraftAsync(entity, expectedVersionNumber)` | New node ⇒ v0 draft. Existing ⇒ **always** mints `VersionNumber+1` as the new current draft and clears the previous one's `IsCurrentDraft` (published or not) — no version is ever rewritten, so every save is restorable. A tracked incoming entity is detached before it is re-keyed. `VersionNumber` comes from `MAX` on the DB, never the client. `expectedVersionNumber` (a hidden form field) is compared against the current draft's number; mismatch returns the friendly stale-version message. The new version's change note is the ambient one, else "Created"/"Edited". |
| `PublishAsync(nodeId)` | Promotes the current draft to `Published`; archives any prior published version. |
| `UnpublishAsync(nodeId)` | Published current → `Draft`; or, with a separate draft, archives the published row and leaves the draft. |
| `RestoreAsync(versionId)` | Clones the historical type-table row + `ContentVersion` into a new draft at the DTO level (no ViewModel round-trip, so no field is dropped). |
| `DeleteAsync(nodeId, softDelete)` | Soft ⇒ `Node.IsDeleted = true`; hard ⇒ removes type rows, versions, and node, and every page-history snapshot that captured one of those versions. |
| `DeleteVersionAsync(versionId)` | Removes a single version row + its type row — **but refuses to delete the highest-numbered version** for the node, matching the admin UI which hides Delete on the latest version. A node's single version is its max, so it can never be removed this way. Snapshots that captured the version go with it (user-initiated deletes of captured versions are refused earlier, by the handlers). |

**Concurrency:** the unique index `UX_ContentVersion_Number` is the backstop; `ContentStore<T>` catches
`DbUpdateException` and maps it to the same stale-version message, so a true race degrades to the
friendly error rather than corruption. On a failed `SaveDraftAsync`/`RestoreAsync`, the abandoned
demotion (the old version row, captured before the swap) and insert are detached from the scoped context (after the transaction's rollback) so no
later `SaveChanges` in the same request replays them. In both, the demotion and the minted version are written
as two `SaveChanges` calls inside one transaction — the filtered `UX_ContentVersion_DraftVariant`
index is checked per-statement and EF Core does not order INSERTs before UPDATEs within one
`SaveChanges` — and the transaction is skipped when the caller already opened one (Npgsql has no
nested transactions).

**Slug generation:** `SlugNormalizer.Normalize` (in `WebWayCMS.Data/Slugs/`) is the single source of truth
for slug shape, called from both `ContentStore<T>.NormalizeSlug` (the write path) and `PageModel`'s
slug-collision checks (the comparison path), so the two always agree on what a slug looks like. It
**slugifies** rather than URL-encodes — lowercase, whitespace/punctuation collapsed to single hyphens,
nothing outside `[a-z0-9-]` — whether the slug is supplied explicitly or derived from the title.
Accented Latin is folded to ASCII via `NormalizationForm.FormD` (`Café Ünïcode` → `cafe-unicode`);
characters with no decomposition (`ø`, `ł`, `ß`) fall through to a hyphen. When slugification yields
nothing but the source was not blank (a purely non-Latin or punctuation-only title), it falls back to
`Uri.EscapeDataString(source)`, so a `日本語ページ` page keeps a routable percent-encoded slug instead of an
unpublishable empty one. `Normalize` is idempotent — normalizing an already-clean slug returns it
unchanged — which is the property that lets the same call sit safely on both the write and comparison
paths.

Because the output is already URL-safe, the encode-on-write / `WebUtility.UrlDecode`-on-read round-trip
is a no-op for newly written slugs. The route pattern is derived on publish by `UrlDecode`ing the stored
slug (so a non-Latin slug stored percent-encoded resolves to a raw route that matches ASP.NET's decoded
incoming path — `日本語ページ` serves at `/日本語ページ`, which is what the server sees for either the raw or
percent-encoded request). **One consequence:** a legacy page whose slug predates the slugify change keeps
its existing decoded route on a bare republish, but migrates to the slugified URL the moment its draft is
re-saved (e.g. `/My%20Page` → `/my-page`), because the slug is normalized at write time; with slugify in
place there is no longer any way to keep a legacy spaced URL once it is edited.

---

## 6. `ICMSRouteService` and the registration services

`CMSRouteService` wraps plain `CMSRouteDTO` rows and `ICMSRouteRegistry` (a 60s cached list of active
routes, invalidated on every write).

| Method | Behaviour |
|---|---|
| `MatchRouteAsync(path)` | Normalizes the path, walks active routes by `Order`, skips `IsReserved`, returns the first pattern match + route values |
| `GetActiveRoutesAsync()` / `GetAllRoutesAsync()` | All routes ordered by `Order` then `Pattern.Length` |
| `GetByOwningContentAsync(nodeId)` | Routes owned by a content node |
| `GetByIdAsync(id)` | Exact row |
| `IsPatternAvailableAsync(pattern, excludeNodeId?, excludeRouteId?)` | `true` if no route occupies that pattern (optional exclusions for edit-in-place; NULL-owner rows are handled explicitly, not dropped by SQL three-valued logic) |
| `UpsertAsync(route)` | Keys the replace on `(OwningContentNodeId, Pattern)`; returns `(bool Success, string? ErrorMessage, CMSRouteDTO? Route)` — a foreign owner on the same pattern is a collision (failure), never a silent steal |
| `DeleteAsync(id)` / `DeleteByOwningContentAsync(nodeId)` | Hard-delete route(s) |

Three registration services back the registries (form components are unversioned and expose a full
CRUD surface — `UpsertAsync`/`DeleteAsync` — alongside the reads):

| Service | Filter |
|---|---|
| `IWidgetRegistrationService` / `IPageControllerRegistrationService` | `IsActive && Version.State == Published && !Version.Node.IsDeleted`, ordered by Category → Order → DisplayName |
| `IFormComponentRegistrationService` | `IsActive` (unversioned row), ordered by Category → Order → DisplayName |

---

## 7. `IContentZoneService`

`ContentZoneService` handles zone assignments, item resolution, and item writes (zones and items are
versioned through `IContentStore<T>`). Zone and item **reads** are read-context aware.

**Draft rule.** Items in a zone that belongs to a page — directly, or through nested zones
(`GetParentPageNodeForZoneAsync` resolves to a page) — are **drafts until the page is published**:
`AddItemAsync`, `UpdateItemAsync` and `ReorderItemsAsync` save drafts without publishing, and
`RemoveItemAsync` saves an `IsActive=false` version — never a delete, published or not, so the item's
history stays and a restore or page revert brings it back (a removed container keeps its nested items,
hidden). Items in global/name-based zones have no page to publish them, so they still auto-publish on write
(a global removal is published at once). Each write sets the versions' change notes and records one page
history entry for every page whose tree holds the zone (see `PageSetSnapshot`). The rule lives in the service, so it applies to every caller (zone editor API,
admin child CRUD, MCP).

**Page draft lifecycle:**

| Method | Behaviour |
|---|---|
| `GetZoneTreeNodeIdsForPageAsync(pageNodeId)` | Every zone under a page: its assigned zones, then the zones owned by their items (matched on any item version, so a moved item's draft and published zones both count), breadth-first (visited set guards cycles). Each level queries only the current frontier's zones, so the cost follows the page's tree, not the whole zone table |
| `GetPendingItemNodeIdsAsync(zoneNodeIds)` | Item nodes in those zones whose current draft is not `Published` (never published, or edited since) |
| `GetPagesWithPendingZoneChangesAsync(pageNodeIds)` | Batched: which of the pages have a change anywhere in their zone tree, **by content** (`PageZoneTree`): a current draft differing from its published row, or an active item never live — a draft equal to what is live is not a change. One breadth-first walk covers all the pages at once |
| `PublishPageZonesAsync(pageNodeId)` | Publishes every pending item under the page; returns `PageZonePublishResult(Published, Errors)` so the caller (`PageModel`) can register widget routes for what went live. Joins the caller's ambient change set |
| `PublishPageZoneItemsAsync(pageNodeId, itemNodeIds)` | Publishes only the listed items among the page's pending drafts (others stay pending; a listed item that isn't pending is ignored). Same result shape, plus `FailedItemNodeIds`. Backs `IPageModel.PublishZoneItemsAsync`, which the content seeder uses on an already-live page instead of republishing it |

| Method | Behaviour |
|---|---|
| `GetItemsAsync(zoneNodeId)` | The zone's active items at the read context, ordered by `Ordinal` |
| `GetZoneByNodeAsync(nodeId)` / `GetZoneByNameAsync(name)` | Zone at the read context |
| `GetByPageSlotAsync` / `GetOrCreateByPageSlotAsync` | Page-slot assignment (transactional create) |
| `GetByItemSlotAsync` / `GetOrCreateByItemSlotAsync` | Nested zone assignment, keyed by the owning item's node id + slot |
| `GetOrCreateByNameAsync(name)` | Global zone by name |
| `GetAllAssignmentsForPageAsync` / `GetAllByPageAsync` / `GetAllByParentZoneAsync` | Assignment/queries (a nested zone's parent zone is the zone its owning item's current draft sits in) |
| `GetZoneNodeIdsWithChildrenAsync` / `GetAssignmentCountsByNodeIdAsync` | Admin indicators |
| `GetParentPageNodeForZoneAsync(zoneNodeId)` | Walks zone → assignment → page, or → owning item → that item's zone …, to the owning page node (null for global, orphaned, or cyclic zones) |
| `GetItemByNodeIdAsync` / `AddItemAsync` / `UpdateItemAsync` / `RemoveItemAsync` | Item CRUD (node-keyed). `UpdateItemAsync(item, expectedVersionNumber)` and the removal save reject the write with the shared stale-version message when the item changed since the caller read it; a null/whitespace `MetadataJson` never reaches the column as NULL (an omitted update keeps the stored document, a blank one stores `"{}"`). The metadata API normalizes a blank body as `{}` against the stored document, so clearing keeps a grandfathered List. |
| `ReorderItemsAsync(zoneNodeId, itemNodeIdsInOrder)` | Validates up front that every id resolves and belongs to `zoneNodeId`, then writes new versions of the reordered items in one ChangeSet (no in-place `Ordinal` mutation). The client sends only the active items: every other current draft in the zone (removed items above all) keeps its place and the requested items fill the positions they held, so the whole zone is renumbered and no two items share an ordinal. A repeated id fails the reorder. Each save carries the version the item was read with, so a concurrent edit to a sibling fails the whole reorder instead of being silently reverted. The saves run in one DB transaction (a failure rolls back and clears the change tracker), so a missing/foreign id or a failed save returns `false` with no partial reorder. |
| `MoveItemAsync(itemNodeId, targetZoneNodeId, position, expectedVersionNumber)` | Moves an item to a 0-based (clamped) position among the target's active items, in one ChangeSet. Removed (inactive) items in the target are renumbered in place too, so no two items share an ordinal if a removal is later discarded, and a cross-zone move renumbers the source zone's remaining items. `expectedVersionNumber` (the moved item's version as the client rendered it) rejects the move with `StaleVersionMessage` when the widget changed since the page loaded; sibling saves carry their read versions, so a concurrent edit fails the move (also `StaleVersionMessage`; the API maps it to 409). Both zones' renumbering commits in one DB transaction, so a failed source renumber never leaves the widget half-moved. A missing or inactive item returns `IContentZoneService.ItemNotFoundMessage` (the API maps it to 404). Same zone = reorder; another zone only within the same page's zone tree, never into the item's own nested zones. Returns `(Success, ErrorMessage)`. A draft until the page is published (global zones auto-publish). See [04 → Moving Items](04-content-zone-framework.md#8a-moving-items-drag-and-drop). |
| `RestoreItemVersionAsync(versionId, metadataJson, expectedVersionNumber)` | One-step restore of an item version: its content, metadata (`metadataJson` when the caller normalized it, else the version's own), active state, **zone and ordinal**. It is inserted back into its old zone before the first sibling at or past its old ordinal, siblings renumbered (and the source zone too on a cross-zone restore), sharing `MoveItemAsync`'s placement code, in one transaction under a `Restore` change set. Refused with `RestoreZoneMissingMessage` when the old zone no longer exists, now belongs to another page (or is not the current zone of a global item), or sits inside the item itself; `StaleVersionMessage` on an expected-version mismatch. A draft until the page publishes (a global zone's item publishes now). |
| `UpdateItemAsync(item, SlotRemapPlan, expected)` / `RestoreItemVersionAsync(versionId, metadataJson, expected, SlotRemapPlan)` | The same writes for a container whose view changed, all in one transaction: the version-checked container save (or the restore's placement) first, then each `SlotMove`'s target zone is got or created (`GetOrCreateByItemSlotAsync`, which joins the open transaction; a missing source or one that is its own target is skipped), so a stale or failed save creates no zones; then each target zone's new order (existing items, then the sources' current drafts — removed ones too — noted "Moved to {slot} (layout changed)") commit in one transaction, published at once in a global zone. One page-history entry with the plan's `HistoryNote` (prefixed "Restored X from version N; " for a restore). The plan comes from Core (`SlotRemapPlanner`, see [04 → Changing a container's view](04-content-zone-framework.md#changing-a-containers-view)). `ContentZoneService.MergeUpdate` is the shared "what an update saves" rule. |
| `GetOwnedSlotsWithItemsAsync(itemNodeId)` | Slot names of the item's own zones holding any current draft (removed ones count) — the remap's fast-path check |
| `GetDescendantItemNodeIdsAsync(containerItemNodeId)` | Every item with any version in a zone nested under the container, at any depth (breadth-first, cycle-guarded, deleted nodes excluded) — the combined history timeline |
| `GetItemOwnedZonesForPageAsync(pageNodeId)` | `ItemOwnedZone`s (zone, slot, owner draft) owned by the page's active widgets and holding an active current draft — the Unplaced panel's candidates |
| `DeleteZoneAsync(zoneNodeId)` | Removes the zone's assignments, its items (each item's nested zone tree first, then its widget routes — `CMSRouteDTO` rows keyed to the item node — so nothing dangles) and the zone. An item whose published version lives in another zone (dragged in) is sent back (if its current draft sits here, a new version restoring the published one — "Returned to its published zone") and only its versions in this zone are dropped. If sending it back or dropping one of those versions fails, the delete stops and returns `false` (logged) instead of deleting a zone a current draft still points at. Nested zones hang off items, so deleting a zone's items recurses into them (the zone's own assignment rows go first, so a cycle can't lead back); nested zones still referenced elsewhere survive. |
| `DeletePageZonesAsync(pageNodeId)` | Removes the page's own assignment rows, then for each distinct zone node re-queries the remaining assignment count and calls `DeleteZoneAsync` when it is `0`. A zone node is deleted exactly when nothing references it any more — whether it is shared with another page or parent, or assigned to two of the same page's slots. This is what stops page deletion from orphaning `ContentZoneAssignments` rows or zone nodes. |

**Breaking API note (for package hosts):** this change removes `IContentStore<T>.DiscardDraftAsync`
and `IContentZoneService.DiscardPageZoneDraftsAsync` (a "discard" is now a page-history revert to what
is live — see `IPageHistoryService.DiscardUnpublishedChangesAsync`), and adds members to
`IContentZoneService` (`UpdateItemAsync`/`RestoreItemVersionAsync` overloads taking a `SlotRemapPlan`,
plus `GetOwnedSlotsWithItemsAsync`, `GetDescendantItemNodeIdsAsync`, `GetItemOwnedZonesForPageAsync`).
A host that *implements* these interfaces directly must add the new members; hosts that only *consume*
them recompile unchanged. This warrants a package major-version bump on the next release.

---

## 8. How to Add a New Content Type's Data Layer

1. **Create a DTO** in `WebWayCMS.Data/Data/Models/` implementing `IVersionedContent`:
   ```csharp
   public record MyThingDTO : IVersionedContent
   {
       public Guid VersionId { get; set; }
       public ContentVersion Version { get; set; } = new();
       public string Body { get; set; } = string.Empty;
   }
   ```

2. **Add an entity configuration class** in `WebWayCMS.Data/Data/EntityConfiguration/`:
   ```csharp
   public sealed class MyThingDTOEntityConfiguration : IEntityTypeConfiguration<MyThingDTO>
   {
       public void Configure(EntityTypeBuilder<MyThingDTO> entity)
       {
           entity.ConfigureContentLink();          // shared PK/FK into ContentVersions
           entity.Property(e => e.Body).IsRequired();
           entity.ToTable("MyThings");
       }
   }
   ```
   `CmsDbContext` calls `ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly())` and then
   applies any injected `ICmsModelExtension` instances in registration order. A package host can
   therefore add its own table by contributing its `IEntityTypeConfiguration<T>` assembly via
   `AddWebWayCms(config, cms => cms.AddApplicationAssembly(assembly))` — no CMS source edit. See
   [14-host-extensibility](14-host-extensibility.md).

3. **Add a migration** (or run `./scripts/RebuildEFMigrations.sh` to regenerate).

4. **Register the store in DI** (`CmsRenderingRegistration.AddContentServices`):
   ```csharp
   AddContentStore<MyThingDTO>(services, "mythings");
   ```

Migrations are applied automatically at startup via `app.UseWebWayCms()` (or `UseWebWayCmsRendering()`).

---

*See also:* [docs/content-system.md](../content-system.md) for the full step-by-step content type creation guide including models, admin views, and mappings.
