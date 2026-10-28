# Phase 3: Routes as Content

**Goal:**

- A versioned `routes` content type, related to routable content as an **owned reference, many per
  owner**, and edited through the phase-2 modal.
- Published route versions are projected into `CMSRoutes`.
- Publishing a page whose prefix changed re-projects its live descendants' derived routes.

## 0. Test impact review

Get approval first. Expected changes:

- `PageModel` publish / unpublish / delete tests (route registration is replaced by projection);
- `RouteRegistrationService.RegisterContentRoutesAsync` / `UnregisterContentRoutesAsync` tests;
- `CMSRouteModel` admin tests (projected rows become read-only);
- `CMSRouteTransformer` tests only if the data tokens change (they should not).

## 1. Data: the route content type

- **`Data/Models/RouteDTO.cs : IVersionedContent`:**
  - `Mode` (`RouteMode.Derived` | `Explicit`);
  - `Pattern` (used when `Explicit`; for `Derived` it holds the last projected pattern, informational);
  - `NavigationName` (max `CMSRouteDTO.NavigationNameMaxLength`);
  - `Order`.
- **`RouteDTOEntityConfiguration`:** `ConfigureContentLink()` and `ToTable("Routes")`.
- **`CmsRenderingRegistration`:** `AddContentStore<RouteDTO>(services, "routes")`.
- **`CMSRouteDTO`:** add `RouteNodeId` (`Guid?`), the route item a row was projected from (`null` for
  widget, code-based and manual rows). Add an index on it. `OwningContentNodeId` / `OwningContentType`
  stay; they are what the transformer reads.

## 2. Core: route model and owner declaration

- **`Models/Route/RouteModel : AdminCrudModel<RouteDTO>`**, key `routes`:
  - upsert view model: `Mode` (dropdown), `Pattern` (shown when Explicit), `NavigationName`, `Order`;
  - `SupportsPublishing = false`: routes publish only with their owner;
  - `WriteRoles` Admin + Editor (the same as pages);
  - `GetApiListAsync` returns `{ id, title = pattern or "(derived)" }`;
  - the save checks reserved parameter names and a valid pattern through the existing
    `CMSRouteService` validators, extracted into a static `RoutePatternRules` so both paths share them.
  - Not shown in the navbar. It's reached through the modal, and by URL or MCP.
- **`PageUpsertViewModel`:**
  `[ContentRelation("routes", Kind = Owned)] [FormProperty(Label = "Routes", Order = …)] public List<Guid> RouteIds { get; set; } = [];`.
- **New page:** `PageModel.SaveUpsertCoreAsync` on create with an empty `RouteIds` mints one `Derived`
  route draft (`NavigationName` = title) and relates it. Every page therefore has at least one route
  item, and the form shows it.
- **`IRoutableContent`** keeps `RouteContentType` and the controller name. Its routes come from the
  owner's `routes` relation instead of `GetRoutesAsync` over `CMSRoutes`. A host type opts in by
  declaring the same `RouteIds` property and implementing `IRoutableContent`.

## 3. Publish: project routes

Replace the delete-and-reregister block in `PageModel.PublishPageAsync` (≈ lines 147–214) with a
`RouteProjectionService.ProjectAsync(IRoutableContent owner, Guid ownerNodeId, ct)`. It runs inside the
page publish's change set:

1. Read the owner's **published** version's `RouteIds` relation targets. This runs after
   `_store.PublishAsync`.
2. Publish each target route's current draft (`IContentStore<RouteDTO>.PublishAsync`).
3. Work out each route's pattern:
   - `Explicit` uses `Pattern`;
   - `Derived` uses `DeriveRoutePatternForPublishAsync(slug, nodeId, parent)`, unchanged (the parent
     comes from `ContentNode.ParentNodeId` until phase 5).
4. Upsert `CMSRoutes` rows keyed by `RouteNodeId`, with `OwningContentNodeId` = owner,
   `OwningContentType` = `owner.RouteContentType`, `DefaultsJson` from the controller, and
   `NavigationName` / `Order` from the route.
   - Collision rules: `CMSRouteService.UpsertAsync` gains an overload keyed on `RouteNodeId`. The same
     owner-and-pattern collision logic applies, and a foreign owner is still a collision.
