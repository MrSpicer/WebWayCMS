# Area 5: Content Domain Models

**Namespaces:**
- `WebWayCMS.Models` — `BaseContentViewModel`
- `WebWayCMS.Models.Article`
- `WebWayCMS.Models.ContentBlock`
- `WebWayCMS.Models.ContentZone`
- `WebWayCMS.Models.Layout`
- `WebWayCMS.Models.Page`
- `WebWayCMS.Models.Shared` — `AdminCrudModel<T>`, `VersionedModel<T>`, `VersionHistoryViewModel`
- `WebWayCMS.Data` — `MappingProfile`
- `MySite` — `MappingProfile`

**Depends on:** Data Tier (services consumed), Admin CRUD Framework interfaces (`IAdminCrudHandler`, `IAdminCrudChildHandler`), Page Routing Subsystem (`IPageControllerRegistry` used in `PageModel`)
**Consumed by:** Admin CRUD Framework (resolves `IAdminCrudHandler` implementations), view components/views (consume ViewModels)

---

## 1. Role of Model Classes

Model classes are the business logic tier. They sit between the data tier (services/DTOs) and the presentation tier (controllers/views). Each model class:
- Orchestrates service calls to assemble a ViewModel
- Maps DTOs to ViewModels (via the in-house `IMapper`)
- Validates business rules before calling services (e.g., `PageModel` checks route uniqueness)
- Implements `IAdminCrudHandler` for top-level content types, making each model class self-describing to the admin CRUD framework

Models are registered in DI as **scoped** services, exposed under both their domain interface and `IAdminCrudHandler` so all consumers share the same scoped instance.

---

## 2. `VersionedModel<T>`

Abstract base class for any model that supports version history. Subclasses implement:

```csharp
protected abstract Task<List<TDto>> GetAllVersionsAsync(Guid masterId, CancellationToken ct);
protected abstract Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct);
protected abstract string VersionHistoryContentType { get; }
protected abstract string GetVersionHistoryBackUrl(string? parentKey = null);
```

`BuildVersionHistoryAsync` is the shared implementation that calls `GetAllVersionsAsync`, finds the maximum version number, and builds a `VersionHistoryViewModel` containing `VersionItemViewModel` entries with an `IsLatest` flag.

`VersionHistoryViewModel` is rendered by the shared `_VersionHistory.cshtml` partial in the admin UI.

---

## 3. `AdminCrudModel<T>`

`AdminCrudModel<T>` extends `VersionedModel<T>` and implements `IAdminCrudHandler`, combining two responsibilities in one class:

- **Domain model** — methods like `GetPageUpsertAsync`, `SaveArticleListUpsertAsync` called directly by domain consumers (page controllers, view components)
- **Admin CRUD handler** — the `IAdminCrudHandler` methods delegate to the domain methods, adapting the generic `object`-typed interface to the concrete types

This dual role means the DI registration exposes one scoped instance as both `PageModel` and `IAdminCrudHandler`, avoiding double instantiation.

**`AdminCrudModel<T>` default implementations:**

| Property/Method | Default |
|-----------------|---------|
| `WriteRoles` | `null` (Admin only) |
| `HasSecondaryApiList` | `false` |
| `GetSecondaryApiListAsync` | Returns empty |
| `RegistryHandler` | `null` |
| `ChildHandler` | `null` |
| `SupportsVersionHistory` | `true` |
| `GetVersionHistoryViewModelAsync` | Calls `BuildVersionHistoryAsync` |
| `DeleteVersionAsync` | Calls `DeleteVersionCoreAsync` |

Subclasses override what they need; everything else inherits the sensible default.

---

## 4. Built-in Model Types

### `PageModel`

- **ContentType:** `"pages"`
- **DisplayName:** `"Page"`
- **Handler:** Full `IAdminCrudHandler`; also exposes `IAdminRegistryHandler` via `PageRegistryHandler` (delegates to `IPageControllerRegistry` to supply controller metadata and available views to the admin page-edit UI)
- **Domain methods:** `GetByRouteAsync`, `GetPageIndexAsync` (builds `PageTreeNode` hierarchy), `GetPageUpsertAsync`, `SavePageUpsertAsync` (includes route availability check), `DeletePageAsync`, `IsRouteAvailableAsync`
- **Version restore:** Copies historical version, sets `Id`/`Version` to the latest version's values so saving creates a new version on top

### `ArticleListModel`

