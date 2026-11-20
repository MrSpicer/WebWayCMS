# Area 17: Content Relations

Every content-to-content link in WebWayCMS — an article's list, a zone item's zone, a zone's owner, a
page's parent, a widget's picked image, a page's routes — is a **content relation**: one row type, declared
with one attribute, handled by one engine. A core or host content type declares a relation once and gets
storage, versioning, validation, delete policy, read visibility, admin editing, MCP and seeding from it.

**Namespaces:**
- `WebWayCMS.Data.Models` — `ContentRelation`, `RelationKind`, `RelationWrite`, `ContentVersionRelations`
- `WebWayCMS.Data.Services` — `IContentRelations`/`ContentRelations`, `ContainmentVisibility`,
  `RelationPredicates`, the relation baseline in `ContentStore`
- `WebWayCMS.Attributes` / `WebWayCMS.Forms` (Forms project) — `[ContentRelation]`, `ContentRelationKind`,
  `RelationDeletePolicy`, `RelationProperties` (reflection), read by `FormPropertyBuilder`
- `WebWayCMS.Services.Relations` (Core) — `RelationBinder`, `ContentRelationRegistry`,
  `ContentDeleteService`, `IContentDeleter`, `RelationKindMap`, the mapper's relation step

**Consumed by:** every content type (core and host), zones, routing, admin forms, MCP, seeding

---

## 1. The Record

