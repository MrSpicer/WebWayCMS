# Phase 4: Zones on Relations

**Goal:** move the zone structure onto relations and retire `ContentZoneAssignments`.

- **Zone item → zone:** an ordered containment owned by the item version, relation name `Zone`, with
  `Ordinal` on the row.
- **Zone → owner (page or item):** a containment owned by the zone version, relation name `Owner`, with
  `Qualifier` = slot.

Behaviour is unchanged: drafts until page publish, remove = inactive version, reorder/move/remap,
page history, Unplaced panel. Only the storage changes.

## 0. Test impact review

This is the largest test change. Get approval for it as a list:

- **`tests/WebWayCMS.Data.Tests`:**
  - `ContentZoneService` tests: every test that seeds items with `ContentZoneNodeId`/`Ordinal` or seeds
    `ContentZoneAssignmentDTO` rows;
  - `PageZoneTree` tests;
  - `PageHistoryService` tests that build zone trees.
- **`tests/WebWayCMS.Core.Tests`:** `ContentZoneModel`, `ContentZoneChildHandler`, `SlotRemapPlanner`
  integration and `JsonContentSeeder` zone-path tests.
- **`tests/WebWayCMS.Presentation.Tests`:** `ContentZoneViewComponent` tests that mock assignment
  lookups.

**Recommended approach to reduce churn:** add a shared test builder (`ZoneFixture` in each affected
test project) that creates zones and items through relation helpers. The rewritten tests then keep
their assertions verbatim and change only their arrange blocks. Ask for approval of that approach too.

## Decisions (2026-10-28, with the human)

- **Tests:** approved — `ZoneFixture` builders rewrite arrange blocks; mocks follow the renamed/retyped
  methods; assertions stay verbatim (any assertion change is listed in the report with its reason).
- **Visibility:** zones and items are containment children, so the containment-visibility filter
  applies to them in Published reads (a zone under an unpublished page is hidden). Accepted; no special case.

## 1. Data: model changes

- **`ContentZoneItemDTO`:**
  - remove the `ContentZoneNodeId` and `Ordinal` columns;
  - add read-only accessors that are ignored in EF, for in-memory code only:
    `public Guid ContentZoneNodeId => Version.TargetOf(ZoneRelations.Zone) ?? Guid.Empty;` and
    `public int Ordinal => Version.OrdinalOf(ZoneRelations.Zone);`. Add `OrdinalOf` to
    `ContentVersionRelations`;
  - writes go through `ZoneRelations.Place(item.Version, zoneNodeId, ordinal)`.

  **Rule:** no LINQ-to-EF query may use the accessors. A unit test enforces it by checking that the
  accessors are `Ignore`d in the model, and the code review checks it too.
- **`ContentZoneItemDTOEntityConfiguration`:** remove the FK, the `(ContentZoneNodeId, Ordinal)` index
  and the properties; add `Ignore` for both accessors.
- **`Data/Services/ZoneRelations.cs`** (static):
  - name constants `Zone` and `Owner`;
  - `Place(version, zone, ordinal)`;
  - `Own(version, ownerNodeId, slot)`;
  - predicates `InZone(zoneId)`, `OwnedBy(ownerId)` and `OwnedBy(ownerId, slot)`;
  - ordinal key selector `ZoneOrdinal`.
- **Delete** `ContentZoneAssignmentDTO`, its entity configuration (with the check constraint and the
  page-slot / item-slot unique indexes), and the `DbSet`.
- **`ContentZoneDTO`:** no column changes. Its owner is the `Owner` relation.

## 2. Data: deterministic zone ids (race guard)

- **`Data/Services/ZoneNodeIds.cs`:** `For(Guid ownerNodeId, string slot)` returns an RFC 4122 v5
  (SHA-1, name-based) Guid under a fixed namespace Guid. The slot is hashed **exactly as given**:
  `GetByPageSlotAsync` matches `SlotName == slotName` case-sensitively today (`ContentZoneService.cs:86`),
  so the id must not fold case either. No library is needed; it is about 20 lines over `SHA1.HashData`.
- **`IContentStore<T>.CreateWithNodeIdAsync(T entity, Guid nodeId, ct)`:**
  - creates the node with the given id;
  - on a duplicate key it returns `ContentWriteResult(false, AlreadyExistsMessage)`;
  - on the InMemory provider the duplicate shows up as an `ArgumentException` or
    `InvalidOperationException` from the tracker, so catch both, plus `DbUpdateException` for
    Postgres.
- **`ResolveOrCreateAssignmentAsync`** (`ContentZoneService.cs:909`) becomes
  `ResolveOrCreateOwnedZoneAsync(owner, slot)`:
  1. Look up the zone node `ZoneNodeIds.For(owner, slot)`.
  2. If it is missing, call `CreatePublishedZoneAsync` with `CreateWithNodeIdAsync` and the `Owner`
     relation.
  3. On `AlreadyExists`, re-read.
  4. The existing "repair" branch (a broken assignment) goes away: there's nothing left to repair.

