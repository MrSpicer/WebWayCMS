# Phase 3a: Review Fixes for Phases 2–3

A `/code-review` of the uncommitted phase 2 and phase 3 work (2026-10-28) reported the ten defects
below. **None of them has been re-verified yet.** Do this phase before phase 4: phase 4 moves zones
onto relations and builds on the zone-item write path (1) and the delete service (2).

Every fix starts with a test that fails for the stated reason. If the test cannot be made to fail,
the finding is wrong: record that in this file and move on. New tests only. Existing passing tests
follow the README's test-impact rule.

## 0. Test impact review

List the existing tests each fix must change, and get a human's approval before editing them.
Expected: the `ContentZoneApiController` and `ContentZoneModel` tests that mock
`IContentZoneService.UpdateItemAsync` (1), the `ContentDeleteService` detach tests (2), and the
`PageModel` publish/projection tests (3, 4, 7, 8, 9).

## Decisions (2026-10-28, with the human)

- **7:** one `ICmsTransaction` around the page publish, its route projection and the descendant
  re-projection. `CMSRouteService.UpsertAsync` joins an ambient transaction. `PageModel` also takes
  `ICMSRouteRegistry` and invalidates it after the transaction (the route service invalidates before
  commit). The legacy no-route-items path keeps its compensation.
- **8:** a child re-projection failure rolls back the whole publish (in that transaction).
- **4:** a missing route item fails the publish, naming the route; revert/discard are unchanged.
- **Tests approved for editing:** `PageRouteProjectionTests.Publish_DerivedPrefixChange_ReprojectsLiveDescendants`
  (child fixture published), `Publish_ProjectionFailure_RevertsAndRestoresPreviousRoute` (rewritten for
  the rollback), and the two new constructor args on every `new PageModel(...)` in `PageModelTests` and
  `PageRouteProjectionTests`.

## 1. Config relation writes on a zone-item update are dropped

- **Where:** `WebWayCMS.Admin/Controllers/ContentZoneApiController.cs:138`,
  `WebWayCMS.Core/Models/ContentZone/ContentZoneModel.cs:753` (`ApplyConfigRelations(updated.Version, …)`),
  `ContentZoneService.UpdateItemCoreAsync` / `MergeUpdate`.
- **Defect:** `ApplyRelations` writes `config.*` relations onto the caller's `AsNoTracking` copy of the
  version. `UpdateItemCoreAsync` loads the item again (`GetCurrentDraftAsync`), and `MergeUpdate(existing,
  item)` copies only the content fields and keeps the new copy's `Version`, so it has no `RelationWrites`.
  The store's overlay then carries the old relations forward. The phase 1 memory note predicted this.
- **Symptom:** in the zone editor or MCP `update_child`, changing an Image widget's image or an Article
  widget's `ArticleId`/`ArticleListId` saves successfully but keeps the old target. Clearing the picker
  does nothing.
- **Fix:** have `MergeUpdate` carry the incoming version's `RelationWrites` onto the reloaded version
  (or apply the writes inside the service, after the reload). One place, so every caller (zone editor
  config save, child form, MCP `update_child`, `restore_child_version`, the seeder, the slot-remap
  overloads) gets it.
- **Tests:** a Data test against the InMemory provider: update an item whose version carries a relation
  write and assert that the new version has the new relation. The existing tests mock the service, so
  they cannot catch this.

## 2. Detaching a widget's config relation fails the whole delete

- **Where:** `WebWayCMS.Core/Services/Relations/ContentDeleteService.cs:148` (`DetachAsync`).
- **Defect:** the referrer's handler is looked up by `ContentNode.ContentTypeKey`. Widget config
  relations belong to `contentzoneitems`, which has no `IAdminCrudHandler` (`ContentZoneModel` is
  `contentzones`), so `GetHandler` returns null. Even with a handler, `RelationProperties.Read` on the
  upsert view model does not see `config.*` names, so a page-config `Detach` would save nothing and
  leave a dangling reference.
- **Symptom:** deleting an article that an Article widget points at (`config.ArticleId`,
  `OnTargetDelete = Detach`) fails with "'…' could not be updated to drop its reference to '…'", and
  nothing is deleted. Deleting an article list used by a list-mode Article widget fails the same way.
- **Fix:** give config relations their own detach path: load the referrer's current draft (zone item or
  page), remove the named `config.*` relation through `RelationWrites`, and save it through the owner's
  normal write path (`IContentZoneService.UpdateItemAsync` for a zone item, `PageModel` for a page), so
  it mints a version and records page history. Depends on 1.
- **Tests:** delete a target referenced by a zone item's config relation and by a page's config relation:
  the delete succeeds, and the referrer's new current draft has no relation of that name.

## 3. Descendant re-projection publishes drafts

