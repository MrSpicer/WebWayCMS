# Known Issues

Open defects and accepted limitations in WebWayCMS, by severity.

## Medium

| # | Issue | Location |
|---|---------|----------|
| M14 | Deleting a page with child pages leaves the deleted pages' routes in the cached route list, so navigation widgets (the site navbar included) keep linking to them until the next route write or an app restart; the URLs themselves correctly answer 404. The delete runs in one `ICmsTransaction`, and `CMSRouteService.Delete*` invalidates `ICMSRouteRegistry` *inside* it; a reload between that invalidation and the commit caches the pre-commit rows, and nothing invalidates again after the commit. `PageModel.PublishPageAsync` already handles the same race with a post-commit `Invalidate()` in a `finally`; the delete path has no equivalent. On the TestHost, a second Playwright run on the same stack fails `layout.spec.ts` ("the navbar lists every seeded page"); workaround: restart the web container before re-running. | `ContentDeleteService.cs:88`, `CMSRouteService.cs:209-253`, `CMSRouteRegistry.cs`; the fixed pattern is at `PageModel.cs:257` |

## Low

| # | Issue | Location |
|---|---------|----------|
| L1 | `PublishAsync` runs two separate `SaveChanges` with no transaction — crash between them leaves node half-published | `ContentStore.cs:206-222` |
| L2 | `DeleteItem` sweeps routes before deleting — concurrent item removal leaves published item routeless | `ContentZoneApiController.cs:165-178` |
| L3 | `ChildReorder` returns 500 for every failure, while equivalent move endpoint correctly maps to 409 — client can't distinguish conflict from server error | `AdminContentController.cs:457-460` |
| L4 | Media upload/delete races: concurrent uploads of same bytes → unhandled PK exception; blob bytes deleted before catalog row commit | `MediaLibrary.cs:43-68, 96-102` |
| L5 | `ContentFieldMerger` silently drops unknown/misspelled fields — typos in MCP/seed files save "successfully" with no effect | `ContentFieldMerger.cs:27-35` |
| L6 | `ApplyNodeFields` ignores `SiteId`/`IsArchived` — node site edits silently dropped | `ContentStore.cs:493-576` |
| L7 | `SeedMediaResolver` path check uses `StartsWith` prefix, not containment — sibling-directory escape (`@media:../seed-evil/…`) | `SeedMediaResolver.cs:109-113` |
| L9 | CSP `style-src 'unsafe-inline'` (documented CKEditor tradeoff; consider nonces) | `CspPolicyBuilder.cs:20` |
| L10 | Antiforgery token sourcing inconsistent: JS reads form inputs, layout emits meta tag — dynamic widget config forms may fail antiforgery | `media-picker.js:11-14`, `_AdminLayout.cshtml:26` |
| L11 | Convention violation: `Debug.WriteLine` instead of Serilog | `CMSRouteRegistry.cs:49`, `PageControllerRegistry.cs:182` |
| L12 | Sync-over-async (`.GetAwaiter().GetResult()`) in 4 registry cache cold-start paths — blocks a thread-pool thread inside a lock during DB refresh; acceptable at cold start, risk under load | `FormComponentRegistry.cs:105`, `CMSRouteRegistry.cs:42`, `WidgetRegistry.cs:217`, `PageControllerRegistry.cs:176` |
| L13 | Several async service methods missing `CancellationToken` | `RouteRegistrationService.cs:28,71,111`, `SeedMediaResolver.cs:53`, `FormComponentRegistrationService.cs:53`, `MediaLibrary.cs:35`, `ContentZoneService.cs:377` |
| L14 | Admin navbar `aria-current` is broken on every item: the attribute is emitted as a Razor string expression (`@(cond ? "aria-current=\"page\"" : "")`), which HTML-encodes the quotes, so the active link renders `aria-current=&quot;page&quot;` and screen readers get no current-page marker. Use a conditional attribute (`aria-current="@(cond ? "page" : null)"`) instead | `_AdminNavbar.cshtml` (every `navbar-item` link) |
