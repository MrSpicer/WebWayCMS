# Area 15: JSON Content Seeding

**Namespaces:**
- `WebWayCMS.Services.ContentSeeding` — `JsonContentSeeder`, `IContentSeedSourceProvider`,
  `AssemblyContentSeedSourceProvider`, `FileContentSeedSourceProvider`, `ContentSeedOptions`,
  `ContentSeedSource`, `ContentSeedDocument`, `ContentSeedItem`
- `WebWayCMS.Content` — `ContentFieldMerger` (shared JSON field overlay)
- `WebWayCMS.Data.Services` — `IContentSeedRecordService` / `ContentSeedRecordService`
- `WebWayCMS.Startup` — `CmsContentSeedCatalog`, `CmsContentSeedRunner`

**Depends on:** Data, Core (admin handler dispatch)
**Consumed by:** a package host that ships a site's content with its package

---

## 1. The Problem

Before this area, the only way to get content into a WebWayCMS database at startup was to write C#:
`DefaultContentSeeder` hard-codes exactly two pages (Home and `/wadmin`), and the four
assembly-scanning seeders seed *registrations* (widgets, form components, page types, code-based
routes), not content. A host that wanted to ship a site's pages and content blocks with its package
had to fork CMS source or hand-roll its own `IContentStore<T>` calls.

JSON content seeding lets any assembly — the host or a satellite content DLL — ship a JSON file of
serialized content that is discovered at startup and created or updated in the database, then
published.

## 2. Design Principles

This is deliberately a *minimal* system. It reuses the exact dispatch the MCP server already uses
(`IAdminHandlerRegistry` → `IAdminCrudHandler.CreateEmptyUpsertViewModel` /
`GetUpsertViewModelAsync` → JSON field overlay → `SaveUpsertAsync` → `PublishAsync`), so it covers
every current and future content type generically with no per-type code, and inherits rich-text
sanitization, required-field validation, change-set grouping, page slug-collision checks, and route
writing on publish for free.

| Decision | Choice |
|---|---|
| File discovery | Embedded resources in scanned assemblies, a configured disk folder, and explicit registration on `IWebWayCmsBuilder` |
| Identity | Each item carries a stable Guid `id` that is a *seed key*, mapped through a ledger table to a CMS-generated `ContentNode.Id`. `ContentStore` is not modified. |
| Update rule | Re-apply an item only when its content hash differs from the hash recorded last time — so edits made in `/wadmin` survive reboots, but shipping new content in a package release takes effect |
| Deployment mode | **Admin mode only** (`UseWebWayCmsAdmin`). `IAdminHandlerRegistry` does not exist in a rendering-only host. |
| Scope | Top-level content items only — no nested child entities (articles, zone items) in v1 |
| Config | `ContentSeedOptions` bound from a `"ContentSeed"` section, plus a `WEBWAYCMS_SKIP_CONTENTSEED` env var |

## 3. File Format

One shape, no variants:

```json
{
  "items": [
    {
      "id": "3f2c9d18-0a41-4c2e-9c7a-5b1d2e6f8a90",
      "contentType": "pages",
      "publish": true,
      "fields": {
        "title": "About Us",
        "slug": "about",
        "controllerName": "GenericPage",
        "configurationJson": "{}"
      }
    }
  ]
}
```

- `id` — required, a **non-empty** Guid, the stable seed key. An empty id (`00000000-0000-…`) is a
  warning and the item is skipped. Duplicate ids across all sources in one run are a warning; last
  write wins.
