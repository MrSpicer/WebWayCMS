# Area 5: Content Domain Models

**Namespaces:**
- `WebWayCMS.Models` — `BaseContentViewModel`
- `WebWayCMS.Models.Article`
- `WebWayCMS.Models.ContentBlock`
- `WebWayCMS.Models.ContentZone`
- `WebWayCMS.Models.Layout`
- `WebWayCMS.Models.Page`
- `WebWayCMS.Models.CMSRoute`, `.WidgetRegistration`, `.PageControllerRegistration`
- `WebWayCMS.Models.Shared` — `AdminCrudModel<T>`, `VersionedModel<T>`, `VersionHistoryViewModel`
- `WebWayCMS.Interfaces` — `IRoutableContent`, `IRoutableViewComponent`, `IRouteRegistrationService`
- `WebWayCMS.Data` — `MappingProfile`
- `MySite` — `MappingProfile`

**Depends on:** Data Tier (services consumed — now `IContentStore<T>`), Admin CRUD Framework interfaces (`IAdminCrudHandler`), CMS Routing Subsystem (`IPageControllerRegistry` and `ICMSRouteService` used in `PageModel`), Content Zone Framework (`IWidgetRegistry`)

> **Versioning note (Node/Version model):** `VersionedModel<T>`/`AdminCrudModel<T>` are now generic over
> `IVersionedContent` and backed by `IContentStore<T>`. `AdminCrudModel<T>` opens a change-set scope around
> `SaveUpsertAsync`, and gains `PublishAsync`/`UnpublishAsync`/`RestoreVersionAsync`. View models carry
> `NodeId` + `ExpectedVersionNumber` (no `Id`/`MasterId`/`Version`/`IsPublished`). See
> [01-data-tier](01-data-tier.md).
**Consumed by:** Admin CRUD Framework (resolves `IAdminCrudHandler` implementations), view components/views (consume ViewModels)

---

## 1. Role of Model Classes

Model classes are the business logic tier. They sit between the data tier (services/DTOs) and the presentation tier (controllers/views). Each model class:
- Orchestrates service calls to assemble a ViewModel
- Maps DTOs to ViewModels (via the in-house `IMapper`)
- Validates business rules before calling services (e.g., `PageModel` checks route uniqueness)
- Implements `IAdminCrudHandler` for top-level content types, making each model class self-describing to the admin CRUD framework

Models are registered in DI as **scoped** services by `AddWebWayCmsRendering`, exposed under their domain interface. `AddWebWayCmsAdmin` then adds a second registration forwarding the same scoped instance as `IAdminCrudHandler`, so all consumers share one instance per request. In a rendering-only host the models exist but are never surfaced as admin handlers.

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

`VersionHistoryViewModel` is rendered by the shared `Views/AdminShared/VersionHistory.cshtml` view in `WebWayCMS.Admin`.

---

## 3. `AdminCrudModel<T>`

`AdminCrudModel<T>` extends `VersionedModel<T>` and implements `IAdminCrudHandler`, combining two responsibilities in one class:

- **Domain model** — methods like `GetPageIndexAsync`, `SavePageUpsertAsync` called directly by domain consumers (page controllers, view components)
- **Admin CRUD handler** — the `IAdminCrudHandler` methods delegate to the domain methods, adapting the generic `object`-typed interface to the concrete types

This dual role means the DI registration exposes one scoped instance as both `PageModel` and `IAdminCrudHandler`, avoiding double instantiation.

`AdminCrudModel<T>.SaveUpsertAsync` is **sealed behaviour**: it runs `RichTextSanitizer.Sanitize`
over the incoming view model before delegating to the abstract `SaveUpsertCoreAsync` that each
subclass implements. This single choke point is what makes stored rich text safe to render with
`@Html.Raw`, and it covers the MCP tools as well as the admin UI. Subclasses override
`SaveUpsertCoreAsync`, never `SaveUpsertAsync`. See [Area 13](13-security.md).

**`AdminCrudModel<T>` default implementations:**

| Property/Method | Default |
|-----------------|---------|
| `WriteRoles` | `null` (Admin only) |
| `HasSecondaryApiList` | `false` |
| `GetSecondaryApiListAsync` | Returns empty |
| `RegistryHandler` | `null` |
| `GetRelatedChildrenAsync` / `ReorderRelatedAsync` / `MoveRelatedAsync` | Generic over the upsert view model's containments (see [06 §3](06-admin-crud-framework.md#3-containment--children-reorder-move)) |
| `RestoreVersionAsync(versionId, expectedVersionNumber)` | Refuses a stale restore, then the plain restore |
| `SupportsVersionHistory` | `true` |
| `GetVersionHistoryViewModelAsync` | Calls `BuildVersionHistoryAsync` |
| `DeleteVersionAsync` | Calls `DeleteVersionCoreAsync` |

Subclasses override what they need; everything else inherits the sensible default.

---

## 4. Built-in Model Types

### `PageModel`

