# Known Issues

Findings from the 2026-10 full code and functionality review of WebWayCMS. All **High** severity
issues have been fixed (see "Resolved" at the bottom). Medium and Low findings are documented here
as accepted risks / backlog items.

## Medium

| # | Finding | Location |
|---|---------|----------|
| M1 | In-place draft saves have no concurrency backstop (no `DbUpdateException` mapping, no DB guard) — concurrent saves silently last-write-wins | `ContentStore.cs:512-529` |
| M2 | `DeleteVersionAsync` can delete a Published version (only guards on version number, not State) — silently unpublishes live content while route rows keep resolving | `ContentStore.cs:343-370` |
| M3 | Zone-tree deletion (`DeleteZoneAsync`, `DeletePageZonesAsync`) is multi-step with **no transaction** — partial failure leaves unreachable orphan subtrees | `ContentZoneService.cs:791-859` |
| M4 | `FormProperty` `Min`/`Max`/`MaxLength`/`Pattern` are never enforced server-side — MCP writes and seed files can violate them | `ModelValidator.cs:32-54` |
| M5 | 4000-char JSON columns (`ComponentPropertiesJson`, `ConfigurationJson`) have no pre-save length check — overflow becomes a raw DB exception; seed items with overflow retry forever | `ContentZoneItemDTOEntityConfiguration.cs:15`, `PageDTOEntityConfiguration.cs:14` |
| M6 | Seeder: failed publish after successful first-time save duplicates the item on every boot (ledger written only after publish) | `JsonContentSeeder.cs:256-284` |
| M7 | Unpublishing a page leaves its zone items' widget routes live — content under an unpublished page remains publicly reachable | `PageModel.cs:210-217` |
| M8 | Zone item ordinal minting races (`Max+1` read-then-write, no unique index) | `ContentZoneService.cs:277-283` |
| M9 | Admin credentials (password) committed to repo in TestHost config | `WebWayCMS.TestHost/appsettings.json:7`, `docker-compose.yml:16` |
| M10 | `LoginWith2fa` and 2FA verification endpoints not rate-limited | `AuthRateLimiting.cs:25-35` |

**Note on M9:** The committed admin password in the TestHost is **intentional**. The TestHost is a
development-only test environment that will never be deployed and requires a known account for
automated and manual testing. This is not a security issue; do not flag it in future reviews.

## Low

| # | Finding | Location |
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

## Dead code / refactoring debt

| Finding | Location |
|---------|----------|
| `ContentQueryExtensions.AtReadContext` — documented as "the single place version selection happens" but **never referenced anywhere**; the hand-copied anti-joins it was meant to replace may still exist (refactoring debt / inconsistency risk) | `WebWayCMS.Data/Data/Services/ContentQueryExtensions.cs` |

## Resolved (fixed during this review)

| # | Finding | Fix |
|---|---------|-----|
| H1 | Forwarded headers trusted from every client (`KnownProxies.Clear()` / `KnownIPNetworks.Clear()`) → per-IP rate-limit bypass and IP spoofing | `CmsHttpInfrastructureRegistration.ConfigureForwardedHeaders` keeps the framework's loopback-only trust list by default; a host opts in with `ForwardedHeaders:KnownProxies` / `KnownIPNetworks`, or `TrustAllProxies` when the proxy address isn't stable (the shipped docker-compose sets it — nginx runs in its own container). Upgrade note in `docs/architecture/13-security.md` → Forwarded headers |
| H2 | `CMSRouteService.UpsertAsync` delete-then-insert not atomic — failed insert permanently deleted the owner's previous route row | Delete + insert share one transaction; on `DbUpdateException` the transaction is rolled back and tracked entities are cleared, so the previous route row survives |
| H3 | Failed republish rollback (`PageModel.PublishPageAsync`) left the page with zero published versions | `PublishAsync` reports the version it archived (`ContentWriteResult.DisplacedVersionId`); on route-registration failure `PublishPageAsync` calls `IContentStore.RevertPublishAsync(nodeId, publishedVersionId, displacedVersionId)`, which undoes only that publish (demote, then re-promote the displaced version, in one transaction). A publish that changed nothing (page already live, only zone drafts pending) is not reverted, and a page that was offline stays offline |
| H4 | Widget `ComponentPropertiesJson` saved without rich-text sanitization — stored XSS sink for widgets declaring `RichText` config fields | `WidgetConfigurationJson.Sanitize` rewrites only the config type's rich-text values in place (other keys kept, relaxed escaping, unreadable JSON fails closed to `{}`) on every zone-item write: the zone editor API and the child handler (admin form, MCP, seeder, restore) |
| L8 | `GenericPageConfiguration.Meta`/`Style` stored XSS via `@Html.Raw` | `GenericPageConfiguration.SanitizedForRender()` (both page controllers) applies `HeadMarkupSanitizer` at render time: a `<meta>`/`<link>`-only allow-list for `Meta`, and `</` → `<\/` for `Style` (CSS kept verbatim). See `docs/architecture/13-security.md` |

## TestHost known quirk

The committed TestHost config uses the placeholder `[EMAIL]` as the admin account email. The seeder
creates that user, but the Login page's `[EmailAddress]` validation rejects it, so admin login via
the UI fails until an operator replaces the placeholder with a real email. Related to M9 (intentional
committed credentials, development-only environment).
