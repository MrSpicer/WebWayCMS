# Content Relations: Implementation Plan

Design: [docs/architecture/17-content-relations.md](../../docs/architecture/17-content-relations.md).
This folder turns that design into six phases. Each phase is shippable on its own and leaves
`./scripts/RunTests.sh` green at the 100% line + branch gate.

| Phase | File | Delivers |
|---|---|---|
| 1 | [01-engine-articles.md](01-engine-articles.md) | Relation record, store engine, declarations, binder, visibility, delete policy; Article → Article List migrated |
| 2 | [02-config-pickers-reference-modal.md](02-config-pickers-reference-modal.md) | Widget/page config pickers as references; multi-select picker; New/Edit modal |
| 3 | [03-routes-as-content.md](03-routes-as-content.md) | `routes` content type as owned references; projection into `CMSRoutes`; descendant rewrite |
| 3a | [03a-review-fixes.md](03a-review-fixes.md) | Fixes from the code review of phases 2–3 (config relation writes/detach, route projection) |
| 4 | [04-zones.md](04-zones.md) | Zone item → zone (ordered) and owner → zone (slot) on relations; `ContentZoneAssignments` retired |
| 5 | [05-page-parent-containment-admin.md](05-page-parent-containment-admin.md) | Versioned page parent; page subtree cascade; generic children panel and MCP child tools; `IAdminCrudChildHandler` retired |
| 6 | [06-testhost.md](06-testhost.md) | TestHost submodule moved onto relations; end-to-end verification |

The phases depend on each other in order: 2–5 build on 1; 3 needs 2's modal; 5's generic child tools
need 4's zone containment.

## Settled decisions

These were agreed with the user and are not reopened in the phases.

1. **One record.** A `ContentRelation` row is the only source of truth for a relation's value. FK
   columns and in-JSON ids are removed.
2. **Version-owned.** Rows belong to the source's `ContentVersion`, so a relation change mints a version
   and is a draft until publish.
3. **One attribute.** Relations are declared with `[ContentRelation]` on a `Guid` / `Guid?` (single)
   or `IEnumerable<Guid>` (many) property. The attribute implies the EntityPicker.
4. **Three kinds:**
   - **Containment** (parent–child): zone item → zone, owner → zone (slot), Article → Article List,
     page → parent page.
   - **Reference:** everything else.
   - **Owned reference:** a reference whose target lives and dies with its source. Routes use it.
5. **Editing a reference:** a picker (multi for `IEnumerable`) with New / Edit buttons that open the
   target's form in a modal with its own save.
6. **Routes are a versioned content type,** related to their owner as an owned reference, many per
   owner. Published route versions are projected into `CMSRoutes`.
7. **Deleting a referenced target** checks referrers' **current draft and published versions only**.
   Historical rows that point at a deleted target are kept and resolve to nothing. `TargetNodeId`
   therefore has **no database FK**; the engine enforces integrity.
8. **Page moves rewrite descendants.** Publishing a page whose route prefix changed re-projects every
   live descendant's derived routes in the same publish. Explicit routes are left alone.
9. **Page delete cascades to the subtree,** after a confirm that states the descendant count (the MCP
   result states it too).
10. **No relation cache.** Relations are auto-included with `ContentVersion`, and referrer lookups are
    indexed. Measure later.
11. **Relations are per variant.** They follow the version, so each culture/segment has its own.

## Implementation decisions taken in this plan (review these)

These come from reading the code, not from the user. Flag any you disagree with before phase 1 starts.

- **`Kind` is stored on the row** (`Containment` / `Reference` / `Owned`). A rendering-only host has no
  admin handler registry, yet must still apply containment visibility. Storing `Kind` makes visibility
  and delete cascade work without declarations. The **delete policy** (`Restrict` / `Detach`) is not
  stored; it comes from the declaration at delete time, which happens in admin mode only.
- **Relations carry forward by default.** `ContentStore.BuildVersion` starts every new version from a
  baseline:
  - **edit:** the current draft's relations;
  - **restore:** the historical version's relations;
  - **create:** none.

  It then applies only the names the caller explicitly wrote (`ContentVersion.RelationWrites`). A write
  path that builds a fresh DTO (every mapper-based save does) can therefore never silently drop a
  relation.
