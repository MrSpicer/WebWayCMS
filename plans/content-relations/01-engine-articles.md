# Phase 1: Relation Engine + Article → Article List

**Goal:** the complete relation engine (record, store, declarations, binding, queries, visibility,
delete), with Article → Article List as the first containment moved onto it. Afterwards a host can
declare `Faq → FaqTopic` with one attribute.

## 0. Test impact review (before any code)

List the existing tests this phase changes and get human approval. Expected:

- `tests/WebWayCMS.Data.Tests`:
  - `ContentStore` tests that assert version clone contents (the new relation copy);
  - `PageSetEquality` / `PageHistoryService` tests (relations join the comparison).
- `tests/WebWayCMS.Core.Tests`:
  - `ArticleModel` / `ArticleListModel` tests: the list check, `MapIfListVisibleAsync`, the delete
    cascade and `ArticleListNodeId` all go away;
  - `MappingProfile` article map tests.
- `tests/WebWayCMS.Mcp.Tests`: `describe_content_type` field-shape tests, if they snapshot the article
  fields.

## 1. Forms: declaration types (`WebWayCMS.Forms`)

New files in the `WebWayCMS.Attributes` namespace (next to `FormPropertyAttribute`):

- `RelationKind.cs`: `Containment`, `Reference`, `Owned`.
- `RelationDeletePolicy.cs`: `Restrict`, `Detach`.
- `ContentRelationAttribute.cs`:
  - required constructor argument `TargetContentType` (string);
  - `Kind` (default `Reference`);
  - `Required`;
  - `Ordered` (containment only);
  - `OnTargetDelete` (reference only; default `Restrict`).

New files in `WebWayCMS.Forms`:

- `RelationValue.cs`: `readonly record struct RelationValue(string Name, Guid TargetNodeId, int Ordinal, string Qualifier)`.
- `RelationProperties.cs` (static, reflection, cached per `Type`):
  - `Describe(Type)` returns `IReadOnlyList<RelationPropertyInfo>`. Each entry carries `PropertyInfo`,
    `Name` (= property name), the attribute, `IsMany` (`IEnumerable<Guid>` but not `string`) and
    `IsOptional` (`Guid?`).
  - `Read(object model)` returns `IEnumerable<RelationValue>`. It skips `Guid.Empty` and nulls; for
    many, `Ordinal` is the list index.
  - `Write(object model, IEnumerable<RelationValue>)` sets the properties. It handles `init` setters,
    `Guid?` and `List<Guid>` / `Guid[]` / `IEnumerable<Guid>`.

Edits:

- `FormPropertyInfo`: add `IsMultiple`, `RelationKind?`.
- `FormPropertyBuilder.BuildPropertyInfos`: when `[ContentRelation]` is present, apply these defaults
  where `[FormProperty]` doesn't set them:
  - `FormComponent = "EntityPicker"`;
  - `EntityType = TargetContentType`;
  - `EditorType = Guid`;
  - `IsRequired |= Required`;
  - `IsMultiple = IsMany`;
  - `RelationKind = Kind`.

Tests (`WebWayCMS.Forms.Tests`): `RelationProperties` for every property shape and for skip rules;
`FormPropertyBuilder` defaults and overrides.

## 2. Data: the record and its storage (`WebWayCMS.Data`)

- `Data/Models/ContentRelation.cs` (shape as in doc 17 §2, plus `Kind`):
  - `Id`, `SourceVersionId`, `TargetNodeId`, `Name` (max 128), `Ordinal`, `Qualifier` (max 256,
    `""` default), `Kind` (`RelationKind`).
  - Data can't reference Forms, so **`RelationKind` moves to Data**, as
    `WebWayCMS.Data.Models.RelationKind`.
  - Forms gets its own enum `ContentRelationKind`, and Core maps between the two. *Alternative if
    preferred: keep one enum in Forms and have Data store an `int`.* Pick one during review. The plan
    assumes two enums with a Core mapping.
- `ContentVersion`:
  - Add `public List<ContentRelation> Relations { get; set; } = new();`.
  - Add the write-side state `RelationWrites`, an `IDictionary<string, IReadOnlyList<RelationWrite>>`
    that is not mapped. `RelationWrite(TargetNodeId, Ordinal, Qualifier, Kind)`.
  - Add helpers on a new `ContentVersionRelations` static extension class:
    - reads: `TargetOf(name)` and `TargetsOf(name)`;
    - writes: `SetRelation(name, Guid? target, kind, qualifier = "")`,
      `SetRelations(name, IEnumerable<Guid>, kind)` and `ReplaceAllRelationsFrom(IEnumerable<ContentRelation>)`.
      The last one writes every name in the source set and clears names that appear in the baseline
      but not in the source.
