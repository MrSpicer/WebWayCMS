# Area 17: Content Relations

> **Status: Implemented** (all six phases) — the engine (§2–§6), every row of §7, routes as content
> (§8), both admin UXes (§9), the generic child tools and zone-item seeding (§10), and the TestHost on
> relations (its FAQ pickers, and the host `FaqTopic` containment of §3 — see
> [Area 14](14-host-extensibility.md#relations-to-other-content)). Sections marked *today* describe the code before relations; sections marked
> **Implemented** describe the code as it is. Decisions already agreed are stated as settled; the
> questions raised during review are recorded as [resolved](#14-resolved-questions). The implementation
> plan is in [plans/content-relations](../../plans/content-relations/README.md).

**Namespaces (implemented):**
- `WebWayCMS.Data.Models` — `ContentRelation`, `RelationKind`, `RelationWrite`, `ContentVersionRelations`
- `WebWayCMS.Data.Services` — `IContentRelations`/`ContentRelations`, `ContainmentVisibility`,
  `RelationPredicates`, the relation baseline in `ContentStore`
- `WebWayCMS.Attributes` / `WebWayCMS.Forms` (Forms project) — `[ContentRelation]`, `ContentRelationKind`,
  `RelationDeletePolicy`, `RelationProperties` (reflection), read by `FormPropertyBuilder`
- `WebWayCMS.Services.Relations` (Core) — `RelationBinder`, `ContentRelationRegistry`,
  `ContentDeleteService`, `IContentDeleter`, `RelationKindMap`, the mapper's relation step

**Consumed by:** every content type (core and host), zones, routing, admin forms, MCP, seeding

---

## 1. The Problem

Content relates to other content through six unrelated mechanisms *today*:

| Link | Storage | Versioned | Integrity |
|---|---|---|---|
| Article → Article List | `ArticleDTO.ArticleListNodeId` FK (`Restrict`) | yes (type row) | FK + hand-written save check |
| Zone item → zone + order | `ContentZoneItemDTO.ContentZoneNodeId` + `Ordinal` (`Cascade`) | yes | FK |
| Page / container item → zone (slot) | `ContentZoneAssignments` (page XOR item, `SlotName`) | no | FK on the item side only |
| Page → parent page | `ContentNode.ParentNodeId` | no | index only |
| Widget / page config → content | a node id inside config JSON (Image, Article, ContentBlock, Faq pickers) | yes | none (soft) |
| Route → owner | `CMSRoutes.OwningContentNodeId` + `OwningContentType` | no | index only |

Each one has its own storage, versioning rules, integrity checks, read rules and admin UI. A new
relationship (e.g. a host `Faq → FaqTopic`) means re-implementing the article pattern by hand:

- **Storage:** an FK column plus `HasOne<ContentNode>()…OnDelete(Restrict)` in the entity configuration.
- **Validation:** a check that the target exists (`ArticleModel.SaveUpsertCoreAsync`). Without it, a bad
  id surfaces as a raw `DbUpdateException`.
- **Delete:** a cascade delete inside an `ICmsTransaction` (`ArticleListModel.DeleteArticleListAsync`).
- **Visibility:** a rule that hides a child whose parent isn't visible (`ArticleModel.MapIfListVisibleAsync`).
- **Admin:** a filtered index that simulates nested editing (`/wadmin/articles?list={id}`), because the
  generic child mechanism (`IAdminCrudChildHandler`) is implemented only by zones.
- **Seeding:** none for children. The seeder's child path (`JsonContentSeeder.ApplyZoneItemAsync`) is
  zone-only.

**Goal:** one simple, sound pattern for every relationship. Core and client sites declare a relation
once and get storage, versioning, validation, delete policy, read visibility, admin editing, MCP and
seeding from one engine.

## 2. The Record

> **Implemented** as designed, in `WebWayCMS.Data` (see [Area 1](01-data-tier.md#contentrelation--content-to-content-links-owned-by-the-source-version)).

A relation is a row that sits between two content items. It is the **only** place a relation's value
lives: FK columns and in-JSON ids go away.

```csharp
public record ContentRelation
{
    public Guid Id { get; set; }

    /// <summary>The source version that owns this relation (Cascade: a version's relations go with it).</summary>
    public Guid SourceVersionId { get; set; }

    /// <summary>The target item, always a node, never a version. No DB FK: the engine enforces integrity (§6).</summary>
    public Guid TargetNodeId { get; set; }

    /// <summary>Containment, Reference or Owned. Stored so visibility and cascade need no declarations.</summary>
    public RelationKind Kind { get; set; }

    /// <summary>The declaring property, e.g. "ArticleListId", or "config.ImageId" for a config picker.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Order within (SourceVersionId, Name); for ordered containment, the child's position.</summary>
    public int Ordinal { get; set; }

    /// <summary>"" by default; the zone slot name for owner → zone.</summary>
    public string Qualifier { get; set; } = string.Empty;
}
```

- **Version-owned.** Rows hang off `ContentVersion` as a `Relations` navigation. Changing a relation
  therefore mints a new version and stays a draft until publish. Restore, page revert, discard, page
  history snapshots and the read context (Published / Draft / Snapshot) all cover relations with no
  extra code, because they already select versions.
- **Its own table**, not JSON like `CustomFields`, so "who points at X" is an indexed query.
- **Indexes:** `(SourceVersionId, Name)` and `(TargetNodeId, Name)`.
- **Node targets.** A relation means "the item", never "version 7 of the item". Which version of the
  target you get is still decided by the read context (`ContentQueryExtensions.AtReadContext`).
- **Direction.** A relation is stored on the side whose edits should version it:
  - **containment:** on the child (a move is a child edit);
  - **reference:** on the referrer.

## 3. Declaring a Relation

> **Implemented**, with one change: Data cannot reference Forms, so the attribute's kind is
> `ContentRelationKind` (Forms) and the stored row's is `RelationKind` (Data), with the same values;
> `RelationKindMap` (Core) converts. `Guid?` is also accepted (an optional single relation).
> `OnTargetDelete` records whether it was set, so the registry can reject it on a containment.
> The attribute on `WidgetConfiguration` and page configuration types is implemented (phase 2), with
> relation names prefixed `config.`.

A relation is declared with one attribute on a property:

```csharp
public enum RelationKind { Containment, Reference, Owned }   // Owned: a reference whose target lives and dies with the source
public enum RelationDeletePolicy { Restrict, Detach }

[AttributeUsage(AttributeTargets.Property)]
public sealed class ContentRelationAttribute(string targetContentType) : Attribute
{
    public string TargetContentType { get; } = targetContentType;
    public RelationKind Kind { get; init; } = RelationKind.Reference;
    public bool Required { get; init; }
    public bool Ordered { get; init; }                                                  // containment only
    public RelationDeletePolicy OnTargetDelete { get; init; } = RelationDeletePolicy.Restrict; // reference only
}
```

**Property shapes:**

- `Guid` is a single relation.
- `IEnumerable<Guid>` (or `List<Guid>`) is many, with order taken from the list.

**Where the attribute may appear:**

- content-type upsert view models;
- `WidgetConfiguration` subclasses (the relation is owned by the zone item's version);
- page configuration types (owned by the page's version).

**The attribute implies the form field.** `FormPropertyBuilder` reads `[ContentRelation]` and emits an
EntityPicker, multi-select for many. The endpoint is resolved as today by
`EntityPickerEndpoint.Resolve`. A separate `[FormProperty(FormComponent = "EntityPicker", EntityType = …)]`
is no longer needed; `[FormProperty]` stays for label, order and help text.

**Startup validation** (`ContentRelationRegistry`, built from the scanned assemblies) fails fast when:

- a containment property is not a single `Guid` (a child has exactly one parent);
- the target content type key is not registered;
- two relations on one type share a `Name`;
- `Ordered` is set on a reference, or `OnTargetDelete` on a containment.

### Host example: `Faq → FaqTopic`

```csharp
public sealed class FaqUpsertViewModel : BaseContentViewModel
{
    [FormProperty(Label = "Topic", Order = 3)]
    [ContentRelation("faqtopics", Kind = ContentRelationKind.Containment, Required = true)]
    public Guid FaqTopicId { get; init; }

    [FormProperty(Label = "Related questions", Order = 9)]
    [ContentRelation("faqs")]
    public List<Guid> RelatedFaqIds { get; init; } = [];
}
```

That is all the relationship code. The host's `FaqDTO` table holds only its own fields: no FK column,
no `HasOne<ContentNode>()`, and nothing relation-related in the host migration, because the relation
table belongs to the CMS.

> **Implemented** in the TestHost (phase 6) as an optional containment (`Guid? FaqTopicId`, no
> `Required`) with a `FaqTopicModel` admin type; the related-questions list is not part of it. The FAQ
> widget's pickers are references (`Detach`), the configured page's is a reference (`Restrict`). See
> [Area 14](14-host-extensibility.md#relations-to-other-content).

## 4. The Engine

> **Implemented**, with these differences from the sketch below: the **mapper** (not the binder) moves
> property values to and from the version (`RelationMapping`, run after every profile mapping); the
> **binder** only validates (`IRelationBinder.ValidateAsync`); writes go through
> `ContentVersion.RelationWrites` and the store's baseline (current draft on edit, historical version on
> restore, none on create); and the queries are the ones below the sketch.

There is one engine, and every write path goes through it.

### Storage (Data tier)

- `ContentStore` writes `Version.Relations` in the **same `SaveChanges`** as the version.
- Every path that mints a version copies relations exactly as `CustomFields` are copied today:
  - `ContentStore.BuildVersion` (edit and create);
  - restore;
  - `PageHistoryService` revert and discard (`ApplyPageAsync` and the item equivalent).
- `PageSetEquality` includes relations in its content comparison, so "pending changes" and history
  de-duplication see a relation change.

### Binding (Core)

`RelationBinder` maps view-model properties to rows and back.

- **Where it runs on save:** at the top-level save choke point, `AdminCrudModel.SaveUpsertAsync`, beside
  `RichTextSanitizer` and `ModelValidator`. That covers the admin UI, MCP `create_content` /
  `update_content` and the JSON seeder in one place.
- **What it does on save:**
  1. Check that each target exists as a current draft of the declared content type, so a draft target
     can be chosen.
  2. Enforce `Required`.
  3. Return a field error (`AdminSaveResult` with the property name) instead of an FK violation.
  4. Write the rows onto the entity's version.
- **On load:** it fills the properties from the current draft's rows (`GetUpsertViewModelAsync`,
  restore view models).
- **Config relations (implemented, phase 2):** zone-item and page writes run the same binder over the
  config type, beside `WidgetConfigurationJson.Sanitize`. Relation properties are left out of the
  stored config JSON (`ExtractRelations`) and hydrated from rows when the config is deserialized for
  render (`ContentZoneModel`, `CMSRouteTransformer`) and for the editor/MCP (`HydrateRelations`).

### Queries (`IContentRelations`)

All queries are read-context aware: Published on a rendering host, Draft in a preview, Snapshot in a
history preview.

```csharp
Task<IReadOnlyList<Guid>> GetTargetsAsync(Guid sourceNodeId, string name, CancellationToken ct = default);
Task<IReadOnlyList<T>> GetSourcesAsync<T>(Guid targetNodeId, string name, CancellationToken ct = default)
    where T : class, IVersionedContent;                                   // ordered by Ordinal
Task<IReadOnlyList<RelationReference>> GetAnyVersionReferencesAsync(Guid targetNodeId, CancellationToken ct = default);
```

- `GetSourcesAsync` returns sources whose **read-context version** points at the target. It is the
  generic form of "articles in this list" (`FindAsync(a => a.ArticleListNodeId == x)`) and "items in
  this zone".
- `GetAnyVersionReferencesAsync` searches **every** version of every source. The delete policy uses it,
  so a historical version that still references the target is never missed.

**As implemented** (resolved question 2 replaced the any-version search): the read-context queries live on
the store — `IContentStore<T>.FindRelatedAsync(name, target)` (ordered by `Ordinal`, then newest),
`FindCurrentDraftsRelatedAsync` and `CountCurrentDraftsByRelationAsync`, with the version's own
`TargetOf`/`TargetsOf` for the forward direction — and `IContentRelations` answers the cross-type, **live
only** questions the delete policy and binder need: `GetLiveReferrersAsync(target, kind?)`,
`GetLiveChildrenAsync(parent)`, `GetLiveOwnedTargetsAsync(source)` and `GetNodesAsync(ids)`.

## 5. Read Semantics

> **Implemented** for containment: `ContainmentVisibility` filters every read-context `ContentStore` read
> (memoized per request; skipped in Draft; a snapshot-governed node counts as visible).

- **Containment:** a child is visible only while its parent is visible in the same read context. This
  is the generic form of `MapIfListVisibleAsync`, and it applies transitively (an item in a zone owned
  by an unpublished page is not visible).
- **Reference:** a target that isn't visible resolves to nothing, and the source still renders. A FAQ
  whose related FAQ is unpublished shows without it.
- **Following the live target.** Editing and publishing a target changes what every referrer shows,
  because references point at nodes.
- **Snapshot mode** needs no changes: a snapshot pins source versions, and their relation rows come
  with them.

## 6. Write and Delete Semantics

> **Implemented** by `ContentDeleteService` (`AdminCrudModel.DeleteAsync` / `TryDeleteAsync`; a model keeps
> only `DeleteSelfAsync`): containment descendants deepest first and owned targets in one
> `ICmsTransaction`, `Restrict` / `Detach` for references from outside the deletion set, live versions only.
> A lone node with nothing to cascade runs its own delete without the wrapping transaction.

### Containment (parent–child)

- **Delete:** deleting a parent deletes its children recursively, in one `ICmsTransaction`. This
  generalizes `DeleteArticleListAsync` and the zone/page delete. It is a **behaviour change for pages**:
  child pages are orphaned today (`PageModel.DeletePageAsync` deletes only zones and the page).
- **Move:** moving a child to another parent is a new child version. It is a draft until published, as
  zone moves are today.
- **Order:** for an `Ordered` containment, the child's position is its row's `Ordinal`. A reorder mints
  versions only for the children that moved, as `ContentZoneService` does today.

### Reference

Only referrers' **current draft and published** versions count. Historical rows that point at a
deleted target are kept and resolve to nothing on read or restore, which is why `TargetNodeId` has no
database FK.

- **`Restrict` (default):** a target can't be deleted while a referrer's current draft or published
  version references it. The error names the referrers.
- **`Detach`:** deleting the target saves a new draft of each referrer without the relation.
  A referrer's upsert relation is cleared on its upsert view model and saved through its handler. A
  **config relation** (`config.*`) is not on any upsert view model, so it has its own path
  (`ContentDeleteService.DetachAsync`): a zone item's is written off its current draft
  (`ContentVersion.SetRelations`) and saved with `IContentZoneService.UpdateItemAsync` (a new version,
  recorded in page history); a page's is removed from the configuration JSON its upsert view model
  hydrates (`WidgetConfigurationJson.ExtractRelations` → `HydrateRelations`) and saved through the page
  handler. All of it runs inside the delete's transaction, so a failed detach deletes nothing.

### Owned reference

The target is deleted with its source and published with it. Routes use this (§8).

**Soft delete** (`IsDeleted`) is always allowed: rows stay valid, and every read filters the node out.

## 7. Existing Links Mapped onto Relations

| Link | Kind | Owned by (source version) | Target | Notes |
|---|---|---|---|---|
| Article → Article List | Containment | article | list node | **Implemented** (phase 1): `ArticleListNodeId` dropped; the list delete cascade and visibility rule are generic |
| Zone item → zone | Containment, `Ordered` | zone item | zone node | **Implemented** (phase 4): relation `ZoneRelations.Zone`; `ContentZoneNodeId` + `Ordinal` are in-memory accessors. Phase 5 declares it on `ContentZoneItemUpsertViewModel.ContentZoneNodeId` (`ShowInParentAdmin = false`), so the stored name became `ContentZoneNodeId` |
| Owner → zone (slot) | Containment | zone | page or zone-item node | **Implemented** (phase 4): relation `Owner`, `Qualifier` = slot; `ContentZoneAssignments` dropped; deterministic zone ids |
| Page → parent page | Containment | page | parent page node | **Implemented** (phase 5): `ContentNode.ParentNodeId` dropped; `PageUpsertViewModel.ParentNodeId` is the relation (the page form's Parent Page picker), so a move is a versioned draft; the binder's cycle guard refuses a page under itself or a descendant; a page delete cascades to its subtree |
| Widget / page config pickers | Reference | zone item / page | picked node | **Implemented** (phase 2): the id leaves the config JSON and is stored as `config.*` |
| Routable content → route | Reference (see §8) | owner | route node | replaces the `CMSRoutes` ownership columns |

Notes:

- **Owner → zone.** Lazy slot creation (`GetOrCreateByPageSlotAsync` / `GetOrCreateByItemSlotAsync`)
  mints zone v1 together with its owner relation, on the node id `ZoneNodeIds.For(owner, slot)` (a v5 Guid),
  so the `ContentNodes` primary key replaces the old (owner, slot) unique index as the race guard. A zone has
  one owner. A container still carries its zones when it moves, because the zone points at the item node.
  The slot-remap planner moves items between zones as before; those moves are child-version edits of the
  items. Ownership is read from the zone's current draft (`PageZoneTree.ZoneOwners()`); whether the owner
  is a page or an item comes from the owner node's content type.
- **Page → parent.** Moving a page becomes a draft until publish, consistent with every other edit.
  Route derivation (`DeriveRoutePatternForPublishAsync`) uses the **published** parent, and the admin
  tree uses current drafts.

## 8. Routes as Content

> **Implemented** (phase 3), including the owned `routes` relation, projection into `CMSRoutes`,
> read-only projected admin rows, and the descendant re-projection on a derived prefix change.

A route becomes a versioned content type, `routes`:

```csharp
public record RouteDTO : IVersionedContent
{
    public Guid VersionId { get; set; }
    public ContentVersion Version { get; set; } = new();
    public string Pattern { get; set; } = string.Empty;
    public string? NavigationName { get; set; }
    public int Order { get; set; }
}
```

- **Relation:** routable content declares `[ContentRelation("routes", Kind = RelationKind.Owned)] List<Guid> RouteIds` on its
  upsert view model. Route edits are made through the reference modal (§9), so a user configures a
  route while creating or editing the content.
- **Drafts until the owner publishes.** Publishing the owner publishes its related route drafts, the
  same way a page publish cascades to its zone items.
- **Projection.** `CMSRoutes` stays as the transformer's fast lookup table, now a projection of
  published route versions:
  - The owner's publish writes the projection, replacing today's delete-and-reregister step in
    `PageModel.PublishPageAsync`.
  - Unpublish and delete remove it.
  - The collision rules of `CMSRouteService.UpsertAsync` (keyed on owner + pattern; reserved names) are
    kept.
  - **One owner.** An owned route has exactly one owner: `UpsertAsync` refuses a row for a route item
    whose row belongs to another owner, and the page save refuses a `RouteIds` entry another page's
    current draft owns (field error on `RouteIds`).
  - **All or nothing.** The owner's publish, its route items' publishes, the projection and the
    descendant re-projection share one `ICmsTransaction`; a collision, a missing route item or a
    colliding descendant rolls the whole publish back. Descendants are re-projected from their
    **published** page and route versions only.
- **Default pattern.** When the owner has no explicit route, publish derives one from slug and parent,
  as today, and mints it as a route item. The user can then edit it.
- **What stays generated:** widget routes (`IRoutableViewComponent`, registered at page publish) and
  code-based routes (`[CmsRoute]`, `CmsRouteSeeder`). They are written straight into `CMSRoutes` and
  are not route content.
- **Generic.** `IRoutableContent` (today implemented only by `PageModel`) becomes "has a
  `routes` relation", so any content type can own routes.

## 9. Admin UX

### Containment: children on the parent's screen

> **Implemented** (phase 5) — see [Area 6 §3](06-admin-crud-framework.md#3-containment--children-reorder-move).
> Reorder is Up/Down buttons (keyboard-accessible) rather than drag; moving a child to another parent is
> its relation field on its own form (or MCP `move_child`), not a panel action. Content zone items became the
> top-level `contentzoneitems` handler, with their containment hidden from the panel.

- A generic **children panel** on the parent's edit screen is driven by the declarations that target
  the parent's type. It lists the children, offers "New {child}", and supports reorder when `Ordered`
  and move to another parent.
- It replaces `IAdminCrudChildHandler` and the `/wadmin/{contentType}/{parentKey}/{childType}/…`
  routes, which only zones implemented (both are gone).
- **Articles:** the Article List edit screen lists its articles, so the `?list=` filtered index is no
  longer needed for nested editing (it may stay as a filter).
- **Zones:** the zone editor keeps its own drag-and-drop UI, built on the engine.

### Reference: picker plus modal

> **Implemented** (phase 2).

- The EntityPicker is single-select for `Guid` and **multi-select for `IEnumerable<Guid>`**.
- **New** and **Edit** buttons open the target type's upsert form in a **modal with its own save**:
  - The form is rendered without the admin layout (e.g. `GET /wadmin/{contentType}/create?modal=1`).
  - The modal save returns `{ id, title }` JSON; the picker adds or updates that entry.
  - The source form is not saved by the modal; the user saves it separately.
  - The modal is sized by `admin.css` → "Relation Modal" (`.relation-modal-content` up to 1100px wide,
    `.relation-modal-frame` the viewport height less a margin); without those rules the iframe falls back
    to the browser's 300×150px default.
- A **containment** picker on an existing item's form (`data-exclude-subtree`) asks its list for
  `?excludeSubtreeOf={nodeId}` (`IAdminCrudHandler.GetApiListExcludingSubtreeAsync`), so a handler can leave
  out the item and its descendants (the binder still refuses such a pick on save). `PageModel` does.
- Plain JS (no jQuery), in a `Views/**` script copied by `CopyViewScripts` (no inline script, per the
  CSP).

## 10. MCP and Seeding

> **Implemented** (phase 5) — see [Area 12 §5](12-mcp-server.md#5-tool-surface) and
> [Area 15 §10](15-content-seeding.md#10-zone-items). Zone items seed as `contentzoneitems` (the old
> `contentzones` + `zone` form is a deprecated alias); "any containment child" seeds through its relation
> field like any other top-level item.

- **MCP:** relations are ordinary `Guid` / `Guid[]` fields of the upsert view model, so
  `create_content`, `update_content` and `get_content` cover them with no new tools.
  `describe_content_type` advertises each relation's kind, target type, cardinality and `Required`.
  The child tools (`list_children`, `create_child`, `reorder_children`, `move_child`, …) become generic
  over containment declarations instead of `IAdminCrudChildHandler`.
- **Seeding:** relation fields carry `@seed:{guid}` references as today, resolved by
  `SeedReferenceResolver` with the deferred retry pass. The zone `zone` locator becomes sugar over the
  zone-item containment, and any containment child can be seeded the same way.

## 11. Migration and Impact

- **Pre-release, so InitialCreate is regenerated** (`./scripts/RebuildEFMigrations.sh`). No data
  migration is needed.
- **Dropped:** `Articles.ArticleListNodeId`, `ContentZoneItems.ContentZoneNodeId` / `Ordinal`, the
  `ContentZoneAssignments` table, `ContentNodes.ParentNodeId`, and the ownership role of
  `CMSRoutes.OwningContentNodeId` (it becomes projection metadata).
- **Added:** `ContentRelations`, plus a `Routes` table for the route content type.
- **Code most affected:** `ContentZoneService` / `PageZoneTree` (every zone query), `PageModel` (parent,
  routes, publish), `ArticleModel` / `ArticleListModel` (simplified), `JsonContentSeeder` (zone path),
  `AdminContentController` child routes, `ChildContentToolset`, `PageHistoryService`.
- **Tests:** the 100% line + branch gate applies to every phase. Zone and page-history tests are the
  largest rewrite.
- **TestHost submodule:** Faq config pickers and any host relations move to `[ContentRelation]`, and
  the host's `AddFaqs` migration loses its FK columns.

## 12. Implementation Sequencing

Each phase is its own plan and is shippable at the coverage gate:

1. **Engine + Article → Article List:** record, store copy, binder, registry, queries, containment
   delete and visibility, MCP describe, generic children panel (read-only list).
2. **Config pickers + reference modal:** config relations, multi-select picker, the modal create/edit
   flow.
3. **Routes as content:** route type, projection into `CMSRoutes`, owner publish cascade.
4. **Zones:** item → zone (ordered) and owner → zone; retire `ContentZoneAssignments`.
5. **Page → parent + generic containment admin:** versioned page parent; retire
   `IAdminCrudChildHandler` and the child routes.
6. **TestHost update.**

## 13. Alternatives Considered

- **Keep FK columns and add a metadata-driven engine** (attribute on the FK property, with EF metadata
  driving validation, delete and queries). It is the least disruptive, but it can't cover config-JSON
  references or zone ownership, has no single table for "who points at X", and each host still writes
  FK configuration and migration columns.
- **Rows as a derived index of values stored elsewhere** (DTO column / JSON stays the truth, and the
  engine extracts rows on save). It is uniform across columns and JSON, but stores every fact twice,
  and any write path that skips the extractor lets the two drift apart.
- **Node-owned relations.** Simpler, but a relation change would go live immediately and sit outside
  history and restore, unlike every other edit in the CMS.

## 14. Resolved Questions

These were open during design review. They are now decided; implementation lives in
[plans/content-relations](../../plans/content-relations/README.md).

1. **Routes' kind:** an **owned reference, many per owner**. The owner holds `List<Guid> RouteIds`;
   routes are deleted and published with it and edited through the modal. A route is `Derived` (slug +
   parent) or `Explicit` (a fixed pattern).
2. **References and history:** delete checks look at **current draft and published versions only**.
   Historical rows to a deleted target are kept and resolve to nothing. `TargetNodeId` has no database
   FK.
3. **Versioned page parent:** publishing a page whose route prefix changed **re-projects its live
   descendants' derived routes** in the same publish. Explicit routes are untouched.
4. **Page delete:** **cascades to the subtree** after a confirm that states the descendant count. The
   MCP result states it too.
5. **Hot path:** no relation cache. Relations are auto-included with `ContentVersion`, and referrer
   lookups use the `(TargetNodeId, Name)` index. Measure later.
6. **Variants:** relations are **per variant**, following the version.
