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
public enum ChangeSetKind { Save = 0, Publish = 1, Unpublish = 2, Restore = 3, Delete = 4 }

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
opens a scope; every `IContentStore<T>` write inside stamps `ChangeSetId = scope.Current`, so a
composite save reads back as one history entry.

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
- `DefaultsJson`, `ConstraintsJson`, `DataTokensJson` — route metadata (`DataTokens` stores a widget route's `ParentPageNodeId`)
- `Order`, `OwningContentNodeId`, `OwningContentType`, `IsReserved`

Routes are written by Publish, never by Save, so a draft slug change does not touch the route table
until published.

### `WidgetRegistrationDTO` / `PageControllerRegistrationDTO` / `FormComponentRegistrationDTO`
Registration records (seeded at startup), each `IVersionedContent` with an `IsActive` type field and a
published/draft `Version.State`.

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
- `ParentPageNodeId?` / `ParentZoneNodeId?` — exactly one non-null (check constraint)

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

Migrations live in `WebWayCMS.Data/Migrations/`; `./scripts/RebuildEFMigrations.sh` wipes and
regenerates a single `InitialCreate` (destructive, not additive).

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
| `SaveDraftAsync(entity, expectedVersionNumber)` | New node ⇒ v0 draft. Existing ⇒ if current draft is `Published`, mint `VersionNumber+1` as a new draft and clear the published row's `IsCurrentDraft`; if already `Draft`, update in place. `VersionNumber` comes from `MAX` on the DB, never the client. `expectedVersionNumber` (a hidden form field) is compared against the current draft's number; mismatch returns the friendly stale-version message. |
| `PublishAsync(nodeId)` | Promotes the current draft to `Published`; archives any prior published version. |
| `UnpublishAsync(nodeId)` | Published current → `Draft`; or, with a separate draft, archives the published row and leaves the draft. |
| `RestoreAsync(versionId)` | Clones the historical type-table row + `ContentVersion` into a new draft at the DTO level (no ViewModel round-trip, so no field is dropped). |
| `DeleteAsync(nodeId, softDelete)` | Soft ⇒ `Node.IsDeleted = true`; hard ⇒ removes type rows, versions, and node. |
| `DeleteVersionAsync(versionId)` | Removes a single version row + its type row. |

**Concurrency:** the unique index `UX_ContentVersion_Number` is the backstop; `ContentStore<T>` catches
`DbUpdateException` and maps it to the same stale-version message, so a true race degrades to the
friendly error rather than corruption.

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

Three read-only services back the registries:

| Service | Filter |
|---|---|
| `IWidgetRegistrationService` / `IPageControllerRegistrationService` / `IFormComponentRegistrationService` | `IsActive && Version.State == Published && !Version.Node.IsDeleted`, ordered by Category → Order → DisplayName |

---

## 7. `IContentZoneService`

`ContentZoneService` handles zone assignments, item resolution, and item writes (zones and items are
versioned through `IContentStore<T>`). Zone and item **reads** are read-context aware; zone **items**
auto-publish on write (there is no separate publish surface for items), which preserves the inline
editor's immediately-visible behaviour while still producing version history.

| Method | Behaviour |
|---|---|
| `GetItemsAsync(zoneNodeId)` | The zone's active items at the read context, ordered by `Ordinal` |
| `GetZoneByNodeAsync(nodeId)` / `GetZoneByNameAsync(name)` | Zone at the read context |
| `GetByPageSlotAsync` / `GetOrCreateByPageSlotAsync` | Page-slot assignment (transactional create) |
| `GetByZoneSlotAsync` / `GetOrCreateByZoneSlotAsync` | Nested zone-slot assignment |
| `GetOrCreateByNameAsync(name)` | Global zone by name |
| `GetAllAssignmentsForPageAsync` / `GetAllByPageAsync` / `GetAllByParentZoneAsync` | Assignment/queries |
| `GetZoneNodeIdsWithChildrenAsync` / `GetAssignmentCountsByNodeIdAsync` | Admin indicators |
| `GetParentPageNodeForZoneAsync(zoneNodeId)` | Walks assignments to the owning page node |
| `GetItemByNodeIdAsync` / `AddItemAsync` / `UpdateItemAsync` / `RemoveItemAsync` | Item CRUD (node-keyed) |
| `ReorderItemsAsync(zoneNodeId, itemNodeIdsInOrder)` | Writes new versions of the reordered items in one ChangeSet (no in-place `Ordinal` mutation) |
| `DeleteZoneAsync(zoneNodeId)` | Removes assignments + items + the zone |

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
   `CmsDbContext` calls `ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly())`, so it scans
   **only `WebWayCMS.Data`** — a content type with its own table must live in this assembly.

3. **Add a migration** (or run `./scripts/RebuildEFMigrations.sh` to regenerate).

4. **Register the store in DI** (`CmsRenderingRegistration.AddContentServices`):
   ```csharp
   AddContentStore<MyThingDTO>(services, "mythings");
   ```

Migrations are applied automatically at startup via `app.UseWebWayCms()` (or `UseWebWayCmsRendering()`).

---

*See also:* [docs/content-system.md](../content-system.md) for the full step-by-step content type creation guide including models, admin views, and mappings.