- `contentType` — required, must match a **versioned** `IAdminCrudHandler.ContentType` (`pages`,
  `contentblocks`, `articles`, `contentzones`, `widgets`, `pagetypes`, or any host key such as the
  TestHost's `faqs`). Non-versioned types with no node identity (`cmsroutes`, `formcomponents`) are
  warned-and-skipped. Unknown ⇒ warning, item skipped.
- `publish` — optional, defaults to `true`. Ignored when `handler.SupportsPublishing` is false.
- `fields` — required; a JSON object of camelCase field values matching the type's upsert view model.
  A missing or non-object `fields` value is a warning and the item is skipped. This is exactly what
  MCP's `describe_content_type` advertises, so `describe_content_type` doubles as the authoring
  reference.

`nodeId` and `expectedVersionNumber` are stripped from `fields` before overlay (case-insensitively) —
the seeder owns identity and must never let a file trigger a stale-version failure or hijack a node.

## 4. Discovery Order

Sources are collected from three providers, then processed in one deterministic pass ordered by
source name, items in file order:

1. **Embedded resources** — every scanned assembly's manifest resources whose name ends in
   `ContentSeedOptions.ResourceSuffix` (`.contentseed.json`, case-insensitive). The scanned
   assemblies are the host's entry assembly plus `IWebWayCmsBuilder.AddApplicationAssembly` and
   `AddContentSeedAssembly` contributions.
2. **Configured disk folder** — every `*.json` file under `ContentSeedOptions.Path` (default
   `contentseed`, resolved against the host's content root). A blank `Path` scans nothing (mirroring
   a blank `ResourceSuffix`, which matches no embedded resource). A missing directory is empty, not
   an error.
3. **Explicit files** — paths registered via `IWebWayCmsBuilder.AddContentSeedFile`, appended after
   the folder scan.

Malformed JSON in a source is caught per-source (`JsonException` → warning), so one bad file never
blocks the rest. Per-assembly and per-file IO failures are likewise caught and logged. A `null`
`items` array is treated as empty (no items, no error).

## 5. The Hash / Ledger Update Rule

For each item, the seeder computes `SHA256` over the canonical JSON of the item (whitespace-
insensitive), then consults the `ContentSeedRecords` ledger (keyed by the seed `id`):

```
if item.id == Guid.Empty                            -> warning, skip
if item.fields is undefined (missing key)           -> warning, skip
hash = SHA256(canonical JSON of the item)
record = ledger[item.id]
if record exists and record.ContentHash == hash  -> skip (unchanged)
resolve handler for item.contentType              -> null: warning, skip
                                                   -> !SupportsVersionHistory: warning, skip
existing = record?.NodeId == null ? null : handler.GetUpsertViewModelAsync(record.NodeId)
                                                   -> null (deleted in admin) => recreate
model = merge(fields) over (existing ?? handler.CreateEmptyUpsertViewModel())
                                                   -> fields not an object: warning, skip
save = handler.SaveUpsertAsync(model)             -> !Success: warning, skip (hash NOT recorded)
nodeId = save.NodeId ?? record?.NodeId            -> Guid.Empty: warning, skip (hash NOT recorded)
if item.publish && handler.SupportsPublishing:
    publish = handler.PublishAsync(nodeId)        -> !Success: warning, skip (hash NOT recorded)
ledger.upsert(record with ContentHash = hash)
```

Consequences:

- **Editor edits survive reboots** — the hash is computed over the *file's* content, not the
  database's. An admin edit changes the database and leaves the file untouched, so the next boot
  recomputes the same hash as the recorded one and skips the item. A shipped file change produces a
  new hash and re-applies.
- **New content in a release takes effect** — the file's hash changes, so the item is re-applied
  (updated in place, same node id, new version).
- **A failed save or publish is retried** — a `SaveUpsertAsync` or `PublishAsync` failure
  deliberately does **not** record the hash, so the item is retried next boot.

`IAdminCrudHandler.SaveUpsertAsync` is `AdminCrudModel.SaveUpsertAsync`, which already runs
`RichTextSanitizer.Sanitize` and `ModelValidator.Validate` and opens a change-set scope — the seeder
adds none of its own. `PageModel.PublishAsync` writes the route, so a seeded page becomes reachable
without the seeder touching `ICMSRouteService` (unlike `DefaultContentSeeder`, which hand-builds
route rows).

## 6. Configuration

`ContentSeedOptions`, bound from the `"ContentSeed"` section:

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Whether JSON content seeding runs at startup |
| `Path` | `contentseed` | Directory (relative to content root) scanned for `*.json` seed files; a blank value scans nothing |
| `ResourceSuffix` | `.contentseed.json` | Case-insensitive suffix marking an embedded resource as a seed file |

The `WEBWAYCMS_SKIP_CONTENTSEED=true` environment variable is a separate kill switch checked by the
runner (`CmsContentSeedRunner`) before the seeder is even resolved.

## 7. Known Limitation

Because the seed Guid is a key mapped to a generated node id (not the node id itself), a seed file
**cannot cross-reference its own items** — e.g. a child page's `parentNodeId`, or an article's
`articleListNodeId`. The ledger makes a future `"@seed:<guid>"` reference-resolution pass
straightforward, but that is out of scope here. Seeded pages are root pages.

## 8. Host Wiring

```csharp
builder.Services.AddWebWayCms(builder.Configuration, cms =>
{
    cms.AddContentSeedFile("contentseed/site.json");      // explicit file
    cms.AddContentSeedAssembly(typeof(SiteContent).Assembly); // embedded resources
});
```

`IJsonContentSeeder` is registered in `MapAdminTypes` (admin mode only), and
`UseWebWayCmsAdmin` runs `EnsureJsonContentSeeded` **after** `EnsureCodeBasedRoutesSeeded` and
**before** `ConfigureAdminPipeline()` — the only point where registrations seeded content may
reference (page types, widgets) already exist. A rendering-only host never registers or runs it.

## 9. `ContentSeedRecords` Ledger

An unversioned entity (modelled on `CMSRouteDTO`), keyed by the seed id:

- `SeedId` (Guid, PK) — the stable seed key
- `ContentTypeKey`, `NodeId` (indexed), `ContentHash`, `Source`, `AppliedUtc`

`IContentSeedRecordService` exposes `GetAsync(seedId)` / `UpsertAsync(record)`. It lives in
`WebWayCMS.Data` alongside the other plain-row services.
