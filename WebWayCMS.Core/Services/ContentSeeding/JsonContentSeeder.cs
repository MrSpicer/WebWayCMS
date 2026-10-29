using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

using Serilog;

using WebWayCMS.Content;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.Page;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Services.ContentSeeding;

/// <summary>
/// Applies JSON content seed files by reusing the admin CRUD dispatch (handler registry → upsert view
/// model → field overlay → save → publish), so every current and future content type is covered with no
/// per-type code. Identity is tracked through the <see cref="IContentSeedRecordService"/> ledger: an item
/// is only re-applied when its content hash differs from the one recorded last time, so admin edits survive
/// reboots while shipped content changes take effect.
/// <para>
/// An item carrying a <see cref="ContentSeedItem.Zone"/> locator is a zone item: it is saved through the
/// handler's child dispatch into the zone the locator names (created on demand). Zone items apply after
/// every top-level item, in file order. A page's own publish state is respected: at the end, the
/// <c>publish: true</c> items of each page that is already live are published on their own (never the page,
/// so unrelated drafts stay pending); on an unpublished page they stay drafts until the page is published.
/// </para>
/// </summary>
public sealed class JsonContentSeeder : IJsonContentSeeder
{
    private static readonly IQueryCollection EmptyQuery =
        new QueryCollection(new Dictionary<string, StringValues>());

    private static readonly HashSet<string> IdentityKeys =
        new(StringComparer.OrdinalIgnoreCase) { "nodeId", "expectedVersionNumber" };

    // A zone item's placement comes from its locator, never from its fields.
    private const string ZoneKeyField = "contentZoneNodeId";

    // The zone item content type: the only type a seed item with a 'zone' locator may name.
    private const string ZoneItemContentType = ContentRelationRegistry.ContentZoneItemContentType;

    // Key of the locator's owner (page or item) in the JSON node the @seed resolver walks.
    private const string OwnerKey = "owner";

    private enum ApplyOutcome
    {
        Applied,
        Skipped,
        Deferred,
    }

    private sealed record QueuedZoneItem(ContentSeedItem Item, ContentSeedSource Source, Guid NodeId, string Hash);

    // A resolved zone: its node and the page whose zone tree it belongs to (null for a global zone).
    private sealed record ZoneTarget(Guid ZoneNodeId, Guid? PageNodeId);

    /// <summary>Per-run state: lookups that cannot change during a run, and the zone items awaiting publish.</summary>
    private sealed class SeedRun
    {
        public Dictionary<Guid, Guid?> ZonePages { get; } = new();

        public Dictionary<Guid, bool> PageExists { get; } = new();

        public Dictionary<Guid, List<QueuedZoneItem>> ItemsToPublish { get; } = new();
    }

    private readonly IEnumerable<IContentSeedSourceProvider> _providers;
    private readonly IAdminHandlerRegistry _registry;
    private readonly IContentSeedRecordService _records;
    private readonly ContentSeedOptions _options;

    // Optional: a host wired without media support simply leaves @media: tokens alone. The shipped
    // admin registration always supplies one.
    private readonly SeedMediaResolver? _mediaResolver;

    // Optional: without them zone items are warned about and skipped. The shipped admin registration
    // always supplies both.
    private readonly IContentZoneService? _zoneService;
    private readonly IPageModel? _pageModel;
    private readonly ILogger _logger = Log.ForContext<JsonContentSeeder>();

