using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

using WebWayCMS.Attributes;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Forms;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Models.Page;

namespace WebWayCMS.Services.Relations;

/// <inheritdoc cref="IContentDeleteService"/>
/// <remarks>
/// Deleters, the handler registry and the relation registry are resolved when a delete runs, not in the
/// constructor: every <c>AdminCrudModel</c> depends on this service and is itself a deleter and a handler,
/// so constructor injection would be a cycle. Only live versions (current draft and published) are
/// consulted; historical rows that point at a deleted node are kept and resolve to nothing.
/// </remarks>
public sealed class ContentDeleteService : IContentDeleteService
{
    private static readonly IQueryCollection EmptyQuery = new QueryCollection(new Dictionary<string, StringValues>());

    private readonly IContentRelations _relations;
    private readonly ICmsTransaction _transaction;
    private readonly IChangeSetScope _changeSetScope;
    private readonly IServiceProvider _services;
    private readonly Serilog.ILogger _logger = Serilog.Log.ForContext<ContentDeleteService>();

    public ContentDeleteService(
        IContentRelations relations,
        ICmsTransaction transaction,
        IChangeSetScope changeSetScope,
        IServiceProvider services)
    {
        _relations = relations ?? throw new ArgumentNullException(nameof(relations));
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        _changeSetScope = changeSetScope ?? throw new ArgumentNullException(nameof(changeSetScope));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public async Task<(bool Success, string? ErrorMessage, int DeletedDescendants)> DeleteAsync(
        Guid nodeId, Func<Guid, CancellationToken, Task<bool>> deleteSelf, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(deleteSelf);

        var (order, descendants) = await CollectAsync(nodeId, ct);
        var deleting = order.ToHashSet();
        var nodes = await _relations.GetNodesAsync(order, ct);

        // References into the set from outside it: Restrict blocks the whole delete, Detach is undone below.
        var registry = _services.GetService<ContentRelationRegistry>();
        var detaches = new List<(RelationReferrer Referrer, Guid Target)>();
        foreach (var target in order)
        {
            var outside = (await _relations.GetLiveReferrersAsync(target, ct: ct))
                .Where(r => r.Kind != RelationKind.Containment && !deleting.Contains(r.SourceNodeId))
                .ToList();
            var blocking = outside
                .Where(r => r.Kind == RelationKind.Owned
                         || (registry?.GetPolicy(r.ContentTypeKey, r.Name) ?? RelationDeletePolicy.Restrict) == RelationDeletePolicy.Restrict)
                .ToList();
            if (blocking.Count > 0)
            {
                var names = string.Join(", ", blocking.Select(r => $"'{r.Title}'").Distinct());
                return (false, $"'{TitleOf(target, nodes)}' is used by {blocking.Count} item(s): {names}.", 0);
            }
            detaches.AddRange(outside.Where(r => r.OnCurrentDraft).Select(r => (r, target)));
        }

        // Nothing goes with it and nothing points at it: the model's own delete, unwrapped (it may run
        // its own transaction).
        if (order.Count == 1 && detaches.Count == 0)
            return (await deleteSelf(nodeId, ct), null, 0);

        var deleters = Deleters();
        foreach (var other in order.Skip(1))
        {
            var key = nodes.GetValueOrDefault(other)?.ContentTypeKey;
            if (key == null || !deleters.ContainsKey(key))
                return (false, $"No deleter registered for '{key ?? other.ToString()}'.", 0);
        }

        string? error = null;
        var deleted = await _transaction.RunAsync(async token =>
        {
            foreach (var (referrer, target) in detaches)
            {
                error = await DetachAsync(referrer, target, TitleOf(target, nodes), token);
                if (error != null) return false;
            }

            // Breadth-first order lists every parent before its children, so reversed it deletes the
            // deepest first; the node itself goes last.
            foreach (var node in Enumerable.Reverse(order))
            {
                var ok = node == nodeId
                    ? await deleteSelf(node, token)
                    : await deleters[nodes[node].ContentTypeKey].DeleteSelfAsync(node, token);
                if (!ok)
                {
                    error = $"'{TitleOf(node, nodes)}' could not be deleted.";
                    return false;
                }
            }
            return true;
        }, ct);

        if (!deleted)
        {
            _logger.Warning("Delete of {NodeId} was rolled back: {Error}", nodeId, error);
            return (false, error, 0);
        }
        return (true, null, descendants);
    }

    /// <summary>
    /// The deletion set in breadth-first order (the node first): its containment descendants and the
    /// owned targets of every node in the set. The count is the containment descendants only.
    /// </summary>
    private async Task<(List<Guid> Order, int Descendants)> CollectAsync(Guid nodeId, CancellationToken ct)
    {
        var order = new List<Guid> { nodeId };
        var seen = new HashSet<Guid> { nodeId };
        var descendants = 0;
        for (var i = 0; i < order.Count; i++)
        {
            foreach (var child in await _relations.GetLiveChildrenAsync(order[i], ct))
            {
                if (seen.Add(child.SourceNodeId))
                {
                    order.Add(child.SourceNodeId);
                    descendants++;
                }
            }
            foreach (var owned in await _relations.GetLiveOwnedTargetsAsync(order[i], ct))
            {
                if (seen.Add(owned))
                    order.Add(owned);
            }
        }
        return (order, descendants);
    }

    /// <summary>Saves a new draft of the referrer without its relation to <paramref name="target"/>.</summary>
    private async Task<string?> DetachAsync(RelationReferrer referrer, Guid target, string targetTitle, CancellationToken ct)
    {
        if (referrer.Name.StartsWith(ContentRelationRegistry.ConfigPrefix, StringComparison.Ordinal))
        {
            using var note = _changeSetScope.UseChangeNote($"Removed reference to {targetTitle}");
            var configSaved = referrer.ContentTypeKey == ContentRelationRegistry.ContentZoneItemContentType
                ? await DetachFromZoneItemAsync(referrer, target, ct)
                : await DetachFromPageConfigurationAsync(referrer, target, ct);
            return configSaved switch
            {
                null => $"'{referrer.Title}' could not be updated to drop its reference to '{targetTitle}'.",
                false => $"'{referrer.Title}' could not be saved.",
                true => null
            };
        }

        var handler = _services.GetService<IAdminHandlerRegistry>()?.GetHandler(referrer.ContentTypeKey);
        var model = handler == null ? null : await handler.GetUpsertViewModelAsync(referrer.SourceNodeId, EmptyQuery, ct);
        if (handler == null || model == null)
            return $"'{referrer.Title}' could not be updated to drop its reference to '{targetTitle}'.";

        var kept = RelationProperties.Read(model)
            .Where(v => !(v.Name == referrer.Name && v.TargetNodeId == target))
            .ToList();
        RelationProperties.Write(model, kept);

        using var _ = _changeSetScope.UseChangeNote($"Removed reference to {targetTitle}");
        var saved = await handler.SaveUpsertAsync(model, ct);
        return saved.Success ? null : saved.ErrorMessage ?? $"'{referrer.Title}' could not be saved.";
    }

    /// <summary>
    /// A widget configuration relation lives on the zone item's version as a <c>config.*</c> row: the
    /// item is saved through the zone service (a new version, recorded in page history) with that
    /// relation written without the target. Null when the item can't be loaded; else whether it saved.
    /// </summary>
    private async Task<bool?> DetachFromZoneItemAsync(RelationReferrer referrer, Guid target, CancellationToken ct)
    {
        var zones = _services.GetService<IContentZoneService>();
        var item = zones == null ? null : await zones.GetItemByNodeIdAsync(referrer.SourceNodeId, ct);
        if (zones == null || item == null)
            return null;

        item.Version.SetRelations(referrer.Name, item.Version.TargetsOf(referrer.Name).Where(t => t != target), referrer.Kind);
        return await zones.UpdateItemAsync(item, ct, item.Version.VersionNumber);
    }

    /// <summary>
    /// A page configuration relation is edited where the page form edits it, in its configuration JSON
    /// (hydrated by the page's upsert view model), and saved through the page's handler.
    /// </summary>
    private async Task<bool?> DetachFromPageConfigurationAsync(RelationReferrer referrer, Guid target, CancellationToken ct)
    {
        var handler = _services.GetService<IAdminHandlerRegistry>()?.GetHandler(referrer.ContentTypeKey);
        var configType = _services.GetService<ContentRelationRegistry>()?.Find(referrer.ContentTypeKey, referrer.Name)?.ModelType;
        if (handler == null || configType == null
            || await handler.GetUpsertViewModelAsync(referrer.SourceNodeId, EmptyQuery, ct) is not PageUpsertViewModel model)
            return null;

        var (stripped, relations) = WidgetConfigurationJson.ExtractRelations(configType, model.ConfigurationJson);
        var kept = relations
            .Where(v => !(v.Name == referrer.Name && v.TargetNodeId == target))
            .Select(v => new ContentRelation { Name = v.Name, TargetNodeId = v.TargetNodeId, Ordinal = v.Ordinal, Kind = referrer.Kind });
        model.ConfigurationJson = WidgetConfigurationJson.HydrateRelations(configType, stripped, kept);
        return (await handler.SaveUpsertAsync(model, ct)).Success;
    }

    /// <summary>Explicitly registered deleters first, then every admin handler that is one.</summary>
    private Dictionary<string, IContentDeleter> Deleters()
    {
        var deleters = new Dictionary<string, IContentDeleter>(StringComparer.OrdinalIgnoreCase);
        var handlers = _services.GetService<IAdminHandlerRegistry>()?.All.OfType<IContentDeleter>() ?? [];
        foreach (var deleter in _services.GetServices<IContentDeleter>().Concat(handlers))
            deleters.TryAdd(deleter.ContentTypeKey, deleter);
        return deleters;
    }

    private static string TitleOf(Guid nodeId, IReadOnlyDictionary<Guid, RelationNode> nodes)
        => nodes.GetValueOrDefault(nodeId)?.Title ?? nodeId.ToString();
}