- **Where:** `WebWayCMS.Core/Models/Page/PageModel.cs:346` (`ReprojectDescendantsAsync`).
- **Defect:** the loop goes through every descendant's **current draft** and upserts a live `CMSRoutes`
  row. It never checks that the child is published, and it uses the child's draft slug.
- **Symptom:** publish parent P with a renamed slug. A never-published child gets a live URL (it shows
  in `RouteNavigation` and takes the pattern), and a live child's unpublished slug change goes live
  before that child is published.
- **Fix:** re-project only descendants that are published, from their **published** page and route
  versions. This matches settled decision 8 ("every live descendant's derived routes").
- **Tests:** a never-published child gets no row; a live child with a draft slug change is re-projected
  with its published slug.

## 4. A publish can leave the page live with no URL

- **Where:** `PageModel.cs:284` (`ProjectRoutesAsync`).
- **Defect:** a related route item with no current draft is skipped with `continue`. The stale-row loop
  then deletes every previous projected row (and its route item) that was not re-projected, and the
  publish reports success.
- **Symptom:** a page-history revert or discard (`ReplaceAllRelationsFrom`) restores a `RouteIds` set
  whose route item an earlier publish's stale cleanup deleted. The next publish removes the page's live
  URL and reports success.
- **Fix:** fail the publish (and revert, see 7) when a related route item is missing, naming the route.
  Also decide, with the human, whether revert/discard should mint a fresh route item when the
  historical one no longer exists.
- **Tests:** publishing with a missing route item fails and keeps the previous rows.

## 5. An explicit route counts its own live row as a collision

- **Where:** `WebWayCMS.Core/Models/Route/RouteModel.cs:74`.
- **Defect:** the save calls `IsPatternAvailableAsync(pattern)` without `excludeNodeId`/`excludeRouteId`,
  so the route's own projected `CMSRoutes` row is a collision.
- **Symptom:** editing only `NavigationName` or `Order` of a published explicit route fails with "This
  route pattern is already in use by another content item."
- **Fix:** pass the route's node id so its own row (keyed by `RouteNodeId`) is excluded.
- **Tests:** saving a projected route with an unchanged pattern succeeds; another route's row still
  collides.

## 6. A projection can take over another page's route row

- **Where:** `WebWayCMS.Data/Data/Services/CMSRouteService.cs:132` (`UpsertAsync`).
- **Defect:** every row with the same `RouteNodeId` is added to the replace set without checking its
  owner. The binder checks only the target type, and the Routes picker
  (`RouteModel.GetApiListAsync`) lists every route draft.
- **Symptom:** page B selects page A's route item R. Publishing B projects R with owner B, and A's row
  is removed in the same transaction. Page A loses its URL with no error, which breaks the documented
  "never a silent steal" rule.
- **Fix:** two layers. In `UpsertAsync`, treat a row with the same `RouteNodeId` but a different
  `OwningContentNodeId` as a collision. In the binder or the page save, reject a `RouteIds` entry that
  another node already owns (an owned relation has one owner), and narrow the picker list to unowned
  routes plus the page's own.
- **Tests:** `UpsertAsync` with a foreign-owned `RouteNodeId` fails and leaves the row; saving a page
  with another page's route fails with a field error on `RouteIds`.

## 7. A failed projection leaves new rows live

- **Where:** `PageModel.cs:235` (`PublishPageAsync` failure path).
- **Defect:** on a projection failure the page's publish is reverted and only `previousRoutes` are
  re-upserted. Rows already upserted for new route items, and route items already published, stay.
- **Symptom:** on a first publish with two routes, route 1 projects, then route 2 collides. The page is
  back to unpublished, but route 1's row stays live, points at an unpublished page, takes the pattern
  and shows in navigation.
- **Fix:** on failure, remove every row this publish upserted that is not in `previousRoutes`, and revert
  the route items' publishes (`RevertPublishAsync` with each `VersionId`/`DisplacedVersionId`).
  Preferable, if the store and route service can share it: run the whole projection in one
  `ICmsTransaction` so a failure rolls back everything.
- **Tests:** the two-route first publish above leaves no rows and no published route versions.

## 8. A failed descendant re-projection is hidden

- **Where:** `PageModel.cs:395` (`ReprojectDescendantsAsync`).
- **Defect:** the loop returns early on a failed `UpsertAsync` without reporting it, and
  `PublishPageAsync` still reports success.
- **Symptom:** rename `/a` to `/b`; the first child's `/b/x` collides with another owner. The remaining
  descendants keep `/a/…` under a parent that now lives at `/b`, and the admin sees a success message.
