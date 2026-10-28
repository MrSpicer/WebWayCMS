# Phase 5: Page Parent + Generic Containment Admin

**Goal:**

- Page → parent page becomes a versioned containment, and `ContentNode.ParentNodeId` goes away.
- Deleting a page cascades to its subtree, behind a confirm.
- Containment gets one generic admin and MCP surface: a children panel, a preselected create, and
  reorder/move. This retires `IAdminCrudChildHandler`, with zone items becoming a top-level handler.

## 0. Test impact review

Get approval for changes to:

- `PageModel` tests (parent resolution, `IsSlugAvailableAsync` siblings, tree build, delete);
- `ContentStore` tests for `GetChildrenAsync`/`GetRootsAsync`/`GetCurrentDraftChildrenAsync`/`ApplyNodeFields`;
- `AdminContentController` child-route tests;
- `ChildContentToolset` tests;
- `ContentZoneChildHandler` tests (moved to the new handler);
- `JsonContentSeeder` zone dispatch tests;
- `CMSRouteTransformer` tests (the invisible-page 404).

## 1. Page parent on relations

- **`PageUpsertViewModel.ParentNodeId`** (`Guid?`): `[ContentRelation("pages", Kind = Containment)]`.
  It is optional, because a root page has no parent. The page form now shows a parent picker, so a
  move is an ordinary edit, a draft until publish.
- **Generic cycle guard in `RelationBinder`:** when a containment's target type equals the source
  type, walk the target's current-draft containment chain. If it reaches the source, return
  "{Title} can't be placed under itself or one of its descendants."
- **`ContentNode.ParentNodeId`:** remove it, along with its index (`ContentNodeEntityConfiguration.cs:14`),
  `ApplyNodeFields`' parent copy, and `CreateDraftAsync`'s parent copy (`ContentStore.cs`).
- **`IContentStore`:** remove `GetChildrenAsync`, `GetRootsAsync` and `GetCurrentDraftChildrenAsync`.
  Callers use:
  - `FindRelatedAsync(PageModel.ParentRelation, parent)`;
  - `FindCurrentDraftsRelatedAsync(...)`;
  - a new `FindCurrentDraftsWithoutRelationAsync(name)` for roots.
- **`PageModel`:**
  - `ParentRelation = nameof(PageUpsertViewModel.ParentNodeId)`;
  - the parent is read via `Version.TargetOf(ParentRelation)` in `PublishPageAsync` (≈ 157),
    `BuildTree` / the unpublished-path walk (≈ 477–485), `PopulateParentageAsync` (≈ 603) and
    `IsSlugAvailableAsync` siblings (≈ 561);
  - `ResolveParentNodeIdAsync(parentRoute)` (≈ 581) stays for the "New child page" link and now
    pre-fills the relation property;
  - the `MappingProfile` page map (≈ 225) drops the `Node.ParentNodeId` line, because the mapper hook
    handles it.
- **Route derivation** (`DeriveRoutePatternForPublishAsync`): the parent comes from the **published**
  page version. Phase 3's descendant rewrite covers the move: publishing a moved page changes its
  derived prefix, so its live descendants are re-projected.
- **Containment visibility consequence:** a published child of an unpublished parent is now hidden
  publicly. `CMSRouteTransformer` must not dispatch an owner-less "Page" route:
  - when `OwningContentType == "Page"` and `GetAsync` returns null, return `null` (404) instead of
    falling into the code-based branch (`CMSRouteTransformer.cs:97`);
  - widget routes whose `ParentPageNodeId` page is null do the same.

  This is required by the containment semantics, not an unrelated gap fix.

## 2. Page subtree delete with confirm

- `PageModel.DeletePageAsync` becomes `DeleteSelfAsync` (zones and routes for this page only). The
  generic `ContentDeleteService` deletes descendants deepest first. Their zones are containment
  children of each page (phase 4), and their routes are owned (phase 3).
- **Pages list delete button:** carry `data-descendant-count` from the tree build. `PageUpsert.js` /
  the Pages index script shows "Delete '{title}' and its {n} child page(s)?". The count includes all
  depths. The script stays in its file; no inline handler.
- **MCP `delete_content`:** the result includes `deletedDescendants`. The tool description says that
  deleting a page deletes its subtree.

## 3. Generic containment admin

- **Registry:** `ContentRelationRegistry.GetContainmentsTargeting(contentType)` returns the
  declarations whose `Kind == Containment` and whose `TargetContentType` is the given type.
- **Handler surface** (`IAdminCrudHandler`, with default implementations in `AdminCrudModel`):
  - `Task<IReadOnlyList<object>> GetRelatedChildrenAsync(string relationName, Guid parentNodeId, ct)`:
    `{ id, title, state, ordinal }` over current drafts;
  - `Task<(bool, string?)> ReorderRelatedAsync(string relationName, Guid parentNodeId, IReadOnlyList<Guid> ordered, ct)`:
    allowed only when `Ordered`; mints versions for moved children only.
- **Preselect on create (generic):** `AdminCrudModel.GetUpsertViewModelAsync(null, query)` copies any
  query key that matches a relation property name (e.g. `?ArticleListId={id}`) into the new view
  model. This replaces `ArticleModel.ListQueryKey`/`ParseListFilter`. Keep `?list=` as an index filter
  alias only if the Articles index still uses it.