- `EntityConfiguration/ContentRelationEntityConfiguration.cs`:
  - table `ContentRelations`;
  - `HasOne<ContentVersion>().WithMany(v => v.Relations).HasForeignKey(r => r.SourceVersionId).OnDelete(Cascade)`;
  - **no FK on `TargetNodeId`** (settled decision 7);
  - indexes `(SourceVersionId, Name)` and `(TargetNodeId, Name)`.
- `ContentVersionEntityConfiguration`: `Navigation(v => v.Relations).AutoInclude()` and
  `Ignore(v => v.RelationWrites)`.

## 3. Data: the store engine (`ContentStore.cs`)

- **`BuildVersion` gains a `baseline` parameter** (`IReadOnlyCollection<ContentRelation>`). The new
  version's `Relations` are:
  1. the baseline rows whose `Name` is not in `source.RelationWrites`,
  2. plus one row per write,
  3. each with a new `Id` and `SourceVersionId = version.Id`.

  Callers pass:
  - `CreateDraftAsync`: empty;
  - `EditDraftAsync`: `currentDraft.Version.Relations`, captured **before** the entity/version swap,
    since identity resolution can make `entity` the same instance as `currentDraft`;
  - `RestoreAsync`: `historical.Version.Relations`, with `RelationWrites` empty, so a restore brings
    back exactly the historical relations.
- **Restore detaches and reinserts** the historical row. Detach its `Relations` too, so EF doesn't try
  to re-parent the old rows.
- `RelationWrites` is cleared after a successful save.
- **Hard delete:** `DeleteAsync` already loads the versions, and the auto-include tracks their
  relations, so the cascade removes them. Add a test for that on the InMemory provider.
- **New read methods on `IContentStore<T>`,** all using `AtReadContext` and the visibility filter
  (step 5):
  - `FindRelatedAsync(string name, Guid targetNodeId, ct)`: sources whose read-context version relates
    to the target, ordered by that relation's `Ordinal`, then `CreatedUtc` descending.
  - `FindCurrentDraftsRelatedAsync(string name, Guid targetNodeId, ct)`: the admin form.
  - `CountCurrentDraftsByRelationAsync(string name, ct)`: returns `Dictionary<Guid, int>` keyed by
    target (it replaces `CountCurrentDraftsByAsync(a => a.ArticleListNodeId)`).
- **`Services/RelationPredicates.cs`:**
  `RelatesTo<T>(name, target) => e => e.Version.Relations.Any(r => r.Name == name && r.TargetNodeId == target)`,
  plus an ordinal key selector for ordering.

## 4. Data: cross-type queries (`IContentRelations`)

`Services/IContentRelations.cs` and `ContentRelations.cs`, scoped and registered in both modes
(`CmsRenderingRegistration`):

- `GetLiveReferrersAsync(Guid targetNodeId, RelationKind? kind, ct)` returns
  `IReadOnlyList<RelationReferrer(SourceNodeId, ContentTypeKey, Name, Kind, OnCurrentDraft, OnPublished)>`.
  It looks only at **current draft and Published** versions (settled decision 7).
- `GetLiveChildrenAsync(Guid parentNodeId, ct)` is the same query filtered to `Kind == Containment`.
  The delete cascade walks it.

Tests (`WebWayCMS.Data.Tests`, InMemory):

- a historical-only reference is ignored;
- draft-only and published-only references are each found;
- kind filtering works.

## 5. Data: containment visibility

- **`Services/ContainmentVisibility.cs`** (scoped, internal to Data, created by `ContentStore`'s DI
  factory):
  - `FilterVisibleAsync<T>(IReadOnlyList<T> rows, ct)` drops a row when any of its `Containment` rows
    points at a node that is not visible in the read context. It applies recursively up the chain,
    with a per-request memo of `nodeId → visible`.
  - A target's visibility is one batched query per level: does the node have a version at the read
    context?
  - **Mode handling:**
    - `Draft`: no-op.
    - `Published`: the rule above.
    - `Snapshot`: nodes governed by the snapshot skip the filter; everything else uses the Published
      rule.