- **Fix:** keep going past a failure, collect the failures, and return them the way the zone cascade
  does ("Page published; some child URLs failed: …"). Ask the human whether a failure should instead
  roll back the whole publish (that needs 7's transaction).
- **Tests:** with one colliding child, every other child is re-projected and the message names the
  failure.

## 9. A child's derived prefix depends on row order

- **Where:** `PageModel.cs:807` (`GetParentRoutePrefixAsync`).
- **Defect:** `routes.FirstOrDefault()` with no ordering. A page can now own several projected routes
  (Derived plus Explicit).
- **Symptom:** parent at `/about` with an explicit alias `/company`: a child may derive `/company/team`
  on one publish and `/about/team` on the next, and `derivedChanged` can trigger extra re-projections.
- **Fix:** use the parent's **Derived** route as the prefix; fall back to a defined order
  (`Order`, then pattern) only when it has none.
- **Tests:** a parent with a derived and an explicit route always gives the derived prefix.

## 10. A hidden slug match hides a visible one

- **Where:** `WebWayCMS.Data/Data/Services/ContentStore.cs:68` (`GetBySlugAsync`; `GetAsync` has the
  same shape).
- **Defect:** the containment filter runs after `FirstOrDefaultAsync`. Slug uniqueness is checked only
  between current drafts, so two published versions can share a slug.
- **Symptom:** if the first row with slug `news` belongs to an unpublished list, the visible article on
  another list renders `_NotFound`.
- **Fix:** fetch the candidates and return the first visible one (or filter in the query). For
  `GetAsync`, a node id has one row per read context, so confirm it is unaffected rather than
  changing it.
- **Tests:** two published articles share a slug, the first's list is unpublished: the second is
  returned.

## 11. Docs

- `CLAUDE.md`: Content Versioning (config-relation detach, route ownership "one owner", re-projection
  of live descendants only, partial-failure message).
- `docs/architecture/17-content-relations.md`: the detach path for config relations (§ delete) and the
  route-ownership rule (§8).
- `docs/architecture/03-page-routing.md`: publish failure handling and the derived-prefix rule.
- Update the `content-relations-progress` memory: the `MergeUpdate` note is resolved.

## Verification

- Every fix above has a test that failed before the fix and passes after.
- Integration host:
  1. Change an Image widget's image in the zone editor, save, preview: the new image shows. Clear an
     Article widget's article: the widget renders nothing.
  2. Delete an article an Article widget points at: the delete succeeds and the widget's draft has no
     article. Same for an article list used by a list-mode widget.
  3. Parent `docs` with a never-published child and a live child with a draft slug change. Rename and
     publish `docs`: the never-published child has no route, and the live child keeps its published slug
     under the new prefix.
  4. Edit only the navigation name of a published explicit route and publish: no collision.
  5. Pick another page's route in a page's Routes picker: the save is refused.
  6. First publish of a page with two routes where the second collides: no row for either remains.
  7. A colliding child during a parent rename: the publish message names it, and the other children
     moved.
  8. A parent with a derived and an explicit route: children always use the derived prefix.
- MCP: `update_child` with a changed `imageNodeId` reaches the stored relation; `delete_content` on a
  widget-referenced article succeeds.

## Outcome (2026-10-28)

All ten findings were real; each has a fix and new tests. Notes:

- **1, 2, 5, 6 (route service), 10:** the new tests were run against the old code first and failed for
  the stated reason. **3, 4, 6 (page save), 7, 8, 9:** the `PageModel` tests were written after the
  rewrite; each asserts a behaviour the old code demonstrably lacked (it skipped a missing route, read
  draft slugs, used `FirstOrDefault` for the prefix, had no owner check, and had no transaction).
- **10:** `GetAsync` is unaffected (`AtReadContext` yields one row per node), so only `GetBySlugAsync` changed.
- **6:** the picker is **not** narrowed. `GetApiListAsync` has no page context, and the multi-picker
  takes chip titles from that list, so dropping owned routes would show raw ids for the page's own. The
  route service and the page save refuse a foreign route instead.
- **2 (found on the way):** `PageModel.GetUpsertViewModelAsync` never hydrated page config relations into
  `ConfigurationJson`, so any page form save would have cleared them (no shipped page config declares one).
  It now hydrates, which is also what the page detach path edits.
- **7:** the ambient-transaction join in `CMSRouteService.UpsertAsync` can't be unit-tested (the InMemory
  provider always reports `CurrentTransaction == null`); it was verified on Postgres (integration items 6, 7).
- Verification: every integration item above passed over MCP/HTTP on a fresh TestHost (item 1 through
  MCP `update_child`; the zone editor's config save goes through the same `UpdateItemAsync`/`MergeUpdate`
  path but was not clicked through in a browser). Playwright: 93 passed, 10 failed — the 10 known stale
  specs (5 CSP, 5 FAQ), none new.
