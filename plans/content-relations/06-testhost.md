# Phase 6: TestHost Submodule + End-to-End

**Goal:**

- The `WebWayCMS.TestHost` submodule
  ([separate repo](https://github.com/MrSpicer/WebWayCMS.TestHost)) uses relations everywhere.
- The shipped seeds use the new zone item type.
- The whole feature is verified end to end.

The submodule is its own repo, so its changes are committed there by the human, not here.

## 0. Test impact review

The TestHost `e2e/` Playwright specs that assert route URLs, Faq picker markup or child admin URLs will
change. Get approval for the list first. The memory notes pinned quirks to keep.

## 1. Host types

- **`ViewComponents/FaqConfiguration.cs`** (lines 26 and 176):
  `[FormProperty(FormComponent = "EntityPicker", EntityType = "faqs")]` becomes
  `[ContentRelation("faqs")]` plus `[FormProperty]` for label and order. Use `List<Guid>` for any field
  that should be many, if a list-capable Faq config wants explicit picks.
- **`Pages/ConfiguredPageConfiguration.cs:128`:** the same.
- **Faq model** (the host `AdminCrudModel<FaqDTO>`, wherever it lives in the TestHost): update its
  constructor for the `RelationBinder` parameter (phase 1), and `DeleteAsync` becomes `DeleteSelfAsync`.
- **Showcase relation:** add a `FaqTopic` content type (`FaqTopicDTO`, entity configuration,
  `AddContentType<FaqTopicDTO>("faqtopics")`, model, views) and
  `[ContentRelation("faqtopics", Kind = Containment)] Guid? FaqTopicId` on the Faq upsert view model.
  It is the worked host example from doc 17 §3, and it proves the "one attribute" claim in a real host.
  **Ask before adding it.** It is new scope in the host repo; skip it if declined.
- **`Data/MySiteMigrationsDbContext`:** regenerate `AddFaqs` (plus `FaqTopics`) with
  `dotnet ef migrations add … --context MySiteMigrationsDbContext` after deleting the old migration
  (pre-release).
- **`Program.cs`:** model registrations are unchanged apart from any new `FaqTopicModel`.

## 2. Seeds

- **`contentseed/zones.json`** and every other seed with zone items: `contentType: "contentzones"`
  becomes `"contentzoneitems"`, which removes the phase-5 deprecation warnings.
- Picker ids inside `componentPropertiesJson` stay as they are, because phase 2's extract lifts them
  out.
- Do the same in `WebWayCMS.TestHost/Fixtures/faqs.contentseed.json` and the CMS test fixture
  `tests/WebWayCMS.Core.Tests/Fixtures/sample.contentseed.json`. The CMS repo ships no seed folder of
  its own; grep for `"contentType": "contentzones"` across both repos to be sure. Then **remove the
  seeder alias** added in phase 5 and its tests.

## 3. Playwright

Add specs for:

- the reference modal round trip (phase 2 verification 4);
- route editing through the modal and the descendant rewrite on rename (phase 3, items 2 and 4);
- a page move and a subtree delete with the confirm (phase 5, items 1 and 4);
- the Article List children panel (phase 5, item 5).

Update the existing specs on the approved list.

## 4. Docs

- `docs/architecture/14-host-extensibility.md`: replace the FK example with the `FaqTopic` relation
  (or a code sketch if `FaqTopic` was declined). Document the constructor parameter and
  `DeleteSelfAsync`.
- `docs/getting-started.md`: if it shows a host content type, update it.
- Doc 17: change the status line to "Implemented". Keep §13 (alternatives) as the rationale.
- `CLAUDE.md`: a final pass, so that nothing still describes FK columns, assignments, `ParentNodeId`
  or child handlers.

## Verification (final, whole feature)

1. `./scripts/RunTests.sh`: everything green at 100%.
2. `./scripts/StartIntegrationHost.sh`: a clean boot from an empty volume
   (`TearDownIntegrationhost.sh -v` first). The seed log has no warnings.
3. The full Playwright suite passes.
4. MCP sweep: run `describe_content_type` on every content type. Every relation field shows its
   `relation` block, and no field anywhere still uses `EntityPicker` without a relation.
   `grep -rn 'FormComponent = "EntityPicker"' --include=*.cs` over both repos returns only
   non-relation uses (there should be none).
5. Code sweep: no remaining references to `ArticleListNodeId`, `ContentZoneAssignment`,
   `ParentNodeId` (on `ContentNode`), `IAdminCrudChildHandler` or `ChildHandler` in source, tests or
   docs, apart from doc 17's "today" history.
6. Re-read every phase file and confirm each step and verification item was done.

## Outcome (2026-10-28)

Decisions taken with the user: add `FaqTopic`; the `FaqTopic` widget resolves `{topic}` through the relation;
copy `e2e/` from the `testhost-playwright` worktree into this checkout's submodule (with that worktree's
uncommitted `@Html.Raw` Faq views, csproj `DefaultItemExcludes` and `.gitignore`); delete the seeder alias
and its test; fix the 10 stale specs (5 CSP: `cdn.jsdelivr.net` dropped; 5 FAQ: answers render raw, as
sanitized rich text).

- **Host:** `faqtopics` (`FaqTopicDTO`/`FaqTopicDTOEntityConfiguration` → `FaqTopics`, `FaqTopicModel`,
  `Views/AdminFaq/FaqTopics.cshtml` + `FaqTopicUpsert.cshtml`); `FaqUpsertViewModel.FaqTopicId` is an
  **optional** containment (`Guid?`); `FaqModel.TopicRelation`, the FAQs list shows the topic. `FaqId` /
  `FeaturedFaqId` on the widget are `Detach` references, the configured page's `FeaturedFaqId` is `Restrict`
  (both policies exercised). `AddFaqs` regenerated: two tables, no relation column. Seed topics are
  `55555555-…` (`22222222-…` is taken by pages).
- **CMS fixes found by the new specs** (none had a test before):
  1. `WebWayCMS.Admin/Views/AdminRoute/` had no `_ViewStart.cshtml`, so the routes form (and the routes
     modal) rendered in the public site layout, whose footer covered the Save button.
  2. The Pages list never rendered `TempData["Success"]`, so "1 child URL(s) updated." and the subtree delete
     flash were invisible where those actions happen.
  3. `PageModel.GetApiListAsync` returned only root pages, so the Parent Page picker could not choose a nested
     page (new test `GetApiListAsync_ListsPagesAtEveryDepth_ParentFirst`).
- **Playwright:** `relations.spec.ts` (read-only: Article List and FAQ topic children panels, preselected New,
  topic counts/confirm, topic column, the relation picker) and `relations.write.spec.ts`, the suite's only
  writer, in a `writes` project that depends on `chromium` (so the read-only specs, which assert the seeded
  page list and navbar exactly, never see its pages) and sweeps every `E2E …` page/list before and after.
  A published page's form offers Unpublish, not Publish, so the spec publishes edits from the Pages list.
- **Verification:** `RunTests.sh` green at 100% (Core 1068); clean boot from an empty volume, seed log
  without warnings; Playwright 111/111, twice on the same stack; MCP `describe_content_type` on all 14 types
  (every relation field has its `relation` block), faqtopics → faqs `create_child`/`update_content`/
  `publish_content`/`list_children`, `delete_content` topic → `deletedDescendants: 1`; DB: no FAQ id left in
  zone-item or page config JSON (`config.FaqId` ×1, `config.FeaturedFaqId` ×2 rows). The only
  `FormComponent = "EntityPicker"` left is `FormPropertyBuilder` deriving it from the attribute.
- **Not done:** `get_registry_properties` reports `entityType` but no `relation` block for config pickers
  (true of core widgets too; not phase 6). No TestHost `List<Guid>` multi-select fixture (phase 2 item 5;
  optional here). Test names still saying `ChildHandler` (21 in `ContentZoneModelTests`, 1 in
  `CMSRouteModelTests`) await approval to rename.
