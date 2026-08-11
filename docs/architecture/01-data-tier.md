# Area 1: Data Tier

**Namespaces:**
- `WebWayCMS.Data.Models`
- `WebWayCMS.Data.DbContexts`
- `WebWayCMS.Data.EntityConfiguration` (one `IEntityTypeConfiguration<T>` per entity)
- `WebWayCMS.Data.Services`
- `WebWayCMS.Data.DesignTime` (`IDesignTimeDbContextFactory<CmsDbContext>`, for the EF tooling)
- `WebWayCMS.Data.Migrations` (auto-generated, do not edit)

**Depends on:** PostgreSQL/Npgsql, EF Core 10, ASP.NET Identity (`CmsDbContext`)
**Consumed by:** Content Domain Models, Admin CRUD Framework, CMS Bootstrap, Tests

---

## 1. Single Unified DbContext

The CMS uses a single EF Core `DbContext`, `CmsDbContext`, which inherits from `IdentityDbContext`
(non-generic — users are `IdentityUser`, roles `IdentityRole`). It covers the Identity tables, the
shared `Content` table, and every content-type-specific table. All tables live in one PostgreSQL
database connected via `DefaultConnection`, and a single `__EFMigrationsHistory` table tracks the
migration history.

The context itself is deliberately tiny and **declares no `DbSet<>` properties at all**:

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
`WebWayCMS.Data/Data/EntityConfiguration/` — one sealed `{Name}DTOEntityConfiguration` per entity —
and reached at runtime through `context.Set<T>()`. Adding a content type therefore means adding a
configuration class, not editing the context.

The single shared helper that remains in `ContentModelConfiguration` is
`ConfigureContentLink<T>()`, an `EntityTypeBuilder<T>` extension that wires the 1:1 shared-primary-key
relationship every `IContent` type needs:

```csharp
entity.HasKey(e => e.ContentId);
entity.HasOne(e => e.ContentMeta)
      .WithOne()
      .HasForeignKey<T>(e => e.ContentId)
      .OnDelete(DeleteBehavior.Cascade);
entity.Navigation(e => e.ContentMeta).AutoInclude();
```

---

## 2. `ContentDTO` / `IContent` — Fields and Semantics

The shared fields are a concrete `ContentDTO` (persisted to the single `Content` table). Content
types **compose** it rather than inheriting — each implements `IContent` and is linked to its
`ContentDTO` row by a shared-primary-key 1:1 relationship (`ContentId` is both PK and FK, and equals
`ContentMeta.Id`). Shared fields are accessed via `dto.ContentMeta.X`.

```csharp
public interface IContent
{
    Guid ContentId { get; set; }       // shared PK / FK to Content
    ContentDTO ContentMeta { get; set; }
}

public record ContentDTO
{
    public Guid Id { get; set; }          // Unique per row/version
    public Guid MasterId { get; set; }    // Stable identity across versions
    public int Version { get; set; }      // Monotonically increasing per MasterId

    public string Slug { get; set; }      // URL-friendly identifier; auto-generated from Title if empty
    public string Title { get; set; }

    public Guid CreatedBy { get; set; }
    public Guid LastModifiedBy { get; set; }

    public DateTime CreationDate { get; set; }
    public DateTime ModificationDate { get; set; }
    public DateTime PublicationDate { get; set; }
    public DateTime? PublicationEndDate { get; set; }

    public bool IsPublished { get; set; }
    public bool IsArchived { get; set; }
    public bool IsHidden { get; set; }
    public bool IsDeleted { get; set; }   // Soft-delete flag

    public Guid? ParentMasterId { get; set; }  // FK to parent's MasterId (child resources)

    public List<CustomField> CustomFields { get; set; } = new();  // JSONB flexible fields
}
```

**Versioning semantics:**
- `ContentMeta.Id` is a new `Guid` on every save (and `ContentId` tracks it). Never reused.
- `MasterId` is set to the original `Id` on first insert, then remains constant. Use `MasterId` as the stable reference to a logical content item.
- `Version` starts at 0 and increments on every update. The row with the highest `Version` for a given `MasterId` is the current version.