5. Delete projected rows of this owner whose `RouteNodeId` is no longer related, and hard-delete those
   orphaned owned route items (via `ContentDeleteService`).
6. **Derived descendants (settled decision 8).** When a `Derived` route's projected pattern changed
   (the old row's pattern differs from the new one), collect live descendant pages (published
   versions whose parent chain leads here) and re-project each one's `Derived` routes under the new
   prefix. Recurse; explicit routes are skipped. The rewritten rows are part of the same publish.
   Report the count in the publish result ("Page published; 3 child URLs updated").
7. **Failure:** if any step fails, keep the existing `RevertPublishAsync` behaviour and restore the
   previous projection (snapshot the owner's projected rows before step 4).

**Unpublish** (`PageModel.UnpublishPageAsync` ≈ line 256): delete the owner's projected rows, which
replaces `UnregisterContentRoutesAsync`. The route items stay, as drafts.

**Delete:** owned route items are deleted by `ContentDeleteService` (`Kind == Owned`), and their
projected rows are removed in the same transaction.

**Widget routes:** they still register at page publish through `RegisterPublishedZoneRoutesAsync`, now
prefixed with the page's **primary** route: the lowest `Order`, then the first relation ordinal. They
are unchanged otherwise. (Re-projecting widget routes on a page rename is a known gap and out of
scope.)

## 4. Routing reads

- `CMSRouteTransformer` is unchanged; it still matches `CMSRoutes` rows.
- **Slug availability** (`IsSlugAvailableAsync`, `IsPatternAvailableAsync`) checks Explicit patterns
  against projected rows and against other routes' current drafts.
- **Admin tree** (`PageModel.BuildTree`, `GetPageIndexAsync`): the URL comes from the primary route's
  projection, or the derived path when unpublished, as today.
- **`RouteNavigation` widget:** it reads `NavigationName` from `CMSRoutes`, so it is unchanged.

## 5. `cmsroutes` admin

- `CMSRouteModel` index: a projected row (`RouteNodeId != null`) shows "Owned by {title}" with a link
  to the owner's edit page, and has no edit or delete.
- `CMSRouteModel.SaveUpsertAsync` / `DeleteAsync` refuse projected rows ("Edit this route from its
  page.").

## 6. Seeding and MCP

- **Seed:** pages without `routeIds` get the derived route minted on create, so existing seed files
  keep working. A seed may declare explicit routes as `routes` items with `@seed:` ids in `routeIds`.
- **MCP:** `create_content routes`, then `update_content pages { routeIds: [...] }`.
  `describe_content_type pages` shows `routeIds` with `relation.kind = owned`.

## 7. Migrations, docs

- `RebuildEFMigrations.sh`: adds the `Routes` table and `CMSRoutes.RouteNodeId`.
- **Docs:**
  - `CLAUDE.md`: Content Versioning ("Routes are written by Publish" becomes "projected from route
    items"), `CMSRouteService.UpsertAsync` keying;
  - `03-page-routing.md` (rewrite of the publish flow), `05-content-domain-models.md`;
  - doc 17 §8: implemented.

## Verification

- Unit coverage 100% (Data, Core, Routing).
- Integration host:
  1. Create page `docs`. The form shows one Derived route. Publish: `/docs` resolves.
  2. Add an Explicit route `/documentation` via the modal **New** and publish. Both URLs resolve, and
     the navbar uses the route with a `NavigationName`.
  3. Create child page `intro` under `docs` and publish: `/docs/intro` resolves.
  4. Rename `docs`'s slug to `guides` and publish:
     - `/guides` and `/guides/intro` resolve;
     - `/docs/intro` no longer does;
     - `/documentation` (Explicit) is untouched;
     - the publish message reports 1 child URL updated.
  5. Edit a route draft without publishing: the live URL is unchanged. Publish the page: the edit
     goes live.
  6. Unpublish the page: its routes disappear from `CMSRoutes`, and the route items remain as drafts.
  7. Delete the page: its route items are gone.
  8. `/wadmin/cmsroutes` shows projected rows read-only, with an owner link.
  9. Pattern collision: give two pages the same Explicit pattern. The second publish fails with the
     collision message, and that page's publish is reverted.