- **Children panel:**
  - `Views/AdminShared/_RelationChildren.cshtml` is rendered by `AdminContentController.Edit` GET under
    the form, once per containment declaration targeting the type;
  - it lists the children with an edit link and state, a "New {child}" link
    (`/wadmin/{childType}/create?{relationName}={parentId}`), and drag/keyboard reorder when `Ordered`;
  - reorder posts to
    `POST /wadmin/{childType}/relations/{relationName}/{parentNodeId}/reorder`;
  - script: `Views/AdminShared/relation-children.js` (added to `CopyViewScripts`).
- **Zones are excluded** from the panel. `contentzones` keeps its zone editor, and page edit screens
  don't list zones as children: a declaration-level `ShowInParentAdmin = false` on the zone relations.
- **Articles:** the Article List edit screen now lists its articles. The Article Lists index keeps its
  count column.

## 4. Retire `IAdminCrudChildHandler`

- **New `ContentZoneItemModel : IAdminCrudHandler`**, key `contentzoneitems`. It moves
  `ContentZoneChildHandler`'s logic out of `ContentZoneModel.cs:556` and delegates every write to
  `IContentZoneService` / `ContentZoneModel`, so page-draft semantics, history recording, slot remap
  and config sanitize/extract all stay. Its upsert view model declares
  `[ContentRelation("contentzones", Kind = Containment, Ordered = true, ShowInParentAdmin = false)] Guid ContentZoneNodeId`.
- **Delete:**
  - `IAdminCrudChildHandler.cs`;
  - `IAdminCrudHandler.ChildHandler` and every `ChildHandler => null` (`CMSRouteModel.cs:120`,
    `FormComponentRegistrationModel.cs:101`, `AdminCrudModel.cs:76`);
  - the `/{parentKey}/{childType}` actions in `AdminContentController.cs:338–545`;
  - `ContentZoneChildHandler`.
- **Zone admin views** `ContentZoneItems.cshtml` / `ContentZoneItemUpsert.cshtml` move to the new
  handler's `IndexViewPath`/`UpsertViewPath`. Links in the zone editor and history pages (child
  history with `returnUrl`) are updated to `/wadmin/contentzoneitems/...`.
- **MCP `ChildContentToolset`, generic over containment.** The tool names stay. Arguments become
  `parentType`, `parentKey` (node id), `childType` (a content type key) and `relation` (optional when
  only one containment links the two types):
  - `list_children` → the child handler's `GetRelatedChildrenAsync`;
  - `create_child` → `create_content` on the child type, with the relation field set;
  - `get_child`, `update_child`, `delete_child`, `list_child_versions` and `restore_child_version` →
    the child type's ordinary handler operations;
  - `reorder_children` → `ReorderRelatedAsync`;
  - `move_child` → an update of the containment field, plus position for ordered relations (zone items
    use `IContentZoneService.MoveItemAsync`, which keeps the version check).

  This is a hard break of the argument names (pre-release). Update the tool descriptions and
  `docs/architecture/12-mcp-server.md`.
- **Seeder:**
  - zone items become `contentType: "contentzoneitems"`;
  - the `zone` locator resolves to the `contentZoneNodeId` field, using the same
    `GetOrCreateBy*SlotAsync` resolution and going through ordinary top-level dispatch;
  - `JsonContentSeeder.ApplyZoneItemAsync` keeps its zone-specific rules (publish only on a live page,
    skip `publish:false` in global zones, owner validation) and drops the child dispatch;
  - accept the old `contentType: "contentzones"` with a `zone` locator as an alias, logging a
    deprecation warning, so the shipped and TestHost seeds can move in phase 6.

## 5. Migrations, docs

- `RebuildEFMigrations.sh`: drops `ContentNodes.ParentNodeId`.
- **Docs:**
  - `CLAUDE.md`: Content Versioning (the page parent is a versioned relation; page delete cascades),
    Host Extensibility (the child-handler bullet goes; describe the containment panel), Content Seeding
    (the zone item content type), MCP;
  - `06-admin-crud-framework.md` (child handlers → containment), `03-page-routing.md`,
    `15-content-seeding.md`, `12-mcp-server.md`;
  - doc 17 §7 page row and §9 containment UX: implemented.

## Verification

- Unit coverage 100% (Data, Core, Routing, Mcp, Host if it's touched).
- Integration host:
  1. Move page `intro` from `docs` to `guides` with the parent picker. The live URL is unchanged until
     publish. After publish `/guides/intro` resolves, and so do its descendants'.
  2. Try to move `docs` under its own child: field error.
  3. Unpublish `docs`: `/docs/intro` returns 404, not a blank page.
  4. Delete `docs`, which has two levels of children. The confirm states the count. Afterwards no
     descendant pages, zones, items, routes or relation rows remain (`psql` checks).
  5. The Article List edit screen lists its articles. "New article" preselects the list.
  6. Zone item admin pages and child history links still work under `/wadmin/contentzoneitems`.
  7. MCP: `list_children parentType=articlelists childType=articles`, `create_child` of a zone item
     into a page zone, `move_child` with a stale `expectedVersionNumber` (409/stale message), and
     `reorder_children` on a zone.
  8. Boot with the unchanged seed files: the alias works and logs the deprecation warning once per
     item.