- **Apply it** in `GetAsync`, `GetAllAsync`, `GetBySlugAsync`, `FindAsync`, `FindRelatedAsync` and
  `GetChildrenAsync`/`GetRootsAsync`. Single-row reads return null when filtered.
- **`AddContentStore<T>`** (`CmsRenderingRegistration.cs:103`) passes the new dependency to the
  `ContentStore<T>` constructor.

Tests:

- a child of an unpublished parent is hidden in Published mode and shown in Draft mode;
- the rule is transitive (grandparent);
- the memo issues one query per node.

## 6. Data: history and equality

- `PageSetEquality.PagesEqual` / `ItemsEqual` / `ItemContentEqual` include relations, compared as a
  sorted list of `(Name, Ordinal, TargetNodeId, Qualifier, Kind)`.
- `PageHistoryService.ApplyPageAsync` and the item counterpart call
  `version.ReplaceAllRelationsFrom(target.Version.Relations)`, so revert and discard restore relations.

## 7. Core: declarations, binding, mapping, delete

- **`Services/Relations/ContentRelationRegistry.cs`** (singleton, admin mode):
  - **Sources:**
    - each `IAdminCrudHandler.CreateEmptyUpsertViewModel().GetType()`, keyed by the handler's
      `ContentType`;
    - from phase 2 on, widget configuration types (`IWidgetRegistry`) and page configuration types
      (`IPageControllerRegistry`).
  - **It throws at first resolution** (the same precedent as duplicate handler keys) when:
    - a containment property is not `Guid`/`Guid?`;
    - `Ordered` is set on a non-containment, or `OnTargetDelete` on a containment;
    - the target type key is not registered in the handler registry (resolved by exact key, then
      plural, as `EntityPickerEndpoint.Resolve` does);
    - a type declares two relations with the same name.
  - **Lookup:** `Get(sourceContentType)` and `GetPolicy(sourceContentType, name)`.
- **Mapper hook (`WebWayCMS.Core/Mapping/Mapper.cs`):** after the profile lambda runs:
  - if the source has relation properties and the destination is `IVersionedContent`, call
    `dest.Version.SetRelation(s)` from `RelationProperties.Read`. Clearing counts: an empty value
    writes the name with no targets;
  - if the source is `IVersionedContent` and the destination has relation properties, call
    `RelationProperties.Write` from `source.Version.Relations`.

  Add a Core mapping between `ContentRelationKind` and `RelationKind`.
- **`Services/Relations/RelationBinder.cs`:** `ValidateAsync(object model, ct)` returns
  `AdminSaveResult?`.
  - Each target must exist as a current draft whose `ContentTypeKey` matches the declared target.
    There is a new `IContentRelations.GetNodeTypesAsync(IEnumerable<Guid>)` for the batch.
  - `Required` must be met.
  - The first failure is returned with `ErrorField` = the property name, and a message from the
    declaration: "Choose the {label} this {type} belongs to." for a containment, "{label}: the
    selected item no longer exists." for a reference.
- **`AdminCrudModel.SaveUpsertAsync`:** call the binder after `ModelValidator.Validate`. The binder is
  injected through the `AdminCrudModel` constructor alongside `IChangeSetScope`; every subclass
  constructor gains the parameter, as host models do (call this out in doc 14).
- **`Services/Relations/ContentDeleteService.cs`:**
  `DeleteAsync(Guid nodeId, Func<Guid, CancellationToken, Task<bool>> deleteSelf, ct)` returns
  `(bool Success, string? ErrorMessage, int DeletedDescendants)`. Inside one `ICmsTransaction`:
  1. Collect descendants breadth-first via `GetLiveChildrenAsync`.
  2. For the node and each descendant, check `GetLiveReferrersAsync(kind: Reference)` for referrers
     outside the deletion set:
     - `Restrict`: fail with "{title} is used by {n} item(s): …";
     - `Detach`: save a new draft of each referrer without the relation, change note
       "Removed reference to {title}".
  3. Delete owned targets (`Kind == Owned`) of each deleted node.
  4. Delete deepest first through the owning handler's `DeleteSelfAsync`.