**Publishing flags:**
- `IsPublished` — visible to public. When a new version is saved with `IsPublished = true`, all prior versions for the same `MasterId` have `IsPublished` set to `false`.
- `IsArchived` — content is retained but hidden from normal queries (application-level convention; not enforced by the service).
- `IsHidden` — content exists but should not appear in listings (application-level convention).
- `IsDeleted` — soft delete marker. `GetAllAsync` and the route/registry queries exclude soft-deleted records.

**`CustomFields`:** A `List<CustomField>` stored as JSONB. Provides a key-value extension point without schema migrations.

---

## 3. Built-in DTOs

### `PageDTO`
Implements `IContent`. Type-specific fields beyond the shared `ContentMeta`:
- `ViewName` — optional override for the Razor view name (the seeded `/admin` page uses `"Dashboard"`)
- `ConfigurationJson` — JSON-serialized page config object (type determined by the controller's `ConfigurationType`)

There is **no `Route` or `ControllerName` here**. A page's URL and dispatch controller live on its
`CMSRouteDTO` row; the URL pattern is derived from `ContentMeta.Slug` on save. See
[03-page-routing](03-page-routing.md).

### `ArticleListDTO`
Parent container for articles. Implements `IContent` with no type-specific fields beyond `ContentMeta`.

### `ArticleDTO`
Implements `IContent`. Adds `Body`, `AuthorName`, `Summary`, and `ArticleListMasterId` (references the owning `ArticleListDTO`'s `MasterId`).

### `ContentBlockDTO`
Implements `IContent`. Stores reusable content blocks (adds `Content`, the block body).

### `CMSRouteDTO`
Implements `IContent`. One row per CMS URL — table `CMSRoutes`, with a **unique index on `Pattern`**:
- `Pattern` — normalized URL pattern (lowercase, leading slash, no trailing slash), e.g. `/about/team` or `/blog/{slug}`
- `DefaultsJson` — route defaults; a `"controller"` key is required, `"action"` defaults to `"Index"`
- `ConstraintsJson`, `DataTokensJson` — route constraints and data tokens. `DataTokens` is where a page route stores its `ConfigurationJson` and where a widget route stores its `ParentPageMasterId`
- `Order` — match precedence; lower wins
- `OwningContentMasterId` / `OwningContentType` — what created this route (`"Page"`, `"ArticleWidget"`, `"CodeBased"`)
- `IsReserved` — when `true`, the pattern is never matched but still blocks reuse

### `WidgetRegistrationDTO`
Implements `IContent`. One row per available widget — table `WidgetRegistrations`, `ComponentName`
unique. Fields: `ComponentName`, `DisplayName`, `Description`, `Category` (default `"General"`),
`IconClass`, `Order`, `ConfigurationTypeName`, `PropertyDefinitionsJson` (serialized
`List<FormPropertyInfo>`), `IsActive`. Seeded from `[ContentZoneComponent]` at startup, then read
through `IWidgetRegistry`.

### `PageControllerRegistrationDTO`
Implements `IContent`. One row per available page type — table `PageControllerRegistrations`,
`ControllerName` unique. Same shape as `WidgetRegistrationDTO` plus `ControllerTypeName`. Seeded
from `[PageController]` at startup, then read through `IPageControllerRegistry`.

### `ContentZoneDTO`
Implements `IContent`. Key fields:
- `Name` — slot identifier used for global lookup
- `Description`
- `Items` — navigation property: `List<ContentZoneItemDTO>` (loaded via EF Include)

### `ContentZoneItemDTO`
Implements `IContent`. Versioned through its own `ContentMeta` (`Id`/`MasterId`/`Version`). Key fields:
- `ContentZoneId` — FK to the owning `ContentZoneDTO` (its `ContentId`)
- `ComponentName` — name of the `[ContentZoneComponent]` ViewComponent to render
- `ComponentPropertiesJson` — JSON-serialized widget configuration
- `Ordinal` — display order within the zone
- `IsActive` — whether this item is currently visible

### `ContentZoneAssignmentDTO`
Join record scoping a zone to a page slot or a nested zone slot:
- `Id` — `Guid`
- `SlotName` — string slot name, e.g. `"Main"`, `"Sidebar"`
- `ContentZoneId` — references `ContentZoneDTO.MasterId`
- `ParentPageMasterId?` — set for page-scoped assignments
- `ParentZoneId?` — set for nested zone assignments; exactly one of these two is non-null

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

## 4. Tables and Entity Configurations

`CmsDbContext` covers 18 tables. Each CMS entity has exactly one sealed configuration class in
`WebWayCMS.Data/Data/EntityConfiguration/`:

| Configuration class | Table |
|---|---|
| *(inherited from `IdentityDbContext`)* | `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetRoleClaims`, `AspNetUserLogins`, `AspNetUserTokens` |
| `ContentDTOEntityConfiguration` | `Content` (shared by all content types) |
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

Migrations live in a single flat folder, `WebWayCMS.Data/Migrations/`, with one
`InitialCreate` migration plus the model snapshot. `./scripts/RebuildEFMigrations.sh` wipes and
regenerates it (destructive — it is not an additive migration workflow).

Design-time scaffolding uses `CmsDbContextFactory`; its connection string comes from
`WEBWAYCMS_DESIGNTIME_CONNECTION`, defaulting to a local `webwaycms_designtime` database.

---

## 5. `IContentService<T>` — Full Method Semantics

`ContentService<T>` is a sealed generic implementation registered once per content type. All queries use `AsNoTracking()` for reads. The "latest version only" query pattern is:

```csharp
.Where(e => !_set.Any(e2 => e2.MasterId == e.MasterId && e2.Version > e.Version))
```

This is an existence-based filter: exclude any row where a newer version (higher `Version`) exists for the same `MasterId`.

| Method | Behaviour |
|--------|-----------|
| `GetAllAsync` | Returns latest version of every non-soft-deleted item, ordered by `ModificationDate` descending |
| `GetByIdAsync(id)` | Returns the exact row with that `Id` (any version) |
| `GetByMasterIdAsync(masterId)` | Returns the latest version for the given `MasterId` |
| `GetAllVersionsAsync(masterId)` | Returns all versions for a `MasterId`, newest first |
| `GetBySlugAsync(slug)` | Returns the latest version with the given `Slug` |
| `GetChildrenAsync(parentMasterId)` | Returns latest versions of all items with `ParentMasterId = parentMasterId` |
| `GetRootsAsync()` | Returns latest versions of all items with `ParentMasterId = null` |
| `CreateAsync(entity)` | Sets `Id = Guid.NewGuid()`, then `MasterId = Id`; auto-generates `Slug` from `Title` if empty; sets timestamps; `Version` stays 0 |
| `UpdateAsync(entity)` | Verifies original `Id` exists; increments `Version`; assigns new `Id`; creates new row; clears `IsPublished` on prior versions if new version is published |
| `UpsertAsync(entity)` | Delegates to `CreateAsync` if `Id` or `MasterId` is empty; otherwise `UpdateAsync` |
| `DeleteAsync(id, softDelete, deleteHistory)` | See below |

**Delete modes:**
- `softDelete=false, deleteHistory=false` — hard-deletes the single row matching `id`
- `softDelete=true, deleteHistory=false` — sets `IsDeleted=true`, `IsPublished=false` on the matching row, then calls `UpdateAsync` (creates a new version recording the soft-delete)
- `deleteHistory=true, softDelete=false` — hard-deletes all versions for the same `MasterId`
- `deleteHistory=true, softDelete=true` — marks all versions for the same `MasterId` as deleted

---

## 6. `IPageService` and `ICMSRouteService`

`PageService` wraps `CmsDbContext` directly rather than reusing the generic `ContentService<T>`.
It is now purely about page records — **all route concerns moved to `ICMSRouteService`**.

| `IPageService` method | Behaviour |
|--------|-----------|
| `GetAllAsync` | Latest non-deleted versions |
| `GetByIdAsync(id)` | Exact row by `Id` |
| `GetAllVersionsAsync(masterId)` | All versions newest-first |
| `CreateAsync(page)` | Sets `MasterId = Id`, `Version = 0`; sets timestamps |
| `UpdateAsync(page)` | Increments version; new row; clears prior `IsPublished` if the new version is published |
| `DeleteAsync(id)` | Hard-deletes ALL versions for the `MasterId` |
| `DeleteVersionAsync(id)` | Deletes only the single version row matching `id` |

| `ICMSRouteService` method | Behaviour |
|--------|-----------|
| `MatchRouteAsync(path)` | Normalizes the path, walks active routes by `Order`, skips `IsReserved` rows, returns the first pattern match plus its extracted route values |
| `GetActiveRoutesAsync()` | Published, non-deleted, latest-version routes ordered by `Order` then `Pattern.Length` |
| `GetByOwningContentAsync(masterId)` | All latest routes owned by a piece of content |
| `GetByIdAsync(id)` | Exact row by `ContentId` |
| `IsPatternAvailableAsync(pattern, excludeMasterId)` | `true` if no latest, non-deleted route occupies that pattern; optionally excludes one owner (for edit-in-place checks) |
| `UpsertAsync(route)` | **Destructive replace** — hard-deletes the existing row for the owner (or pattern) and its `ContentMeta`, then inserts a fresh `Version = 0` row. Routes have no version history |
| `DeleteAsync(masterId)` | Hard-deletes all versions for the `MasterId` |
| `DeactivateByOwningContentAsync(masterId)` | Sets `IsPublished = false` on the owner's published routes (used when a page is unpublished or deleted) |

**Pattern normalization** (`CMSRouteService.NormalizePattern`), applied to both stored patterns and
incoming request paths:
1. Blank ⇒ `/`
2. Trim and lowercase
3. Ensure leading `/`
4. Remove trailing `/` unless the pattern is exactly `/`

The pattern-matching syntax is documented in [03-page-routing](03-page-routing.md#3-icmsrouteservice--matching-semantics).

Two more read-only services back the registries:

| Service | Methods |
|---|---|
| `IWidgetRegistrationService` | `GetActiveAsync`, `GetByComponentNameAsync`, `GetActiveByCategoryAsync` |
| `IPageControllerRegistrationService` | `GetActiveAsync`, `GetByControllerNameAsync`, `GetActiveByCategoryAsync` |

Both filter on `IsActive && IsPublished && !IsDeleted`, take the latest version per `MasterId`, and
order by Category → Order → DisplayName.

---

## 7. `IContentZoneService`

`ContentZoneService` wraps `CmsDbContext`. Zones and their items are both versioned.

**Zone methods:**

| Method | Behaviour |
|--------|-----------|
| `GetByNameAsync(name)` | Returns the published, non-deleted, latest-version zone with that name, including active items sorted by `Ordinal` |
| `GetByIdAsync(id)` | Returns the zone with that exact `Id`, including items |
| `GetAllAsync` | All latest, non-deleted zones including items |
| `CreateAsync` | Sets `MasterId = Id`; sets timestamps; `Version = 0` |
| `UpdateAsync` | Creates a new version row using `record with { }` syntax; preserves `MasterId` |
| `DeleteAsync` | Hard-deletes the single zone row (not all versions) |

**Item methods:**

| Method | Behaviour |
|--------|-----------|
| `AddItemAsync(zoneId, item)` | Sets `ContentZoneId`, `MasterId = Id`, `Version = 0`, `IsPublished = true`; auto-assigns `Ordinal` as `maxOrdinal + 1` |
| `UpdateItemAsync(item)` | Creates a new version row; preserves `Ordinal`, `ContentZoneId`, `MasterId` |
| `RemoveItemAsync(itemId)` | Hard-deletes the single item row |
| `GetItemByIdAsync(itemId)` | Returns the exact item row |
| `ReorderItemsAsync(zoneId, itemIdsInOrder)` | Updates `Ordinal` on the latest-version items in place (does not create new versions) |

**Assignment and slot methods:**

| Method | Behaviour |
|--------|-----------|
| `GetByPageSlotAsync(pageMasterId, slotName)` | Returns the assignment for a page's named slot, or `null` |
| `GetOrCreateByPageSlotAsync(pageMasterId, slotName)` | Returns existing `(Zone, Assignment)` or creates both atomically in a transaction; double-checked locking inside the transaction prevents duplicate creation on concurrent first renders |
| `GetByZoneSlotAsync(parentZoneId, slotName)` | Returns the assignment for a parent zone's named slot |
| `GetOrCreateByZoneSlotAsync(parentZoneId, slotName)` | Same pattern for nested zone slots |
| `GetOrCreateByNameAsync(name)` | Returns or creates a global zone by name (no assignment); also transactional |
| `GetAllAssignmentsForPageAsync(pageMasterId)` | All assignments for a page |
| `GetAllByPageAsync(pageMasterId)` | All latest zones assigned to a page |
| `GetAllByParentZoneAsync(parentZoneId)` | All latest zones that are nested children of a zone |
| `GetZoneIdsWithChildrenAsync(zoneIds)` | Returns which of the provided zone IDs have at least one child zone |
| `GetAllVersionsAsync(masterId)` | All zone versions newest-first |
| `GetAllItemVersionsAsync(itemMasterId)` | All item versions newest-first |
| `GetAssignmentCountsByMasterIdAsync(masterIds)` | Assignment count per zone `MasterId` (used for admin "in use" indicators) |

---

## 8. How to Add a New Content Type's Data Layer

1. **Create a DTO** in `WebWayCMS.Data/Data/Models/` implementing `IContent`:
   ```csharp
   public record MyThingDTO : IContent
   {
       public Guid ContentId { get; set; }
       public ContentDTO ContentMeta { get; set; } = new();
       public string Body { get; set; } = string.Empty;
   }
   ```

2. **Add an entity configuration class** in `WebWayCMS.Data/Data/EntityConfiguration/`. No change to
   `CmsDbContext` is needed — `ApplyConfigurationsFromAssembly` picks it up:
   ```csharp
   public sealed class MyThingDTOEntityConfiguration : IEntityTypeConfiguration<MyThingDTO>
   {
       public void Configure(EntityTypeBuilder<MyThingDTO> entity)
       {
           entity.ConfigureContentLink();          // shared PK/FK into Content
           entity.Property(e => e.Body).IsRequired();
           entity.ToTable("MyThings");
       }
   }
   ```
   > **Constraint:** `CmsDbContext` calls `ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly())`,
   > so it scans **only `WebWayCMS.Data`**. An `IEntityTypeConfiguration<T>` defined in a host
   > assembly is not discovered, and the CMS currently exposes no hook for registering one. Adding a
   > content type with its own table therefore means adding the DTO and its configuration to
   > `WebWayCMS.Data`. Host-defined content that fits the existing shape can still use
   > `ContentDTO.CustomFields` (JSONB) without a schema change.

3. **Add a migration** (from the repo root):
   ```bash
   dotnet ef migrations add AddMyThing \
     --project WebWayCMS.Data --startup-project WebWayCMS.Data \
     --context CmsDbContext --output-dir Migrations
   ```
   To wipe and regenerate the CMS migration from scratch, run `./scripts/RebuildEFMigrations.sh` —
   note this is **destructive**: it deletes `WebWayCMS.Data/Migrations/*` and recreates a single
   `InitialCreate`.

4. **Register in DI** (`ServiceCollectionExtensions.cs`, or the host's `Program.cs` for a
   host-defined type):
   ```csharp
   services.AddScoped<IContentService<MyThingDTO>>(sp =>
       new ContentService<MyThingDTO>(sp.GetRequiredService<CmsDbContext>()));
   ```
   `ContentService<T>` takes a plain `DbContext` and reaches entities via `Set<T>()`.

Migrations are applied automatically at startup via `app.EnsureCMS()` (or `EnsureCmsRendering()`).

---

*See also:* [docs/content-system.md](../content-system.md) for the full step-by-step content type creation guide including models, admin views, and mappings.