    public JsonContentSeeder(
        IEnumerable<IContentSeedSourceProvider> providers,
        IAdminHandlerRegistry registry,
        IContentSeedRecordService records,
        IOptions<ContentSeedOptions> options,
        SeedMediaResolver? mediaResolver = null,
        IContentZoneService? zoneService = null,
        IPageModel? pageModel = null)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _mediaResolver = mediaResolver;
        _zoneService = zoneService;
        _pageModel = pageModel;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            _logger.Information("Skipping JSON content seeding because ContentSeed:Enabled is false.");
            return;
        }

        var sources = _providers
            .SelectMany(p => p.GetSources())
            .OrderBy(s => s.Name, StringComparer.Ordinal);

        var seenIds = new Dictionary<Guid, string>();
        var pending = new List<(ContentSeedItem Item, ContentSeedSource Source)>();

        foreach (var source in sources)
        {
            if (Deserialize(source) is not { } document)
                continue;

            foreach (var item in document.Items ?? [])
            {
                if (seenIds.TryGetValue(item.Id, out var firstSource))
                    _logger.Warning("Duplicate content seed id '{SeedId}' in source '{Source}' (first seen in '{FirstSource}'); last write wins.", item.Id, source.Name, firstSource);
                else
                    seenIds[item.Id] = source.Name;

                pending.Add((item, source));
            }
        }

        // Zone items come after every top-level item, so a locator or config referencing seeded content
        // (pages, blocks, images) resolves in the first pass and file order decides each zone's order.
        // One retry loop covers both, so a top-level item may also reference a zone item.
        var run = new SeedRun();
        var ordered = pending.Where(p => p.Item.Zone == null).Concat(pending.Where(p => p.Item.Zone != null)).ToList();
        await ApplyPassesAsync(ordered, run, ct);
        await PublishZoneItemsAsync(run, ct);
    }

    private async Task ApplyPassesAsync(
        List<(ContentSeedItem Item, ContentSeedSource Source)> pending,
        SeedRun run,
        CancellationToken ct)
    {
        // Deferred-retry pass: an item whose @seed reference has not been applied yet is retried after
        // every other item has had a chance to run, so file/source ordering never decides whether a
        // reference resolves. The loop stops when a pass makes no progress (empty, or every pending
        // item deferred again — cycles and permanently missing references).
        while (pending.Count > 0)
        {
            var deferred = new List<(ContentSeedItem Item, ContentSeedSource Source)>();

            // Once a zone item defers, the later items naming the same zone defer with it, so a zone's
            // items are always added in file order.
            var blockedZones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (item, source) in pending)
            {
                var zoneKey = item.Zone == null ? null : ZoneKey(item.Zone);
                if (zoneKey != null && blockedZones.Contains(zoneKey))
                {
                    deferred.Add((item, source));
                    continue;
                }

                if (await ApplyItemAsync(item, source, run, ct) == ApplyOutcome.Deferred)
                {
                    deferred.Add((item, source));
                    if (zoneKey != null)
                        blockedZones.Add(zoneKey);
                }
            }

            if (deferred.Count == 0 || deferred.Count == pending.Count)
            {
                pending = deferred;
                break;
            }

            pending = deferred;
        }

        foreach (var (item, _) in pending)
            _logger.Warning("Content seed item '{SeedId}' has unresolved @seed reference(s); will retry next boot.", item.Id);
    }

    private async Task<ApplyOutcome> ApplyItemAsync(
        ContentSeedItem item,
        ContentSeedSource source,
        SeedRun run,
        CancellationToken ct)
    {
        if (item.Id == Guid.Empty)
        {
            _logger.Warning("Content seed item has no 'id'; skipping.");
            return ApplyOutcome.Skipped;
        }

        if (item.Fields.ValueKind == JsonValueKind.Undefined)
        {
            _logger.Warning("Content seed item '{SeedId}' has no 'fields' object; skipping.", item.Id);
            return ApplyOutcome.Skipped;
        }

        var hash = ComputeHash(item);
        var record = await _records.GetAsync(item.Id, ct);

        if (record != null && string.Equals(record.ContentHash, hash, StringComparison.Ordinal))
        {
            _logger.Debug("Content seed item '{SeedId}' is unchanged; skipping.", item.Id);
            return ApplyOutcome.Skipped;
        }

        var handler = _registry.GetHandler(item.ContentType);
        if (handler == null)
        {
            _logger.Warning("Content seed item '{SeedId}' references unknown content type '{ContentType}'; skipping.", item.Id, item.ContentType);
            return ApplyOutcome.Skipped;
        }

        if (item.Zone != null)
            return await ApplyZoneItemAsync(item, source, handler, record, hash, run, ct);

        // Only versioned content has a ContentNode identity the ledger can track. Non-versioned
        // types (cmsroutes, formcomponents) return no NodeId on save, so they are skipped up front
        // rather than recorded with a meaningless key.
        if (!handler.SupportsVersionHistory)
        {
            _logger.Warning("Content seed item '{SeedId}' references non-versioned content type '{ContentType}'; skipping.", item.Id, item.ContentType);
            return ApplyOutcome.Skipped;
        }

        object? existing = null;
        if (record != null)
            existing = await handler.GetUpsertViewModelAsync(record.NodeId, EmptyQuery, ct);

        var overlay = ContentFieldMerger.TryGetObject(item.Fields);
        if (overlay == null)
        {
            _logger.Warning("Content seed item '{SeedId}' has a non-object 'fields' value; skipping.", item.Id);
            return ApplyOutcome.Skipped;
        }

        // The seeder owns identity: strip any id/version keys so a file can never trigger a
        // stale-version failure or hijack a node.
        StripIdentityKeys(overlay);

        if (await ResolveTokensAsync(item, source, overlay, null, ct) is { } tokenOutcome)
            return tokenOutcome;

        var model = ContentFieldMerger.TryMerge(existing ?? handler.CreateEmptyUpsertViewModel(), overlay)!;

        var result = await handler.SaveUpsertAsync(model, ct);
        if (!result.Success)
        {
            // Hash is deliberately NOT recorded, so the item is retried on the next boot.
            _logger.Warning("Content seed item '{SeedId}' failed to save: {ErrorMessage}; will retry next boot.", item.Id, result.ErrorMessage);
            return ApplyOutcome.Skipped;
        }

        var nodeId = result.NodeId ?? record?.NodeId ?? Guid.Empty;

        if (nodeId == Guid.Empty)
        {
            _logger.Warning("Content seed item '{SeedId}' saved without a node id; skipping.", item.Id);
            return ApplyOutcome.Skipped;
        }

        if (item.Publish && handler.SupportsPublishing)
        {
            var publishResult = await handler.PublishAsync(nodeId, ct);
            if (!publishResult.Success)
            {
                // Hash is deliberately NOT recorded, so the item is retried on the next boot.
                _logger.Warning("Content seed item '{SeedId}' failed to publish: {ErrorMessage}; will retry next boot.", item.Id, publishResult.ErrorMessage);
                return ApplyOutcome.Skipped;
            }
        }

        await RecordAsync(item, source, nodeId, hash, ct);
        return ApplyOutcome.Applied;
    }

    private async Task<ApplyOutcome> ApplyZoneItemAsync(
        ContentSeedItem item,
        ContentSeedSource source,
        IAdminCrudHandler handler,
        ContentSeedRecordDTO? record,
        string hash,
        SeedRun run,
        CancellationToken ct)
    {
        if (!string.Equals(handler.ContentType, ZoneItemContentType, StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warning("Content seed item '{SeedId}' has a 'zone' but content type '{ContentType}' is not \"{ZoneItemContentType}\"; skipping.", item.Id, item.ContentType, ZoneItemContentType);
            return ApplyOutcome.Skipped;
        }

        if (_zoneService == null || _pageModel == null)
        {
            _logger.Warning("Content seed item '{SeedId}' is a zone item but no zone service or page model is registered; skipping.", item.Id);
            return ApplyOutcome.Skipped;
        }

        var locator = BuildLocator(item.Zone!);
        if (locator == null)
        {
            _logger.Warning("Content seed item '{SeedId}' has an invalid 'zone': set exactly one of 'page' or 'item' (with a 'slot'), or 'name'; skipping.", item.Id);
            return ApplyOutcome.Skipped;
        }

        var overlay = ContentFieldMerger.TryGetObject(item.Fields);
        if (overlay == null)
        {
            _logger.Warning("Content seed item '{SeedId}' has a non-object 'fields' value; skipping.", item.Id);
            return ApplyOutcome.Skipped;
        }

        StripIdentityKeys(overlay);
        foreach (var key in overlay.Select(p => p.Key).Where(k => string.Equals(k, ZoneKeyField, StringComparison.OrdinalIgnoreCase)).ToArray())
            overlay.Remove(key);

        if (await ResolveTokensAsync(item, source, overlay, locator, ct) is { } tokenOutcome)
            return tokenOutcome;

        var zone = await ResolveZoneAsync(item, locator, run, ct);
        if (zone == null)
            return ApplyOutcome.Skipped;

        // Global-zone writes go live immediately; there is no draft to leave unpublished.
        if (zone.PageNodeId == null && !item.Publish)
        {
            _logger.Warning("Content seed item '{SeedId}' is in a global zone, whose items go live on write, so 'publish: false' cannot be honoured; set \"isActive\": false to keep it hidden. Skipping.", item.Id);
            return ApplyOutcome.Skipped;
        }

        // A changed locator moves the stored item before it is saved (the save then edits the moved draft).
        // A move is only possible within one page's zone tree, so anything else is left untouched (nothing
        // saved or recorded) and reported on every boot until the seed is fixed.
        var stored = record == null ? null : await _zoneService.GetItemByNodeIdAsync(record.NodeId, ct);
        if (stored != null && stored.ContentZoneNodeId != zone.ZoneNodeId)
        {
            var storedPage = await GetZonePageAsync(stored.ContentZoneNodeId, run, ct);
            if (storedPage == null || storedPage != zone.PageNodeId)
            {
                _logger.Warning("Content seed item '{SeedId}' names a zone on a different page (or a global zone) from where it is stored; a zone item can only move within one page. Give it a new seed id to place it there. Skipping.", item.Id);
                return ApplyOutcome.Skipped;
            }

            var (moved, moveError) = await handler.MoveRelatedAsync(
                ZoneRelations.Zone, stored.Version.Node.Id, zone.ZoneNodeId, int.MaxValue, null, ct);
            if (!moved)
            {
                _logger.Warning("Content seed item '{SeedId}' could not be moved to its zone: {ErrorMessage}; skipping.", item.Id, moveError);
                return ApplyOutcome.Skipped;
            }
        }

        // The locator names the zone: it fills the item's zone relation (a create places it there; an
        // update never moves it, which the move above did).
        overlay[ZoneKeyField] = zone.ZoneNodeId.ToString();
        var existing = stored == null ? null : await handler.GetUpsertViewModelAsync(stored.Version.Node.Id, EmptyQuery, ct);
        var model = ContentFieldMerger.TryMerge(existing ?? handler.CreateEmptyUpsertViewModel(), overlay)!;

        var result = await handler.SaveUpsertAsync(model, ct);
        if (!result.Success)
        {
            // Hash is deliberately NOT recorded, so the item is retried on the next boot.
            _logger.Warning("Content seed item '{SeedId}' failed to save: {ErrorMessage}; will retry next boot.", item.Id, result.ErrorMessage);
            return ApplyOutcome.Skipped;
        }

        if (result.NodeId is not { } nodeId || nodeId == Guid.Empty)
        {
            _logger.Warning("Content seed item '{SeedId}' saved without a node id; skipping.", item.Id);
            return ApplyOutcome.Skipped;
        }

        if (item.Publish && zone.PageNodeId is { } page)
        {
            // A page-zone item is a draft until it is published. Record the node now (a nested item's locator
            // may reference it) with an empty hash; the real hash is recorded by PublishZoneItemsAsync, so a
            // failed publish retries next boot against this same item.
            await RecordAsync(item, source, nodeId, string.Empty, ct);
            if (!run.ItemsToPublish.TryGetValue(page, out var queued))
                run.ItemsToPublish[page] = queued = [];
            queued.Add(new QueuedZoneItem(item, source, nodeId, hash));
            return ApplyOutcome.Applied;
        }

        // A global-zone item is already live; a 'publish: false' page-zone item stays a draft until its page
        // is next published.
        await RecordAsync(item, source, nodeId, hash, ct);
        return ApplyOutcome.Applied;
    }

    /// <summary>
    /// Publishes the queued <c>publish: true</c> zone items of every page that is live, item by item: the page
    /// itself is never (re)published, so an unpublished page stays unpublished and an admin's other pending
    /// drafts on it stay pending. On a page that is not live the items are left as drafts for its next publish.
    /// </summary>
    private async Task PublishZoneItemsAsync(SeedRun run, CancellationToken ct)
    {
        foreach (var (pageNodeId, items) in run.ItemsToPublish)
        {
            // Read now, not cached: a page seeded in this same run may have been published after its items applied.
            var history = await _pageModel!.GetVersionHistoryAsync(pageNodeId, ct);
            if (history == null || !history.Versions.Any(v => v.IsPublished))
            {
                _logger.Information("Page {PageNodeId} is not published; its seeded zone items stay drafts until it is.", pageNodeId);
                foreach (var queued in items)
                    await RecordAsync(queued.Item, queued.Source, queued.NodeId, queued.Hash, ct);
                continue;
            }

            var result = await _pageModel.PublishZoneItemsAsync(pageNodeId, items.Select(i => i.NodeId).ToList(), ct);
            if (result.Errors.Count > 0)
                _logger.Warning("Publishing seeded zone items on page {PageNodeId} reported errors: {Errors}", pageNodeId, string.Join(" ", result.Errors));

            // An item that failed to publish keeps its empty-hash record and retries next boot. A route failure
            // on an item that did go live is not retried: re-applying the same seed could not fix it.
            var failed = result.FailedItemNodeIds.ToHashSet();
            foreach (var queued in items.Where(i => !failed.Contains(i.NodeId)))
                await RecordAsync(queued.Item, queued.Source, queued.NodeId, queued.Hash, ct);
        }
    }

    /// <summary>
    /// Validates a locator and returns the JSON node its owner reference is resolved in: <c>{ "owner": … }</c>
    /// for a page or item slot, empty for a named zone. Null when the locator is invalid.
    /// </summary>
    private static JsonObject? BuildLocator(ContentSeedZoneLocator zone)
    {
        var page = Blank(zone.Page) ? null : zone.Page;
        var owner = Blank(zone.Item) ? page : zone.Item;
        var set = (page == null ? 0 : 1) + (Blank(zone.Item) ? 0 : 1) + (Blank(zone.Name) ? 0 : 1);

        if (set != 1)
            return null;
        if (owner == null)
            return [];
        return Blank(zone.Slot) ? null : new JsonObject { [OwnerKey] = owner };
    }

    /// <summary>The raw (pre-resolution) identity of the zone a locator names, for ordering only.</summary>
    private static string ZoneKey(ContentSeedZoneLocator zone)
        => string.Join("|", zone.Page?.Trim(), zone.Item?.Trim(), zone.Name?.Trim(), zone.Slot?.Trim());

    /// <summary>
    /// Resolves (creating on demand) the zone a locator names, after checking that its owner exists and is the
    /// right kind of node. Null — with a warning logged — when the owner is not a valid page or zone item.
    /// </summary>
    private async Task<ZoneTarget?> ResolveZoneAsync(ContentSeedItem item, JsonObject locator, SeedRun run, CancellationToken ct)
    {
        var zone = item.Zone!;
        if (!locator.ContainsKey(OwnerKey))
            return new ZoneTarget((await _zoneService!.GetOrCreateByNameAsync(zone.Name!, ct)).Version.Node!.Id, null);

        if (!Guid.TryParse(locator[OwnerKey]!.GetValue<string>(), out var owner))
        {
            _logger.Warning("Content seed item '{SeedId}' has a 'zone' owner that is not a node id or @seed reference; skipping.", item.Id);
            return null;
        }

        if (!Blank(zone.Page))
        {
            if (!await PageExistsAsync(owner, run, ct))
            {
                _logger.Warning("Content seed item '{SeedId}' has a 'zone.page' ({Owner}) that is not an existing page; skipping.", item.Id, owner);
                return null;
            }

            var (pageZone, _) = await _zoneService!.GetOrCreateByPageSlotAsync(owner, zone.Slot!, ct);
            run.ZonePages[pageZone.Version.Node!.Id] = owner;
            return new ZoneTarget(pageZone.Version.Node!.Id, owner);
        }

        var ownerItem = await _zoneService!.GetItemByNodeIdAsync(owner, ct);
        if (ownerItem == null)
        {
            _logger.Warning("Content seed item '{SeedId}' has a 'zone.item' ({Owner}) that is not an existing zone item; skipping.", item.Id, owner);
            return null;
        }

        var page = await GetZonePageAsync(ownerItem.ContentZoneNodeId, run, ct);
        var (nested, _) = await _zoneService.GetOrCreateByItemSlotAsync(owner, zone.Slot!, ct);
        run.ZonePages[nested.Version.Node!.Id] = page;
        return new ZoneTarget(nested.Version.Node!.Id, page);
    }

    private async Task<bool> PageExistsAsync(Guid pageNodeId, SeedRun run, CancellationToken ct)
    {
        if (!run.PageExists.TryGetValue(pageNodeId, out var exists))
        {
            var history = await _pageModel!.GetVersionHistoryAsync(pageNodeId, ct);
            run.PageExists[pageNodeId] = exists = history != null && !history.Versions.Any(v => v.IsDeleted);
        }

        return exists;
    }

    private async Task<Guid?> GetZonePageAsync(Guid zoneNodeId, SeedRun run, CancellationToken ct)
    {
        if (!run.ZonePages.TryGetValue(zoneNodeId, out var page))
            run.ZonePages[zoneNodeId] = page = await _zoneService!.GetParentPageNodeForZoneAsync(zoneNodeId, ct);
        return page;
    }

    /// <summary>
    /// Resolves the <c>@seed:</c> and <c>@media:</c> tokens in <paramref name="overlay"/> (and the zone
    /// <paramref name="locator"/>, when given) in place. Returns null when every token resolved, else the
    /// outcome the item should report.
    /// </summary>
    private async Task<ApplyOutcome?> ResolveTokensAsync(
        ContentSeedItem item,
        ContentSeedSource source,
        JsonObject overlay,
        JsonObject? locator,
        CancellationToken ct)
    {
        // Resolve @seed:{guid} references against the ledger before overlay. The hash is computed over
        // the raw item, so it never depends on generated node ids.
        var resolved = new Dictionary<Guid, Guid>();
        var references = SeedReferenceResolver.CollectReferences(overlay)
            .Concat(SeedReferenceResolver.CollectReferences(locator))
            .Distinct();
        foreach (var seedId in references)
        {
            var refRecord = await _records.GetAsync(seedId, ct);
            if (refRecord != null && refRecord.NodeId != Guid.Empty)
                resolved[seedId] = refRecord.NodeId;
        }

        var unresolved = SeedReferenceResolver.Substitute(overlay, resolved).Count
            + (locator == null ? 0 : SeedReferenceResolver.Substitute(locator, resolved).Count);
        if (unresolved > 0)
        {
            // Hash is deliberately NOT recorded, so the item is retried (later this run, or next boot).
            _logger.Debug("Content seed item '{SeedId}' references an unresolved @seed token; deferring.", item.Id);
            return ApplyOutcome.Deferred;
        }

        // Resolve @media:{path} tokens by storing the referenced file and substituting its hash.
        // An unresolvable path is not saved and not hash-recorded, so it retries on the next boot --
        // the same contract as an unresolved @seed reference.
        var unresolvedMedia = _mediaResolver == null
            ? []
            : await _mediaResolver.SubstituteAsync(overlay, source, ct);
        if (unresolvedMedia.Count > 0)
        {
            _logger.Warning("Content seed item '{SeedId}' references media that could not be loaded: {Paths}; will retry next boot.",
                item.Id, string.Join(", ", unresolvedMedia));
            return ApplyOutcome.Skipped;
        }

        return null;
    }

    private async Task RecordAsync(ContentSeedItem item, ContentSeedSource source, Guid nodeId, string hash, CancellationToken ct)
    {
        await _records.UpsertAsync(new ContentSeedRecordDTO
        {
            SeedId = item.Id,
            ContentTypeKey = item.ContentType,
            NodeId = nodeId,
            ContentHash = hash,
            Source = source.Name,
            AppliedUtc = DateTime.UtcNow,
        }, ct);

        _logger.Information("Seeded content item '{ContentType}' with seed id '{SeedId}' as node {NodeId}.", item.ContentType, item.Id, nodeId);
    }

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);

    private ContentSeedDocument? Deserialize(ContentSeedSource source)
    {
        try
        {
            return JsonSerializer.Deserialize<ContentSeedDocument>(source.Json, ContentFieldMerger.JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.Warning(ex, "Malformed content seed JSON in source '{Source}'; skipping.", source.Name);
            return null;
        }
    }

    private static void StripIdentityKeys(JsonObject overlay)
    {
        foreach (var key in overlay.Select(p => p.Key).ToArray())
        {
            if (IdentityKeys.Contains(key))
                overlay.Remove(key);
        }
    }

    private static string ComputeHash(ContentSeedItem item)
    {
        var canonical = JsonSerializer.SerializeToNode(item)!.ToJsonString();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