- **`AdminCrudModel`:**
  - `DeleteAsync` becomes non-virtual: `_deletes.DeleteAsync(id, DeleteSelfAsync, ct)`.
  - A new `protected virtual Task<bool> DeleteSelfAsync(Guid id, ct)` holds what the old overrides
    did.
  - **`IContentDeleter { string ContentTypeKey; Task<bool> DeleteSelfAsync(Guid, ct); }`** is
    registered per content type key (`IEnumerable<IContentDeleter>`).
    - Every `AdminCrudModel` is one; register it alongside the `IAdminCrudHandler` registration.
    - Non-handler types contribute their own: zone items in phase 4, until they become a handler in
      phase 5.
    - The service resolves a descendant's deleter by its node's `ContentTypeKey`. A missing deleter
      fails the whole delete with "No deleter registered for '{key}'." and rolls back.
  - The admin and MCP delete responses carry `DeletedDescendants`.

## 8. Article → Article List

- **`ArticleDTO`:** remove `ArticleListNodeId`. **`ArticleDTOEntityConfiguration`:** remove the FK and
  its index.
- **`ArticleUpsertViewModel.ArticleListId`:**
  `[ContentRelation("articlelists", Kind = Containment, Required = true)]` plus
  `[FormProperty(Label = "Article List", HelpText = …, Order = 3)]`. Drop `EntityType`, `FormComponent`
  and `EditorType`, which are now implied.
- **`MappingProfile`:**
  - remove the `ArticleListId`/`ArticleListNodeId` lines from the upsert maps (the hook handles them);
  - `ArticleItemViewModel.ArticleListId` and `ArticleViewModel` read
    `s.Version.TargetOf(ArticleModel.ListRelation) ?? Guid.Empty`.
- **`ArticleModel`:**
  - add `public const string ListRelation = nameof(ArticleUpsertViewModel.ArticleListId);`;
  - remove the list check from `SaveUpsertCoreAsync` (the binder does it) but keep the global slug
    check;
  - remove `MapIfListVisibleAsync` (the visibility filter does it);
  - `GetListAsync` uses `_store.FindRelatedAsync(ListRelation, listId)`;
  - the index filter uses `FindCurrentDraftsRelatedAsync`;
  - `DeleteAsync` becomes `DeleteSelfAsync`.
- **`ArticleListModel`:**
  - delete `DeleteArticleListAsync`'s manual loop; `DeleteSelfAsync` deletes the list only, and the
    cascade service deletes the articles;
  - `IArticleListModel.DeleteArticleListAsync` delegates to `DeleteAsync`;
  - counts use `CountCurrentDraftsByRelationAsync(ArticleModel.ListRelation)`.
- **MCP descriptions** (`ContentToolset` `list_content` / `list_secondary_content` text,
  `ChildContentToolset` text): drop the stale "articles are children" wording.
- **Seed:** the default seed's article `articleListId: "@seed:…"` keeps working, because it is a plain
  view-model field. Check the TestHost `contentseed/` and `Fixtures/*.contentseed.json` and the Core
  test fixture `sample.contentseed.json`.

## 9. MCP: describe relations

- `McpToolHelpers.DescribeFields`: for relation fields add `relation: { kind, target, many, required }`
  to the field description (`McpResults` gets a nullable `McpRelationDescription`).

## 10. Migrations, docs

- `./scripts/RebuildEFMigrations.sh`.
- **Docs:**
  - `CLAUDE.md`: Articles (the list check, cascade and visibility are now generic), Content
    Versioning (relations carried and copied), Host Extensibility (the `[ContentRelation]` bullet and
    the constructor parameter);
  - `docs/architecture/01-data-tier.md`, `05-content-domain-models.md`, `06-admin-crud-framework.md`,
    `14-host-extensibility.md` (replace the FK example), `12-mcp-server.md`;
  - doc 17: mark §2–§6 and the Article row of §7 implemented.

## Verification

- Unit coverage at 100% in Forms, Data, Core and Mcp.
- Integration host:
  1. Create two article lists and an article in list A, then publish all three. The article renders
     on the public page.
  2. Unpublish list A. The article is hidden publicly and still visible in preview.
  3. Move the article to list B (a new version). Restore its previous version: the article is back in
     A.
  4. Delete list A. The article is **not** deleted, because its current versions point at B, and the
     delete **succeeds**, because the historical rows are ignored.
  5. Delete list B. Its articles go with it, and the response states the count.
  6. MCP: `create_content` on `articles` with a bogus `articleListId` returns a field error, not a 500.
     `describe_content_type articles` shows `relation.kind = containment`.
- Write a host-style `Faq → FaqTopic` test fixture in `WebWayCMS.Core.Tests` (types declared in the
  test project) proving a type needs only the attribute.