- **ContentType:** `"pages"`
- **DisplayName:** `"Page"`
- **Also implements:** `IRoutableContent` (`RouteContentType => "Page"`)
- **Handler:** Full `IAdminCrudHandler`; also exposes `IAdminRegistryHandler` via `PageRegistryHandler` (delegates to `IPageControllerRegistry` to supply page-type metadata and available views to the admin page-edit UI)
- **Domain methods:** `GetPageIndexAsync` (builds the `PageTreeNode` hierarchy by joining pages to their `CMSRoutes` patterns, with each page's `DescendantCount`), `SavePageUpsertAsync`; `DeleteSelfAsync` deletes one page (its routes and zones) after the relation engine has deleted its child pages
- **Parent:** `PageUpsertViewModel.ParentNodeId` is a `[ContentRelation("pages", Kind = Containment)]` (`PageModel.ParentRelation`), the form's Parent Page picker; a move is a versioned draft, and its publish re-derives the URLs
- **URL handling:** the page's route pattern is derived from its Slug (`DeriveRoutePatternFromSlug`) and written through `IRouteRegistrationService` on save; `ICMSRouteService.IsPatternAvailableAsync` guards uniqueness and produces the `"A page with this slug already exists at this location."` error on the `Slug` field. Saving an unpublished page unregisters its route instead
- **Version restore:** Copies historical version, sets `Id`/`Version` to the latest version's values so saving creates a new version on top

### `ArticleListModel`

- **ContentType:** `"articlelists"`
- **DisplayName:** `"Article List"`
- **Handler:** Full `IAdminCrudHandler`; its edit screen lists its articles (the generic children panel, with **New Article** preselecting the list). Its index (the admin entry point for articles) shows each list's article count (`IContentStore.CountCurrentDraftsByRelationAsync(ArticleModel.ListRelation)`) and links to `/wadmin/articles?list={nodeId}`
- **Domain methods:** `GetArticleListIndexAsync`, `GetArticleListUpsertAsync`, `SaveArticleListUpsertAsync`, `DeleteArticleListAsync` — the base `DeleteAsync`: the relation engine deletes the list's articles (its containment children: any article whose current draft or published version is in the list), then the list (`DeleteSelfAsync`), in one transaction. An article moved to another list and live there is not touched
- **API list:** `GetApiListAsync` returns the lists, so an `EntityType = "ArticleList"` picker resolves to `/wadmin/articlelists/api/list`

### `ArticleModel`

- **ContentType:** `"articles"`, **DisplayName:** `"Article"`, `WriteRoles = ["Admin", "Editor"]`
- **Handler:** Full `IAdminCrudHandler` (draft → publish, version history, restore from the base class); also registered as `IArticleModel` for the widget's public reads
- **List membership:** `ArticleUpsertViewModel.ArticleListId` is `[ContentRelation("articlelists", Kind = Containment, Required = true)]` (relation name `ArticleModel.ListRelation`), which implies the `EntityPicker`; the mapper stores it on the version and the relation binder rejects an empty or unknown list. The save path itself rejects a slug another article's current draft already uses
- **Admin index** honours `?list={nodeId}` (`ListQueryKey`) through the query-aware `IAdminCrudHandler.GetIndexViewModelAsync` overload; its **New Article** link preselects the same list with `?ArticleListId=` (the controller's generic `RelationPreselect`)
- **Public reads** (`IArticleModel`): `GetViewModelAsync`, `GetBySlugAsync`, `GetListAsync(listNodeId, maxItems, sortOrder)` (`FindRelatedAsync`) — the store's containment filter returns nothing unless the article's list is visible in the current read context

### `ContentBlockModel`

- **ContentType:** `"contentblocks"`
- **DisplayName:** `"Content Block"`
- **Handler:** Full `IAdminCrudHandler`; no registry handler
- Domain methods: `GetIndexViewModelAsync`, `GetUpsertViewModelAsync`, `SaveUpsertAsync`, `DeleteAsync`

### `ContentZoneModel`

- **ContentType:** `"contentzones"`
- Manages zones; zone items have their own handler, `ContentZoneItemModel` (below), which delegates every write to this model
- Exposes `IContentZoneModel` which is consumed by `ContentZoneViewComponent`
- Exposes `IAdminRegistryHandler` via `ContentZoneRegistryHandler`, backed by `IWidgetRegistry`
- Domain methods: `GetOrCreateViewModelAsync`, `GetOrCreateViewModelByPageSlotAsync`, `GetOrCreateViewModelByItemSlotAsync`, `GetViewModelByIdAsync`, `MoveItemAsync`
- Also calls `IRouteRegistrationService.TryRegisterWidgetRoutesAsync` for zone-item writes; routes for items in a page's zones are registered by the page publish cascade (see [03-page-routing.md](03-page-routing.md))

### `ContentZoneItemModel`

- **ContentType:** `"contentzoneitems"` — **DisplayName:** `"Content Zone Item"`; Admin only; `SupportsPublishing = false` (items go live with their page, or at once in a global zone)
- Its upsert view model's `ContentZoneNodeId` is the ordered `Zone` containment (hidden, `ShowInParentAdmin = false`); create places the item last in that zone, an update never moves it
- `DeleteAsync` **removes** the item (an inactive version); `MoveRelatedAsync`/`ReorderRelatedAsync` are the zone service's move/reorder; restore is one step (content, metadata, active state, zone and position); a version in page history cannot be deleted; a container's history is the combined timeline
- Index: one zone's items (`/wadmin/contentzoneitems?ContentZoneNodeId={zone}`)

### `WidgetRegistrationModel`

- **ContentType:** `"widgets"` — **DisplayName:** `"Widget Registration"`
- Admin surface for the `WidgetRegistrations` table seeded from `[ContentZoneComponent]`
- Every mutation calls `IWidgetRegistry.Invalidate()` so the 5-minute cache does not mask edits
- `BuildPropertyDefinitions(configurationTypeName)` resolves the CLR type and re-serializes `FormPropertyBuilder.BuildPropertyInfos(type)` into `PropertyDefinitionsJson`, returning an error if the type cannot be resolved
- Exposes the same `ContentZoneRegistryHandler : IAdminRegistryHandler` as `ContentZoneModel` (one shared implementation of the component registry endpoints), which resolves `EditorType.ViewPicker` options through `IViewDiscoveryService`

### `PageControllerRegistrationModel`

- **ContentType:** `"pagetypes"` — **DisplayName:** `"Page Controller Registration"`
- Admin surface for the `PageControllerRegistrations` table seeded from `[PageController]`
- Every mutation calls `IPageControllerRegistry.Invalidate()`
- Same `BuildPropertyDefinitions` behaviour as `WidgetRegistrationModel`

### `CMSRouteModel`

- **ContentType:** `"cmsroutes"` — **DisplayName:** `"CMS Route"`
- Admin surface for the `CMSRoutes` table
- **`SupportsVersionHistory => false`** — `ICMSRouteService.UpsertAsync` replaces rows rather than versioning them, so `GetAllVersionsAsync` returns empty and `DeleteVersionCoreAsync` returns `false`
- `SaveRouteUpsertAsync` rejects duplicates with `"This route pattern is already in use."`

---

## 5. Parent and Child Content

Every admin resource is **top-level**: it has its own admin list/edit routes (`/wadmin/{contentType}/`),
extends `AdminCrudModel<TDto>` (or implements `IAdminCrudHandler` directly, like `ContentZoneItemModel`) and is
registered as `IAdminCrudHandler`. A child is placed under its parent by a **containment** relation on its own
upsert view model (an article's list, a page's parent page, a zone item's zone); the parent's edit screen lists
it through the generic children panel, and MCP's child tools address it through that relation (see
[06 §3](06-admin-crud-framework.md#3-containment--children-reorder-move) and
[17](17-content-relations.md)). The old `IAdminCrudChildHandler` (an inner child handler on the parent's model)
is gone.

---

## 6. ContentZoneConfiguration Classes

Each built-in content type that can contain zones (as a page, a layout region, etc.) has a configuration class that controls which zone slots are available:

| Class | Used By |
|-------|---------|
| `PageContentZoneConfiguration` | `PageViewComponent` — page-navigation filters (`ShowDraftPages`, `ShowHiddenPages`, `AdminPages`) plus `ViewName` |
| `RouteNavigationConfiguration` | `RouteNavigationViewComponent` — route-navigation filters (`AdminRoutes`, `IncludeReserved`) plus `ViewName` |
| `ArticleContentZoneConfiguration` | `ArticleViewComponent` — `ArticleId` (Single), `ArticleListId`/`MaxItems`/`SortOrder` (List) plus the inherited view pickers |
| `ContentBlockContentZoneConfiguration` | Zone slots within content block views |
| `LayoutContentZoneConfiguration` | Zone slots in the shared layout |

These are passed as page controller `ConfigurationType` or referenced in view templates. Each contains `[FormProperty]`-decorated properties for the zone slot names editors can configure.

---

## 7. Mapping Profiles

Mapping is handled by an in-house mapper (`WebWayCMS.Mapping`: `IMapper`, `Profile`,
`MapperConfiguration`). Each `Profile` subclass declares pairs in its constructor with
`CreateMap<TSource, TDestination>(s => new TDestination { ... })`, where the converter lambda is the
complete mapping logic for the pair.

**`WebWayCMS.Data.MappingProfile`** (the file lives at `WebWayCMS.Core/Data/MappingProfile.cs`) — the CMS library's mapping profile:
- Maps all built-in DTOs to their ViewModels and back
- Conventions:
  - `NodeId` (from `Version.Node.Id`) maps to `NodeId` on the ViewModel for edits; a new ViewModel has `NodeId = null`
  - `ExpectedVersionNumber` is stamped from the current draft's `VersionNumber` for concurrency tracking
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
