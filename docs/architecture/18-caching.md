# Area 18: Caching

**Namespaces:**
- `WebWayCMS.Data.Services.Caching` — `ICmsCacheGeneration` / `CmsCacheGeneration`, `ICacheGenerationStore` / `CacheGenerationStore`, `PublicContentChangeInterceptor`, `PublicContentChangeDetector`, `IContentReadCache` / `HybridContentReadCache` / `NullContentReadCache`, `ScopeRunner`
- `WebWayCMS.Data.Models` — `CacheGenerationDTO`
- `WebWayCMS.Caching` — `CacheOptions` / `CacheOptionsValidator`, `CmsPublicOutputCachePolicy`, `CmsCacheInvalidation`, `CacheGenerationMonitor`
- `WebWayCMS.Startup` — `CmsCachingRegistration` (the one DI registration point)

**Depends on:** Data (entities, `CmsDbContext`), Core (the four registries), ContentZones
**Consumed by:** every host, in both deployment modes

---

## 1. Overview

Two caches, one invalidation mechanism:

| Layer | What it caches | Backed by | Evicted by |
|---|---|---|---|
| **Output cache** (browser/CDN layer) | Anonymous `200` GET/HEAD responses on public paths | ASP.NET Core `AddOutputCache` (per-process store) | The cross-host generation stamp |
| **Data read cache** (database layer) | Published-mode content and zone reads (`ContentStore<T>`, `IContentZoneService`) | `AddHybridCache` (`HybridCache`, per-process) | The cross-host generation stamp |

The invalidation mechanism is a **single-row generation stamp in the database**
(`CacheGenerations`, `CacheGenerationDTO { Id, Value }`). Every host that writes public content
bumps it at commit (an EF interceptor); every host polls it (a background service, default 5 s).
When a host observes a higher stamp, it evicts its own output cache by tag, its data cache by tag,
and re-reads the four in-memory registries (routes, widgets, page types, form components).

Why a database stamp and not pub/sub or a shared cache: an admin host and one or more rendering-only
hosts are guaranteed to share a *database* (that is how the split deployment works, see
[11-deployment-modes](11-deployment-modes.md)) but never a filesystem, and they may sit in different
availability zones or behind different load balancers. A single-row `UPDATE … RETURNING` is the
smallest correct shared invalidation channel that fits the existing infrastructure.

## 2. The stamp

| Aspect | Choice | Why |
|---|---|---|
| Storage | One row, `Id = 1`, `Value bigint` | One value for the whole database; the row is created lazily by the first bump. |
| Bump statement | `INSERT … VALUES (1, 1) ON CONFLICT ("Id") DO UPDATE SET "Value" = "Value" + 1 RETURNING "Value"` | Atomic upsert: two concurrent bumps never collide on a value, so every commit moves the stamp exactly once. A missing row is created at **1**, not 0: reads of a missing row count as generation 0, so a first-ever write must still move the stamp. |
| Execution | ADO (`GetDbConnection` + `ExecuteScalarAsync`), not `Database.SqlQuery<T>` | EF 10 refuses to compose queries over a non-`SELECT` statement (`SqlQuery` returning `IQueryable` is only composable for `SELECT`/`WITH`). The bump runs post-commit on its own connection, so the statement auto-commits. |
| No model seed | The row has no `HasData` seed | Host migration contexts (`CmsExtensionDbContext<TSelf>`) mark the table `ExcludeFromMigrations`, but EF *still diffs seed data of excluded tables* — a `HasData` row would demand a spurious host migration on every host that contributes its own tables. |
| Read | LINQ over `Set<CacheGenerationDTO>()` | Missing row → `0`. |

`CacheGenerationStore` is a singleton that opens a **service scope per operation** (`ScopeRunner`):
the bump must run on a fresh `CmsDbContext`, not on the context of the transaction that committed —
the post-commit flush happens after that transaction has ended.

## 3. Local generation: `ICmsCacheGeneration`

A process-local, forward-only counter:

- `Advance(observed)` moves `Current` to `observed` only when it is higher (compare-and-swap loop),
  and raises `Changed` **at most once per value**: only the thread whose compare-and-swap moved it raises.
- `Current` mirrors the shared database stamp only. Cache keys and the output-cache vary value use
  `Key` = `{Current}.{localEpoch}`.
- It is monotonic and never decreases: the poller and the interceptor both feed it, and a host that
  misses a bump (database outage) catches up on the next successful poll.
- When a bump *fails* (the stamp did not move in the database), the writer calls `AdvanceLocal()`:
  its local epoch moves (so `Key` changes and `Changed` fires) and this host evicts immediately,
  without claiming a stamp value the database never issued — the next real bump from any host is
  still seen. The other hosts catch up at their TTL.

