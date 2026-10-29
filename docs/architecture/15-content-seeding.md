# Area 15: JSON Content Seeding

**Namespaces:**
- `WebWayCMS.Services.ContentSeeding` — `JsonContentSeeder`, `IContentSeedSourceProvider`,
  `AssemblyContentSeedSourceProvider`, `FileContentSeedSourceProvider`, `ContentSeedOptions`,
  `ContentSeedSource`, `ContentSeedDocument`, `ContentSeedItem`, `SeedReferenceResolver`
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
| Scope | Top-level content items, plus **zone items** placed by a `zone` locator (§10). Other child entities are not seeded. Articles are top-level: seed an `articlelists` item and an `articles` item whose `articleListId` is `@seed:{listSeedId}` |
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
  `contentblocks`, `articles`, `contentzones`, `contentzoneitems` (zone items, §10), `widgets`, `pagetypes`, or any host key such as the
  TestHost's `faqs`). Non-versioned types with no node identity (`cmsroutes`, `formcomponents`) are
  warned-and-skipped. Unknown ⇒ warning, item skipped.
- `publish` — optional, defaults to `true`. Ignored when `handler.SupportsPublishing` is false.
- `fields` — required; a JSON object of camelCase field values matching the type's upsert view model.
  A missing or non-object `fields` value is a warning and the item is skipped. This is exactly what
  MCP's `describe_content_type` advertises, so `describe_content_type` doubles as the authoring
  reference.
- `@seed:{guid}` — a string field value may reference another seeded item's id with an `@seed:`
  token, which is replaced with the referenced item's generated node id at apply time (see §7
  Cross-item references).

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
overlay = fields as object                          -> not an object: warning, skip
strip nodeId / expectedVersionNumber from overlay
refs = collect @seed:{guid} tokens from overlay
for each ref: map[ref] = ledger[ref].NodeId when present and != Guid.Empty
overlay = substitute @seed tokens using map         -> any unresolved: defer (skip, hash NOT recorded)
model = merge(overlay) over (existing ?? handler.CreateEmptyUpsertViewModel())
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

## 7. Cross-item References (`@seed:{guid}`)

Because the seed Guid is a key mapped to a generated node id (not the node id itself), an item that
needs to point at another seeded item — a child page's `parentNodeId`, or an entity picker such as
`ConfiguredPage.FeaturedFaqId` — references it with an `@seed:` token carrying the target item's seed
id:

```json
"fields": {
  "parentNodeId": "@seed:22222222-0000-4000-8000-000000000002",
  "configurationJson": "{\"featuredFaqId\":\"@seed:11111111-0000-4000-8000-000000000001\"}"
}
```

- **Token format** — `@seed:` followed by a Guid in canonical form. Substitution is a plain string
  replace (`SeedReferenceResolver`), so a token works both as a whole string value (a bare guid that
  deserializes to `Guid?`) and embedded inside a serialized-JSON string such as a page-type or widget
  `configurationJson` — the case that matters most, since every page-type and widget config travels as
  a JSON string.
- **Resolution** — at apply time the seeder collects every referenced seed id, looks each up in the
  `ContentSeedRecords` ledger, and maps it to `record.NodeId` when the record exists and its node id
  is non-empty.
- **Deferred pass** — resolution depends only on what has already been applied, so file/source
  ordering never decides whether a reference resolves. `SeedAsync` re-applies any item left with an
  unresolved reference after every other item has had a chance to run, and stops only when a pass
  makes no progress.
- **Unresolved ⇒ retry next boot** — an item that still has an unresolved reference is never saved and
  its hash is never recorded (exactly like a failed save), so it is retried on the next boot.
- **Cycles** — self-references and mutually-referencing items resolve to "no progress" and are
  reported (a warning naming the item) rather than looped on.
- **Hashes are reference-stable** — the hash is computed over the raw item *before* substitution, so a
  fixture's hash does not depend on generated node ids.

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

---

## 10. Zone Items

A seed item with a `zone` locator is a **zone item** (a widget placed in a content zone). It uses
`"contentType": "contentzoneitems"` and is saved through the ordinary top-level dispatch of that handler
(`ContentZoneItemModel.SaveUpsertAsync`), with the zone the locator resolves to written into its
`contentZoneNodeId` field (any value the file gives there is dropped), so it gets the same metadata
normalization and widget-route registration as a zone item created in the zone editor or over MCP.

**Legacy form.** Before zone items became their own content type they were seeded as
`"contentType": "contentzones"` with a `zone` locator. That form is still accepted as an alias for
`contentzoneitems`, with a warning ("uses the deprecated contentType…") once per item per seed run; a
`zone` locator on any other content type is skipped with a warning.

```json
{
  "id": "44444444-0000-4000-8000-000000000002",
  "contentType": "contentzoneitems",
  "zone": { "page": "@seed:22222222-0000-4000-8000-000000000001", "slot": "Main" },
  "fields": {
    "componentName": "Faq",
    "componentPropertiesJson": "{\"ListViewName\":\"Accordion\",\"FeaturedFaqId\":\"@seed:11111111-0000-4000-8000-000000000001\"}",
    "metadataJson": "{\"DisplayMode\":\"List\"}",
    "isActive": true
  }
}
```