## 3. Data: rewrite queries in `ContentZoneService` and `PageZoneTree`

Structural queries (zone tree, ownership, descendants) read **current-draft** relation rows, the same
read-context independence the unversioned assignment table had. Ownership is immutable, because a zone
never changes owner or slot, so every version agrees. Item placement queries keep their current
read-context behaviour.

Method by method (`ContentZoneService.cs`):

| Method | New query |
|---|---|
| `GetItemsAsync` (54) | items `InZone(zone)` at the read context, ordered by `ZoneOrdinal` |
| `GetByPageSlotAsync` / `GetByItemSlotAsync` (82, 102) | the zone node `ZoneNodeIds.For(...)`, if it exists; return the zone DTO (the signatures change from `ContentZoneAssignmentDTO` to a small `OwnedZone(ZoneNodeId, OwnerNodeId, Slot, OwnerIsPage)` record) |
| `GetOrCreateByPageSlotAsync` / `GetOrCreateByItemSlotAsync` (89, 109) | the same, plus create; return `(ContentZoneDTO Zone, OwnedZone Owner)` |
| `GetAllAssignmentsForPageAsync` (149) | zones `OwnedBy(page)`; rename to `GetOwnedZonesForPageAsync` |
| `GetAllByPageAsync` / `GetAllByParentZoneAsync` (157, 174) | zones `OwnedBy(page)` / zones owned by items `InZone(parentZone)` |
| `GetZoneNodeIdsWithChildrenAsync` / `GetOwnedSlotsWithItemsAsync` (195, 215) | the `Owner` + `Zone` relation joins |
| `GetDescendantItemNodeIdsAsync` (227) | BFS: zones `OwnedBy(item)` → items `InZone` → … |
| `GetItemOwnedZonesForPageAsync` (257), `GetAssignmentCountsByNodeIdAsync` (277), `GetParentPageNodeForZoneAsync` (292) | the `Owner` chain; "is the owner a page" comes from the owner node's `ContentTypeKey == "pages"` |
| `AddItemAsync` (329) | `Place(item, zone, nextOrdinal)` |
| `ReorderItemsAsync` (533) | a new version only for moved items, with `Place(item, zone, newOrdinal)`; removed items keep their ordinals |
| `MoveItemAsync` (578) | `Place` in the target zone, then renumber the source zone (as today) |
| `RestoreItemVersionAsync` (641/645) | restore carries the historical `Zone` relation (zone + ordinal) through the phase-1 baseline. The existing "old zone gone / other page" refusal checks `TargetOf(Zone)` on the historical version |
| `UpdateItemAsync` + `SlotRemapPlan` (378/381) | moves write `Place`; target-zone creation uses `ResolveOrCreateOwnedZoneAsync` inside the open transaction |
| `DeleteZoneAsync` (980), `DeletePageZonesAsync` (1049) | ownership rows go with the zone versions (cascade); the delete order is items, then nested zones (deepest first), then the zone. With no FK `Restrict` any more, the order is kept for clarity, not enforced |
| `GetPendingItemNodeIdsAsync` / `PublishPageZonesAsync` (846, 862) | unchanged logic over the new tree queries |

`PageZoneTree.cs` (lines 68, 101, 137, 161, 198, 219): replace each assignment query with the
`OwnedBy` / `InZone` equivalents.

## 4. Core and Presentation callers

- `ContentZoneModel.GetPageZoneSlotsAsync` (≈ 208) uses `GetOwnedZonesForPageAsync`.
- `ContentZoneChildHandler`: item view models read the accessors (in-memory), so this is unchanged
  apart from the type renames.
- `JsonContentSeeder` (474, 487): `GetOrCreateBy*SlotAsync` keep their names, and the tuple type
  changes.
- `ContentZoneViewComponent`: update the doc comment (`ContentZoneViewComponent.cs:14`). The
  `ParentItemId` resolution path is unchanged.
- `SlotRemapPlanner`: pure; unchanged.

## 4a. Delete cascade wiring

From this phase on, zones are containment children of pages and items, and items are containment
children of zones, so phase 1's `ContentDeleteService` sees them when a page or zone is deleted.

- Register a **`ZoneItemDeleter : IContentDeleter`** (`contentzoneitems`) that hard-deletes one item
  through `IContentZoneService`. It is replaced by the phase-5 handler.
- `ContentZoneModel` is already the `contentzones` deleter.
- `PageModel.DeleteSelfAsync` still calls `DeletePageZonesAsync`. After the cascade that is a no-op,
  but it stays as a safety net until phase 5.