## 4. Who bumps: the interceptor and the detector

`PublicContentChangeInterceptor` is registered as an EF interceptor on `CmsDbContext` (before
`AddDbContext`, so the context options can resolve it). On every save it asks
`PublicContentChangeDetector.IsPublicChange(changeTracker.Entries())` and, if the save is public,
bumps the stamp — at the **outermost commit**:

- Outside a transaction: `SavedChangesAsync` flushes immediately (the save is already committed).
- Inside a transaction (page publish, zone writes, route projection): the flag is held until
  `IDbTransactionInterceptor.TransactionCommittedAsync`; a rollback or failure clears it instead, so
  a rolled-back transaction never bumps.
- A flush never fails the write: failures are logged.
- The synchronous hooks (`SaveChanges()`, `Commit()`) behave the same as the async ones.

### What counts as a public change

Visitors only ever see published content, so the detector bumps for:

| Entry | Rule |
|---|---|
| `ContentVersion` | Added as `Published`; Modified with a **real column change** (a change of the `IsCurrentDraft` bookkeeping flag alone — minting a draft on a live item — changes nothing visitors see); Deleted while `Published`. An `Unchanged` version never counts on its own: a save that merely tracks published content does not bump. |
| `ContentNode` | Modified or Deleted (`IsHidden`/`IsDeleted`/`IsArchived` are live at once). |
| `CMSRouteDTO` | Added, Modified or Deleted (the live projection table). |
| `ContentRelation` | Added to or removed from a version that was/is `Published` (the live variant's relations) — including when the version row itself is untouched by the save. |
| `IVersionedContent` rows (DTOs) | Modified or Deleted, when the version they belong to is published as far as the tracked values show. |

Never a bump: drafts (`State = Draft/InReview/Approved/Archived`), change sets, page-set snapshots,
the seed ledger, media blobs, Identity/user rows, API tokens, form-component registration rows
(admin-only rendering).

**Over-bumping is safe** (extra eviction, no staleness); **under-bumping is not**. The detector is
deliberately conservative in that direction.

## 5. Who listens: `CmsCacheInvalidation` and `CacheGenerationMonitor`

`CmsCacheInvalidation` subscribes to `ICmsCacheGeneration.Changed` and, on every bump, evicts:

1. the output cache by tag `cms-public` (`IOutputCacheStore.EvictByTagAsync`),
2. the data cache by tag `cms-content` (`HybridCache.RemoveByTagAsync`),
3. the four in-memory registries (`ICMSRouteRegistry`, `IWidgetRegistry`, `IPageControllerRegistry`,
   `IFormComponentRegistry`) — the seeder may have rewritten them in another host.

Each step is independent: one failing target never blocks the others, and a failure never surfaces to
the writer — the cache TTL is the backstop.

**The subscriber is a lazy singleton that nothing else resolves.** `UseWebWayCmsRendering` and
`UseWebWayCmsAdmin` therefore resolve it at startup (`GetRequiredService<CmsCacheInvalidation>`);
without that, its constructor — the only place the subscription happens — never runs, and generation
advances evict nothing. (The output cache would still self-heal, because every stored response varies
by generation value, see §6; the data cache has no such escape and would serve until its TTL.)

`CacheGenerationMonitor` (a `BackgroundService`) is the **cross-host** half: it reads the stamp every
`Caching:GenerationPollInterval` (default 5 s) and calls `Advance`. In a split deployment this is how
a rendering-only host learns about publishes made on the admin host — within one poll interval. A
failing poll is logged and skipped; the TTL is the backstop, so a transient database outage must not
stop the host from serving.

## 6. The output cache policy: `CmsPublicOutputCachePolicy`

The single `IOutputCachePolicy`, active only when `Caching:Enabled`. It is attached as the output
cache's **base policy** (`OutputCacheOptions.AddBasePolicy`): the middleware reads policies only from
base policies or endpoint metadata, never from DI. `CacheRequestAsync` sets `EnableOutputCaching`,
`AllowCacheLookup` and `AllowCacheStorage` together — the middleware decides lookup and body capture
right after it, so a store-time veto can only switch storage off, never on.

**Cacheable:** anonymous `200` GET/HEAD on public paths — no `Authorization` header. The cache key
carries `VaryByValues["gen"] = <generation Key>`, so a bump on *any* host moves the key and old
responses stop matching even before the tag eviction arrives; it also varies on the whole query
string (`QueryKeys = "*"`, admin-adjacent pages pass `?role=` and friends) and is tagged `cms-public`.

**Excluded paths** (segment-boundary match, so `/wadmin` excludes `/wadmin/users` but not
`/wadmin-x`): `/wadmin`, `/_preview`, `/Identity`, `/api`, the MCP endpoint, `/media` (immutable
bytes with ETag), `/.well-known`.

**Store-time re-checks** (a hit never skips the pipeline, so these run after `CacheRequestAsync`):
the response is stored only if it is `200`, sets no `Set-Cookie`, is not `no-store`/`private`, the
generation did not move meanwhile (the transformer can bump it mid-render via a write), and the
request was not turned into a draft (`PreviewConstants.DraftRequestItemKey`) or snapshot read while
it rendered.

The middleware is ordered `UseAuthentication` → `UseOutputCache` → `UseRouting`: the policy inspects
`User`, and a cache hit skips the route transformer, rate limiting and authorization — safe because
only anonymous 200s are ever stored.

## 7. The data read cache

`IContentReadCache.GetOrCreateAsync(key, factory)` is the one read-through seam. It is wired into the
two read engines:

- `ContentStore<T>.PublishedCachedAsync` — every **Published**-mode read (a rendering-only host is
  always in this mode; an admin host is in this mode for every non-preview request). Draft, admin and
  snapshot reads never touch the cache.
- `ContentZoneService.PublishedCachedAsync` — the same for zone reads.
- Neither caches while the context has an open transaction (`CmsDbContext.HasOpenTransaction`): such a
  read may see the transaction's uncommitted writes (a rollback would leave them cached, since a
  rollback never bumps) and must see them (a cached pre-transaction value would hide them from the
  writer, e.g. the global-zone create's in-transaction re-read).

Key format: `{contentTypeKey}:{suffix}:{culture}:{segment}` for store reads, `zonesvc:{suffix}:{culture}:{segment}`
for zone reads. `HybridContentReadCache` prefixes every key with the **current generation**
(`cms:{generation Key}:{key}`): a factory that races a publish lands under a dead key and is evicted by
the tag anyway, so a render racing a publish can never be served under the new generation.

Failure policy: a factory exception propagates untouched with its original stack (the read really
failed); a caller cancellation (an aborted request) propagates and leaves the type eligible; a cache-side
failure (a type that doesn't round-trip through `HybridCache` serialization) is logged once and the
type is **bypassed for the process lifetime** — a host DTO that doesn't serialize must not break
reads. `NullContentReadCache` (registered when `Caching:Enabled = false`) runs every factory directly.

## 8. Configuration

`"Caching"` section (`CacheOptions`), validated at startup (`CacheOptionsValidator`, ranges enforced):

| Key | Default | Range | Meaning |
|---|---|---|---|
| `Caching:Enabled` | `true` | — | Turns the policy, the read-through and the poller on/off. When off, the stores are still registered (the invalidation subscriber needs its targets) and `NullContentReadCache` is used. |
| `Caching:OutputCacheExpiration` | `10:00:00` | 1 s – 1 d | TTL of anonymous public responses. |
| `Caching:DataCacheExpiration` | `10:00:00` | 1 s – 1 d | TTL of published-mode reads. |
| `Caching:GenerationPollInterval` | `00:00:05` | 0 (off) – 10 min | Cross-host poll interval. Zero disables the poller: the host then relies on its own interceptor-driven bumps and the TTLs. |

## 9. Staleness semantics

| Event | This host | Other hosts |
|---|---|---|
| Public save commits here | Evicted within milliseconds (the bump fires `Changed` locally) | Within one poll interval (default 5 s), or at TTL |
| Draft-only save | Not evicted (the detector ignores it) | Same |
| Bump fails (DB error at flush time) | Evicted (local epoch) | At TTL — the stamp never moved |
| **Direct SQL write** (bypassing the interceptor) | Not evicted | Not evicted — stale until the next public write moves the stamp, or the TTL. This is by design: the stamp is the channel, and anything that writes outside the interceptor is outside the system. |
| Database outage during a poll | Logged, skipped; serving continues | Same |

The TTL is the universal backstop: no failure mode leaves a host serving content older than
`OutputCacheExpiration` / `DataCacheExpiration`.

## 10. Deployment modes

Both modes register the whole stack (`CmsCachingRegistration` runs from both `AddWebWayCmsRendering`
and `AddWebWayCmsAdmin`): a rendering-only host writes nothing, but it still *polls* — that is exactly
where the split-deployment staleness is solved. The admin host writes, so its own writes evict its own
caches instantly and are picked up by the renderers on their next poll. The integration host
(`scripts/StartIntegrationHost.sh`) runs both against one database and verifies the full loop
end-to-end, including the draft-only no-bump and the direct-SQL staleness cases.
