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