- A test covers deleting a page with nested zones through the cascade. Every page, zone and item and
  every relation row goes, in one transaction.

## 5. Page history

- Snapshots record item **version** ids, whose relations (zone + ordinal) are now part of the captured
  state. So a revert restores placement through `ReplaceAllRelationsFrom`, which needs no special code.
  Verify that the existing revert tests cover a moved item.
- `PageSetEquality.ItemsEqual` already includes relations (phase 1), so a pure reorder is a pending
  change, as today.

## 6. Migrations, docs

- `RebuildEFMigrations.sh`: drops `ContentZoneAssignments`, `ContentZoneItems.ContentZoneNodeId` and
  `Ordinal`.
- **Docs:**
  - `CLAUDE.md`: Content Versioning ("Nested zones are owned by items" now describes the `Owner`
    relation and deterministic zone ids; "Moving items"; "Zone items in a page's zones");
  - `04-content-zone-framework.md`, `01-data-tier.md`;
  - doc 17 §7 zone rows: implemented.

## Verification

- Unit coverage 100% (Data, Core, Presentation).
- A race test (Data, InMemory): two `GetOrCreateByPageSlotAsync` calls for the same (page, slot) on
  separate contexts yield one zone node.
- Integration host, on the `/zones` showcase page and in the page zone editor:
  1. Add, edit, reorder (drag and ArrowUp/Down), cross-zone move, and remove plus restore an item. The
     preview reflects each change, and the public page is unchanged until publish.
  2. Change a Layout container's view: its content follows the slot remap, with one history entry.
  3. Page history: revert to an entry from before a move, and the item returns to its old zone and
     position. Discard changes works.
  4. Unplaced content panel: strand a Layout column (a view change that drops a slot), and the panel
     shows it.
  5. Delete a page that has nested zones. No orphan zone, item or relation rows remain (check with
     `psql`: `select count(*) from "ContentRelations" r left join "ContentVersions" v on v."Id" = r."SourceVersionId" where v."Id" is null` returns 0).
  6. MCP: `move_child`, `reorder_children` and `restore_child_version` all behave as before.
- Playwright: the zone drag tests (memory: stepped synthetic `PointerEvent`s) pass unchanged.

## Outcome (2026-10-28)

Implemented as planned, with these deviations and findings:

- **Shared zones are gone.** A zone has exactly one owner, so the tests of a zone shared by two pages, two
  slots, or a page and a container were removed (`DeletePageZonesAsync_ChildZoneSharedWithAnotherPage_Survives`,
  `DeletePageZonesAsync_SharedZone_KeepsZoneButRemovesPageAssignment`,
  `DeleteZoneAsync_ContainerItem_KeepsNestedZoneReferencedElsewhere`), as was the removed dangling-assignment
  repair's test (`GetOrCreateByPageSlotAsync_DanglingAssignment_RepairsAndRepoints`).
- **Owner kind:** an owner is an item iff its node is a `contentzoneitems` node (left join), so a vanished
  owner keeps its zone page-owned (never global, which would auto-publish) — the old assignment row's behaviour.
- **`MergeUpdate` drops a `Zone` write**, so an update can never move an item.
- **`PageSetEquality`** leaves the `Zone` relation out of content equality and the relation part of the
  fingerprint (placement is already compared as fields); without that a pure move read as an edit.
- **Delete cycles:** ownership now stays on the zone until the zone itself goes, so `DeleteZoneAsync` carries a
  visited set (the old code broke cycles by deleting assignment rows first).
- **Phase 3a leftover fixed:** `ContentZoneApiController`'s config save wrote the relation writes onto the
  reloaded `existingItem` instead of the item it passes to the update, so zone-editor picker changes were still
  dropped. Fixed, with a controller test.
- **Assertion changes** (mechanical translations of the retired table): `Set<ContentZoneAssignmentDTO>()` →
  `ZoneFixture.OwnerRows(ctx)`, `i.ContentZoneNodeId == x` in EF → `ZoneFixture.ItemsIn(ctx, x)`; the two
  `ContentZoneNode` identity asserts became asserts on the deterministic id.
- **Not done:** the Playwright suite has no zone-drag specs; drag (same-zone, cross-zone) and ArrowDown were
  verified in the browser with stepped `PointerEvent`s instead.
- **Unplaced panel:** a view change now always remaps, so stranded content can't be produced from the UI;
  item 4 was verified with a zone inserted by SQL on the deterministic id `ZoneNodeIds.For(layout, "Stranded")`
  (the Python `uuid5` of the same name matched the C# id, confirming the v5 encoding).
- **Delete count:** `DeletedDescendants` for a page delete now counts its zones and items (16 for the showcase
  page). Phase 5's subtree confirm should count pages only.