- **Value transfer happens in the mapper.** `WebWayCMS.Mapping.Mapper.Map` gets one generic post-map
  step. When a `[ContentRelation]` view model maps to an `IVersionedContent`, it records relation
  writes. When an `IVersionedContent` maps to such a view model, it fills the properties. Models need no
  per-type relation code.
- **Validation happens at `AdminCrudModel.SaveUpsertAsync`.** A `RelationBinder` checks target
  existence, target type and `Required` beside `ModelValidator`, returning field errors. This covers the
  admin UI, MCP and the seeder.
- **Layering:**

  | Project | Owns |
  |---|---|
  | `WebWayCMS.Forms` (no refs) | the attribute, the enums, and the reflection helper `RelationProperties`, using a neutral `RelationValue` struct |
  | `WebWayCMS.Data` | the record, EF configuration, store engine, `IContentRelations`, predicates, visibility filter |
  | `WebWayCMS.Core` | the declaration registry, binder, delete service, mapper hook |

  No new project references are added.
- **Deterministic zone node ids** (phase 4). Today a unique index on assignments stops two concurrent
  renders from creating two zones for the same (owner, slot). Version-owned rows can't carry that
  index. A zone's node id becomes a name-based (v5) Guid of `(ownerNodeId, slot)`, so the
  `ContentNodes` primary key enforces it, in Postgres and the InMemory provider alike.
- **Containment visibility is applied inside `ContentStore` reads** when the mode is not `Draft`, via a
  per-request memoized walk up containment rows. It replaces `ArticleModel.MapIfListVisibleAsync`. In
  Draft mode every node has a current draft, so the filter is skipped.
- **`cmsroutes` admin.** The existing `CMSRouteModel` admin keeps working for **unowned** rows (manual
  and code-based). Projected rows are shown read-only, with a link to their owner.

## Cross-cutting rules (apply in every phase)

- **Tests.** Before editing any existing test that currently passes, **list the tests the phase must
  change and get a human's approval** (repo rule). New behaviour gets new tests. The 100% line + branch
  gate per project is non-negotiable. EF-backed tests use the InMemory provider.
- **Migrations.** This is pre-release, so each phase regenerates `InitialCreate` with
  `./scripts/RebuildEFMigrations.sh`. No data migrations. The TestHost's `AddFaqs` migration is
  regenerated in phase 6.
- **Scripts.** Admin scripts are edited only in `Views/**` sources; `CopyViewScripts` produces the
  `wwwroot/js` copies. New copied JS must also exist in the repo's `wwwroot` (see memory: generated
  assets must be committed by the human). No jQuery, no inline `<script>`, no new external libraries.
- **Conventions.** Follow `CLAUDE.md`'s code conventions: file-scoped namespaces,
  `(bool Success, string? ErrorMessage)` tuples, `CancellationToken ct = default`,
  `Serilog.Log.ForContext<T>()`, and test names `MethodName_Scenario_ExpectedBehavior`.
- **Builds and tests** run from a scratchpad copy if the sandbox blocks writes (memory: sandbox build
  permissions).
- **No commits.** The human commits.
- **Docs.** Each phase ends by updating `CLAUDE.md` and the affected area docs to the new behaviour
  (listed per phase), and moving the matching part of doc 17 from "proposed" to "implemented".
- **Wrap-up.** The last step of every phase is to re-read that phase file and confirm that every step
  and every verification item was actually done.

## Shared verification (every phase, in addition to the phase's own)

1. `dotnet build` is clean (no new warnings).
2. `./scripts/RunTests.sh` passes, and every project is at 100% line + branch.
3. `./scripts/StartIntegrationHost.sh` boots and the default seed applies with no errors in the log.
4. MCP smoke over curl (memory: integration verify via curl): `list_content_types`,
   `describe_content_type` on each changed type, and a create → update → publish round trip on each
   changed type.
5. The TestHost Playwright suite (`e2e/`) passes. Mind the 5/min login rate limit. **Note:** `e2e/`
   is not in the submodule's current checkout. Before phase 1, find the branch or commit that has it
   and update the submodule pointer, or ask the human where the suite lives.
6. `./scripts/TearDownIntegrationhost.sh -v`.
