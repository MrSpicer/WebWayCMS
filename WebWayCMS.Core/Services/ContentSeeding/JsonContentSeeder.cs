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

namespace WebWayCMS.Services.ContentSeeding;

/// <summary>
/// Applies JSON content seed files by reusing the admin CRUD dispatch (handler registry → upsert view
/// model → field overlay → save → publish), so every current and future content type is covered with no
/// per-type code. Identity is tracked through the <see cref="IContentSeedRecordService"/> ledger: an item
/// is only re-applied when its content hash differs from the one recorded last time, so admin edits survive
/// reboots while shipped content changes take effect.
/// </summary>
public sealed class JsonContentSeeder : IJsonContentSeeder
{
    private static readonly IQueryCollection EmptyQuery =
        new QueryCollection(new Dictionary<string, StringValues>());

    private static readonly HashSet<string> IdentityKeys =
        new(StringComparer.OrdinalIgnoreCase) { "nodeId", "expectedVersionNumber" };

    private readonly IEnumerable<IContentSeedSourceProvider> _providers;
    private readonly IAdminHandlerRegistry _registry;
    private readonly IContentSeedRecordService _records;
    private readonly ContentSeedOptions _options;
    private readonly ILogger _logger = Log.ForContext<JsonContentSeeder>();

    public JsonContentSeeder(
        IEnumerable<IContentSeedSourceProvider> providers,
        IAdminHandlerRegistry registry,
        IContentSeedRecordService records,
        IOptions<ContentSeedOptions> options)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
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

                await ApplyItemAsync(item, source.Name, ct);
            }
        }
    }

    private async Task ApplyItemAsync(ContentSeedItem item, string source, CancellationToken ct)
    {
        if (item.Id == Guid.Empty)
        {
            _logger.Warning("Content seed item has no 'id'; skipping.");
            return;
        }

        if (item.Fields.ValueKind == JsonValueKind.Undefined)
        {
            _logger.Warning("Content seed item '{SeedId}' has no 'fields' object; skipping.", item.Id);
            return;
        }

        var hash = ComputeHash(item);
        var record = await _records.GetAsync(item.Id, ct);

        if (record != null && string.Equals(record.ContentHash, hash, StringComparison.Ordinal))
        {
            _logger.Debug("Content seed item '{SeedId}' is unchanged; skipping.", item.Id);
            return;
        }

        var handler = _registry.GetHandler(item.ContentType);
        if (handler == null)
        {
            _logger.Warning("Content seed item '{SeedId}' references unknown content type '{ContentType}'; skipping.", item.Id, item.ContentType);
            return;
        }

        // Only versioned content has a ContentNode identity the ledger can track. Non-versioned
        // types (cmsroutes, formcomponents) return no NodeId on save, so they are skipped up front
        // rather than recorded with a meaningless key.
        if (!handler.SupportsVersionHistory)
        {
            _logger.Warning("Content seed item '{SeedId}' references non-versioned content type '{ContentType}'; skipping.", item.Id, item.ContentType);
            return;
        }

        object? existing = null;
        if (record != null)
            existing = await handler.GetUpsertViewModelAsync(record.NodeId, EmptyQuery, ct);

        var overlay = ContentFieldMerger.TryGetObject(item.Fields);
        if (overlay == null)
        {
            _logger.Warning("Content seed item '{SeedId}' has a non-object 'fields' value; skipping.", item.Id);
            return;
        }

        // The seeder owns identity: strip any id/version keys so a file can never trigger a
        // stale-version failure or hijack a node.
        StripIdentityKeys(overlay);

        var model = ContentFieldMerger.TryMerge(existing ?? handler.CreateEmptyUpsertViewModel(), overlay)!;

        var result = await handler.SaveUpsertAsync(model, ct);
        if (!result.Success)
        {
            // Hash is deliberately NOT recorded, so the item is retried on the next boot.
            _logger.Warning("Content seed item '{SeedId}' failed to save: {ErrorMessage}; will retry next boot.", item.Id, result.ErrorMessage);
            return;
        }

        var nodeId = result.NodeId ?? record?.NodeId ?? Guid.Empty;

        if (nodeId == Guid.Empty)
        {
            _logger.Warning("Content seed item '{SeedId}' saved without a node id; skipping.", item.Id);
            return;
        }

        if (item.Publish && handler.SupportsPublishing)
        {
            var publishResult = await handler.PublishAsync(nodeId, ct);
            if (!publishResult.Success)
            {
                // Hash is deliberately NOT recorded, so the item is retried on the next boot.
                _logger.Warning("Content seed item '{SeedId}' failed to publish: {ErrorMessage}; will retry next boot.", item.Id, publishResult.ErrorMessage);
                return;
            }
        }

        await _records.UpsertAsync(new ContentSeedRecordDTO
        {
            SeedId = item.Id,
            ContentTypeKey = item.ContentType,
            NodeId = nodeId,
            ContentHash = hash,
            Source = source,
            AppliedUtc = DateTime.UtcNow,
        }, ct);

        _logger.Information("Seeded content item '{ContentType}' with seed id '{SeedId}' as node {NodeId}.", item.ContentType, item.Id, nodeId);
    }

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