- **ContentType:** `"articles"`
- **DisplayName:** `"Article List"`
- **Handler:** Full `IAdminCrudHandler` for the parent (article list); exposes `ChildHandler` via `ArticleChildHandler` for individual articles
- **Domain methods:** `GetArticleListIndexAsync`, `GetArticleListUpsertAsync`, `SaveArticleListUpsertAsync`, `DeleteArticleListAsync` (cascades delete to all articles in the list), `GetArticlesForListAsync`, `GetArticlesForListBySlugAsync`
- **Secondary API list:** `HasSecondaryApiList = true`; `GetSecondaryApiListAsync("articlelists")` returns all article lists for GUID entity pickers

### `ArticleModel`

- **Not a top-level handler** — registered as `IArticleModel` only, not `IAdminCrudHandler`
- Used exclusively via `ArticleChildHandler` which delegates to it
- Domain methods: `GetUpsertViewModelAsync`, `SaveUpsertAsync`, `DeleteAsync`, version history methods

### `ContentBlockModel`

- **ContentType:** `"contentblocks"`
- **DisplayName:** `"Content Block"`
- **Handler:** Full `IAdminCrudHandler`; no child handler; no registry handler
- Domain methods: `GetIndexViewModelAsync`, `GetUpsertViewModelAsync`, `SaveUpsertAsync`, `DeleteAsync`

### `ContentZoneModel`

- **ContentType:** `"contentzones"`
- Manages both zones (parent) and zone items (child) via `ContentZoneChildHandler`
- Exposes `IContentZoneModel` which is consumed by `ContentZoneViewComponent`
- Domain methods: `GetOrCreateViewModelAsync`, `GetOrCreateViewModelByPageSlotAsync`, `GetOrCreateViewModelByZoneSlotAsync`, `GetViewModelByIdAsync`

---

## 5. Top-level vs Child Resource Pattern

**Top-level** resources have their own admin list/edit routes (`/admin/{contentType}/`). They extend `AdminCrudModel<TDto>` and are registered as `IAdminCrudHandler`.

**Child** resources live under a parent (`/admin/{contentType}/{parentKey}/{childType}/`). They do **not** extend `AdminCrudModel<TDto>`; instead their parent's model creates an inner class implementing `IAdminCrudChildHandler` and exposes it via `ChildHandler`. The child handler itself is not registered in DI — it is created as part of the parent model.

Example: `ArticleChildHandler` is a private sealed class inside `ArticleListModel.cs`, instantiated in `ArticleListModel`'s constructor and returned via `override IAdminCrudChildHandler? ChildHandler => _childHandler`.

---

## 6. ContentZoneConfiguration Classes

Each built-in content type that can contain zones (as a page, a layout region, etc.) has a configuration class that controls which zone slots are available:

| Class | Used By |
|-------|---------|
| `PageContentZoneConfiguration` | Zone slots available on pages |
| `ArticleContentZoneConfiguration` | Zone slots within article detail views |
| `ContentBlockContentZoneConfiguration` | Zone slots within content block views |
| `LayoutContentZoneConfiguration` | Zone slots in the shared layout |

These are passed as page controller `ConfigurationType` or referenced in view templates. Each contains `[FormProperty]`-decorated properties for the zone slot names editors can configure.

---

## 7. Mapping Profiles

Mapping is handled by an in-house mapper (`WebWayCMS.Mapping`: `IMapper`, `Profile`,
`MapperConfiguration`). Each `Profile` subclass declares pairs in its constructor with
`CreateMap<TSource, TDestination>(s => new TDestination { ... })`, where the converter lambda is the
complete mapping logic for the pair.

**`WebWayCMS.Data.MappingProfile`** — the CMS library's mapping profile:
- Maps all built-in DTOs to their ViewModels and back
- Conventions:
  - `Id` on a DTO maps to `Id` on the ViewModel for edits; new ViewModel has `Id = null`
  - `MasterId` is preserved on ViewModels for version tracking
  - `PublicationDate` is stored in UTC; normalized to UTC in mapping if not already
  - Fields not needed in the ViewModel are simply left unset in the converter lambda

**`MySite.MappingProfile`** — the Web project's mapping profile:
- Empty by default; add Web-project-specific custom mappings here
- Registered alongside the CMS profile in `Program.cs`

Profiles are collected by a `MapperConfiguration` whose `CreateMapper()` builds the `IMapper`
registered in DI (see `ServiceCollectionExtensions`):
`new MapperConfiguration(cfg => cfg.AddProfile(new MappingProfile())).CreateMapper()`.

---

*See also:* [docs/content-system.md](../content-system.md) for the step-by-step guide to adding a custom content type.