The record lives in `WebWayCMS.Data` (see [Area 1](01-data-tier.md#contentrelation--content-to-content-links-owned-by-the-source-version)).
A relation's value is stored only here: content types have no FK columns or in-JSON ids for related content.

```csharp
public record ContentRelation
{
    public Guid Id { get; set; }

    /// <summary>The source version that owns this relation (Cascade: a version's relations go with it).</summary>
    public Guid SourceVersionId { get; set; }

    /// <summary>The target item, always a node, never a version. No DB FK: the engine enforces integrity (§5).</summary>
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

- **Version-owned.** Rows hang off `ContentVersion` as a `Relations` navigation (auto-included).
  Changing a relation mints a new version and stays a draft until publish. Restore, page revert, discard,
  page history snapshots and the read context (Published / Draft / Snapshot) all cover relations, because
  they select versions.
- **Its own table**, so "who points at X" is an indexed query.
- **Indexes:** `(SourceVersionId, Name)` and `(TargetNodeId, Name)`.
- **Node targets.** A relation means "the item", never "version 7 of the item". Which version of the
  target you get is decided by the read context (`ContentQueryExtensions.AtReadContext`).
- **Direction.** A relation is stored on the side whose edits should version it:
  - **containment:** on the child (a move is a child edit);
  - **reference:** on the referrer.
- **Per variant.** Relations follow the version, so each culture/segment variant has its own.

## 2. Declaring a Relation

A relation is declared with one attribute on a property:

```csharp
[ContentRelation("articlelists", Kind = ContentRelationKind.Containment, Required = true)]
public Guid ArticleListId { get; init; }
```

| Attribute property | Applies to | Meaning |
|---|---|---|
| `TargetContentType` (constructor) | all | The target's content type key, e.g. `"articlelists"` |
| `Kind` | all | `Reference` (default), `Containment` (parent–child), or `Owned` (a reference whose target lives, dies and publishes with the source) |
| `Required` | all | The save is rejected (on this field) until a target is chosen |
| `Ordered` | containment | The children are kept in an explicit order (the row's `Ordinal`) |
| `ShowInParentAdmin` | containment | Default `true`: the parent's edit screen lists these children. Off for structure with its own editor, such as zone items |
| `OnTargetDelete` | reference | `Restrict` (default) or `Detach` (§5) |

The attribute's kind is `ContentRelationKind` (Forms) and the stored row's is `RelationKind` (Data), with the
same values; `RelationKindMap` (Core) converts.

**Property shapes:**

- `Guid` is a single relation; `Guid?` is an optional single relation.
- `IEnumerable<Guid>` (or `List<Guid>`) is many, with order taken from the list.

**Where the attribute may appear:**

- content-type upsert view models;
- `WidgetConfiguration` subclasses (the relation is owned by the zone item's version);
- page configuration types (owned by the page's version).

On a configuration type, the relation name is prefixed `config.` (e.g. `config.ImageNodeId`).

**The attribute implies the form field.** `FormPropertyBuilder` reads `[ContentRelation]` and emits an
EntityPicker, multi-select for many; the picker's list endpoint is resolved by `EntityPickerEndpoint.Resolve`.
No `[FormProperty(FormComponent = "EntityPicker", …)]` is needed; `[FormProperty]` supplies label, order and
help text.

**Startup validation** (`ContentRelationRegistry`, built from the scanned assemblies) fails fast when:

- a containment property is not a single `Guid`/`Guid?` (a child has exactly one parent);
- the target content type key is not registered;
- two relations on one type share a `Name`;
- `Ordered` is set on a reference, or `OnTargetDelete` on a containment.

### Host example: `Faq → FaqTopic`

```csharp
public sealed class FaqUpsertViewModel : BaseContentViewModel
{
    [FormProperty(Label = "Topic", Order = 3)]
    [ContentRelation("faqtopics", Kind = ContentRelationKind.Containment)]
    public Guid? FaqTopicId { get; init; }
}
```

That is all the relationship code. The host's `FaqDTO` table holds only its own fields: no FK column, no
`HasOne<ContentNode>()`, and nothing relation-related in the host migration, because the relation table
belongs to the CMS. The TestHost's FAQ widget pickers are references (`Detach`); its configured page's FAQ
picker is a reference (`Restrict`). See [Area 14](14-host-extensibility.md#relations-to-other-content).

## 3. The Engine

Every write path goes through one engine.

### Storage (Data tier)

- `ContentStore` writes `Version.Relations` in the **same `SaveChanges`** as the version.
- Writes go through `ContentVersion.RelationWrites` (`SetRelation` / `SetRelations` /
  `ReplaceAllRelationsFrom` in `ContentVersionRelations`). The store starts each new version from a
  **baseline** — edit: the current draft's rows; restore: the historical version's rows; create: none —
  and replaces only the written names, so a save that never mentions a relation keeps it.
- `PageHistoryService` revert and discard copy relations; `PageSetEquality` includes them in its content
  comparison, so "pending changes" and history de-duplication see a relation change.

### Mapping and validation (Core)

- The **mapper** moves `[ContentRelation]` property values to and from the version (`RelationMapping`, run
  after every profile mapping), so content types need no per-type relation code.
- The **binder** (`IRelationBinder.ValidateAsync`) runs at the top-level save choke point,
  `AdminCrudModel.SaveUpsertAsync`, after `ModelValidator`. That covers the admin UI, MCP `create_content` /
  `update_content` and the JSON seeder. It:
  1. checks that each target exists as a current draft of the declared content type (so a draft target can
     be chosen);
  2. enforces `Required`;
  3. refuses a containment whose target's current-draft chain reaches the source ("can't be placed under
     itself or one of its descendants");
  4. reports a field error (`AdminSaveResult` with the property name) instead of a database error.
- **Config relations:** zone-item and page writes run the same binder over the configuration type, beside
  `WidgetConfigurationJson.Sanitize`. Relation properties are stripped from the stored config JSON
  (`ExtractRelations`), written as `config.*` rows (`ApplyRelations`), and hydrated back when the config is
  deserialized for render (`ContentZoneModel`, `CMSRouteTransformer`) and for the editor and MCP
  (`HydrateRelations`).

### Queries

Read-context aware queries live on the store:

- `IContentStore<T>.FindRelatedAsync(name, target)` — sources whose read-context version points at the
  target, ordered by `Ordinal`, then newest (e.g. "articles in this list", "items in this zone");
- `FindCurrentDraftsRelatedAsync`, `FindCurrentDraftsWithoutRelationAsync`, `CountCurrentDraftsByRelationAsync`
  — the admin equivalents over current drafts;
- the version's own `TargetOf` / `TargetsOf` for the forward direction.

`IContentRelations` answers the cross-type, **live-only** (current draft and published) questions the delete
policy and binder need: `GetLiveReferrersAsync(target, kind?)`, `GetLiveChildrenAsync(parent)`,
`GetLiveOwnedTargetsAsync(source)`, `GetCurrentDraftTargetAsync(source, name)` and `GetNodesAsync(ids)`.
There is no relation cache: relations are auto-included with `ContentVersion`, and referrer lookups use the
`(TargetNodeId, Name)` index.

## 4. Read Semantics

- **Containment:** a child is visible only while its parent is visible in the same read context, and this
  applies transitively (an item in a zone owned by an unpublished page is not visible).
  `ContainmentVisibility` filters every read-context `ContentStore` read (memoized per request; skipped in
  Draft; a snapshot-governed node counts as visible).
- **Reference:** a target that isn't visible resolves to nothing, and the source still renders. A widget
  whose picked item is unpublished shows without it.
- **Following the live target.** Editing and publishing a target changes what every referrer shows,
  because references point at nodes.
- **Snapshot mode:** a snapshot pins source versions, and their relation rows come with them.

## 5. Write and Delete Semantics

Deletes go through `ContentDeleteService` (`AdminCrudModel.DeleteAsync` / `TryDeleteAsync`; a model
implements only `DeleteSelfAsync`). Containment descendants (deepest first) and owned targets are deleted in
one `ICmsTransaction`; a lone node with nothing to cascade is deleted without the wrapping transaction.
Descendants are deleted by their `IContentDeleter` (every `AdminCrudModel`, plus explicit registrations).
The result's `DeletedDescendants` reaches the admin flash and MCP `delete_content`.

### Containment (parent–child)

- **Delete:** deleting a parent deletes its children recursively (a page delete deletes its child pages,
  zones and zone items).
- **Move:** moving a child to another parent is a new child version, a draft until published.
- **Order:** for an `Ordered` containment, the child's position is its row's `Ordinal`. A reorder mints
  versions only for the children that moved.

### Reference

Only referrers' **current draft and published** versions count. Historical rows that point at a deleted
target are kept and resolve to nothing on read or restore, which is why `TargetNodeId` has no database FK.

- **`Restrict` (default):** a target can't be deleted while a referrer's current draft or published version
  references it. The error names the referrers.
- **`Detach`:** deleting the target saves a new draft of each referrer without the relation. An upsert
  relation is cleared on the referrer's upsert view model and saved through its handler. A **config
  relation** (`config.*`) is handled by `ContentDeleteService.DetachAsync`: a zone item's is written off its
  current draft (`ContentVersion.SetRelations`) and saved with `IContentZoneService.UpdateItemAsync` (a new
  version, recorded in page history); a page's is removed from the configuration JSON its upsert view model
  hydrates and saved through the page handler. All of it runs inside the delete's transaction, so a failed
  detach deletes nothing.

### Owned reference

The target is deleted with its source and published with it. Routes use this (§7).

**Soft delete** (`IsDeleted`) is always allowed: rows stay valid, and every read filters the node out.

## 6. Built-in Relations

| Link | Kind | Owned by (source version) | Target | Relation name |
|---|---|---|---|---|
| Article → Article List | Containment, `Required` | article | list node | `ArticleListId` (`ArticleModel.ListRelation`) |
| Zone item → zone | Containment, `Ordered`, `ShowInParentAdmin = false` | zone item | zone node | `ContentZoneNodeId` (`ZoneRelations.Zone`) |
| Owner → zone (slot) | Containment | zone | page or zone-item node | `ZoneRelations.Owner`, `Qualifier` = slot |
| Page → parent page | Containment | page | parent page node | `ParentNodeId` (`PageModel.ParentRelation`) |
| Widget / page config pickers | Reference | zone item / page | picked node | `config.*` |
| Page → routes | Owned | page | route node | `RouteIds` |

Notes:

- **Zone item → zone.** `ContentZoneItemDTO.ContentZoneNodeId` / `Ordinal` are in-memory accessors over the
  relation (ignored by EF). Queries use `ZoneRelations.InZone/InZones/ZoneOf/ZoneOrdinal`; writes use
  `ZoneRelations.Place` / `Placed`.
- **Owner → zone.** Lazy slot creation (`GetOrCreateByPageSlotAsync` / `GetOrCreateByItemSlotAsync`) mints
  zone v1 together with its owner relation, on the deterministic node id `ZoneNodeIds.For(owner, slot)` (a v5
  Guid), so the `ContentNodes` primary key is the race guard. A zone has one owner. A container carries its
  zones when it moves, because the zone points at the item node. Ownership is read from the zone's current
  draft (`PageZoneTree.ZoneOwners()`); whether the owner is a page or an item comes from the owner node's
  content type.
- **Page → parent.** Moving a page (the page form's Parent Page picker) is a draft until publish. Route
  derivation uses the **published** parent; the admin tree uses current drafts.

## 7. Routes as Content

A route is a versioned content type, `routes`:

```csharp
public record RouteDTO : IVersionedContent
{
    public Guid VersionId { get; set; }
    public ContentVersion Version { get; set; } = new();
    public RouteMode Mode { get; set; }          // Derived (slug + parent) or Explicit (a fixed pattern)
    public string Pattern { get; set; } = string.Empty;
    public string? NavigationName { get; set; }
    public int Order { get; set; }
}
```

- **Relation:** a page owns its routes through `[ContentRelation("routes", Kind = Owned)] List<Guid> RouteIds`
  on its upsert view model. Routes are edited through the reference modal (§8), so a user configures a
  route while creating or editing the page. A new page mints one Derived route.
- **Drafts until the owner publishes.** Publishing the owner publishes its route drafts, the same way a page
  publish cascades to its zone items.
- **Projection.** `CMSRoutes` is the transformer's fast lookup table, a projection of published route
  versions:
  - The owner's publish writes the projection; unpublish removes it (the route items stay as drafts);
    deleting the owner deletes its route items.
  - The collision rules of `CMSRouteService.UpsertAsync` (keyed on route node; reserved names) apply.
  - **One owner.** A route item has exactly one owner: `UpsertAsync` refuses a row for a route item whose
    row belongs to another owner, and the page save refuses a `RouteIds` entry another page's current
    draft owns (field error on `RouteIds`).
  - **All or nothing.** The owner's publish, its route items' publishes, the projection and the re-projection
    of live descendants' derived routes share one `ICmsTransaction`; a collision, a missing route item or a
    colliding descendant rolls the whole publish back. Descendants are re-projected from their **published**
    page and route versions only; explicit routes are untouched.
- **Projected rows are read-only** in the `cmsroutes` admin; edit them from their owner.
- **What is not route content:** widget routes (`IRoutableViewComponent`, registered at page publish) and
  code-based routes (`[CmsRoute]`, `CmsRouteSeeder`). They are written straight into `CMSRoutes`.

## 8. Admin UX

### Containment: children on the parent's screen

See [Area 6 §3](06-admin-crud-framework.md#3-containment--children-reorder-move).

- A generic **children panel** on the parent's edit screen is driven by the declarations that target the
  parent's type (`ShowInParentAdmin = false` opts out). It lists the children, offers **New {child}** (which
  preselects the parent), and Up/Down reorder (keyboard-accessible) for an `Ordered` containment.
- Moving a child to another parent is its relation field on its own form (or MCP `move_child`).
- **Articles:** the Article List edit screen lists its articles; `/wadmin/articles?list={id}` remains as a
  filter.
- **Zones:** the zone editor has its own drag-and-drop UI, built on the engine.

### Reference: picker plus modal

- The EntityPicker is single-select for `Guid` and **multi-select for `IEnumerable<Guid>`**.
- **New** and **Edit** buttons open the target type's upsert form in a **modal with its own save**:
  - The form is rendered with the minimal admin layout (`GET /wadmin/{contentType}/create?modal=1`).
  - The modal save returns `{ id, title }` JSON; the picker adds or updates that entry.
  - The source form is not saved by the modal; the user saves it separately.
  - The modal is sized by `admin.css` → "Relation Modal" (`.relation-modal-content` up to 1100px wide,
    `.relation-modal-frame` the viewport height less a margin); without those rules the iframe falls back
    to the browser's 300×150px default.
- A **containment** picker on an existing item's form (`data-exclude-subtree`) asks its list for
  `?excludeSubtreeOf={nodeId}` (`IAdminCrudHandler.GetApiListExcludingSubtreeAsync`), so a handler can leave
  out the item and its descendants (the binder still refuses such a pick on save). `PageModel` does.
- The client script is `relation-modal.js`, a `Views/**` source copied by `CopyViewScripts` (no inline
  script, per the CSP).

## 9. MCP and Seeding

See [Area 12 §5](12-mcp-server.md#5-tool-surface) and [Area 15 §10](15-content-seeding.md#10-zone-items).

- **MCP:** relations are ordinary `Guid` / `Guid[]` fields of the upsert view model, so `create_content`,
  `update_content` and `get_content` cover them. `describe_content_type` advertises each relation's kind,
  target type, cardinality and `Required`. The child tools (`list_children`, `create_child`,
  `reorder_children`, `move_child`, …) are generic over containment declarations.
- **Seeding:** relation fields carry `@seed:{guid}` references, resolved by `SeedReferenceResolver` with
  the deferred retry pass. Zone items seed as `contentzoneitems` with a `zone` locator, and any containment
  child seeds through its relation field like any other top-level item.