- **Locator** — exactly one of:
  - `{ "page": …, "slot": "Main" }`: a zone the page's template renders;
  - `{ "item": …, "slot": "Sidebar" }`: a nested zone owned by another zone item (e.g. a Layout column);
  - `{ "name": "SiteFooter" }`: a global, name-based zone.

  `page`/`item` take a node id or an `@seed:{guid}` reference, resolved (and deferred) like any other
  reference. The zone is created when it does not exist yet (`IContentZoneService.GetOrCreateBy…Async`),
  exactly as first rendering the page would, so a fresh database needs no page visit. An invalid locator
  (none or several owners set, or no `slot` with `page`/`item`) is a warning and the item is skipped. So
  is an owner that does not exist or is the wrong kind of node: `page` must be an existing, non-deleted
  page (`IPageModel.GetVersionHistoryAsync`) and `item` an existing zone item. Nothing is created for it,
  so a stale id never aborts the run with an FK error or attaches a zone to the wrong node.
- **Fields** — `ContentZoneItemUpsertViewModel` in camelCase. `contentZoneNodeId` is stripped (placement
  comes only from the locator). **Set `"isActive": true`**: the upsert model defaults it to `false`, and
  the seeder adds no zone-specific defaults. `componentPropertiesJson` is the widget config as a JSON
  string with PascalCase keys (the zone editor's format), and may carry `@seed:` tokens.
- **Ordering** — top-level items and zone items share one deferred-retry loop, with every top-level item
  ahead of every zone item in each pass, so pages, blocks, images and so on referenced by a locator or a
  widget config resolve in the first pass (and a top-level item may reference a zone item: it resolves on
  the next pass). Zone items apply in file order, each appended to its zone. When a zone item defers, every
  later item naming the same zone (same raw locator) defers with it, so a zone's items are always added in
  file order. Re-applying a changed item updates it in place and keeps its position.
- **Changed locator** — an item whose locator now names another zone of the **same page** is moved there
  (`MoveRelatedAsync` on the `Zone` containment, to the end) *before* it is saved. A move to another page, or into or out of a global zone,
  is not possible (zone moves stay within one page), and neither is a rejected move (e.g. into the item's
  own nested zone): the item is left untouched — nothing saved, nothing recorded — and a warning repeats
  every boot until the seed is fixed. To place content on another page, give it a new seed id.
- **Publishing respects the page's own state.** The page is never (re)published by a zone item. After all
  items apply, the `publish: true` items of each owning page are handled once per page:
  - page **live** → just those items are published, through `IPageModel.PublishZoneItemsAsync` (one change
    set; their widget routes are registered). The page's other pending drafts — an admin's unfinished
    edits, and `publish: false` seed items — stay pending.
  - page **not published** (seeded `publish: false`, unpublished in the admin, or its own publish failed)
    → the items stay drafts and go live when the page is next published.

  A `publish: false` page-zone item is saved as a draft and never published by the seeder. Global zones
  have no drafts (their writes go live), so a `publish: false` item in a `name` zone is a warning and is
  skipped — use `"isActive": false` to seed a hidden global item.
- **Ledger** — a `publish: true` page-zone item is recorded with an **empty hash** as soon as it saves (so
  a nested item's locator can reference it in the same run), then re-recorded with its real hash once it
  publishes, or straight away when its page is not live. An item whose publish fails keeps the empty hash:
  the next boot updates the same item (no duplicate) and retries. A widget-route failure on an item that
  did go live is logged and recorded, since re-applying the same seed could not fix it.
- **Hash stability** — `zone` is omitted from the hashed item when null, so adding the locator changed no
  existing top-level item's hash.
- A zone item deleted in the admin is re-created (appended) the next time its hash changes, like a
  top-level item.

## Media references (`@media:{path}`)

A seed item may reference an image file shipped beside the seed file:

```json
{ "id": "…", "contentType": "images",
  "fields": { "title": "Logo", "altText": "Our logo", "blobHash": "@media:images/logo.png" } }
```

`SeedMediaResolver` loads the file, validates it exactly as an admin upload is validated, stores it
in the media library, and substitutes the resulting content hash. Like `@seed:`, substitution is a
plain string replace, so a token works as a whole field value or embedded inside a serialized-JSON
string.

Paths resolve relative to the seed file's own directory, or as a manifest resource of the seed
resource's assembly — `ContentSeedSource` carries its declaring `Assembly` for this. A path may not
escape the seed file's directory. An unresolvable or invalid reference is not saved and not
hash-recorded, so the item retries on the next boot, matching the `@seed:` contract.

Because media is content-addressed, re-running a seed that references the same file stores nothing
new. See [16-media-and-images](16-media-and-images.md).
