using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// Hides content whose containment parent is not visible in the read context: a child is visible only
/// while every node up its containment chain has a version at that context. Scoped (one per request), so
/// each node's answer is looked up once per request and shared by every <see cref="ContentStore{T}"/>.
/// </summary>
/// <remarks>
/// Draft reads skip the filter (every node has a current draft). Snapshot reads treat a node the
/// snapshot governs as visible (it is pinned to its captured version) and apply the published rule to
/// everything else.
/// </remarks>
public sealed class ContainmentVisibility
{
    private readonly CmsDbContext _context;
    private readonly IContentReadContext _readContext;
    private readonly Dictionary<Guid, bool> _visible = new();

    public ContainmentVisibility(CmsDbContext context, IContentReadContext readContext)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _readContext = readContext ?? throw new ArgumentNullException(nameof(readContext));
    }

    /// <summary><paramref name="rows"/> without the ones whose containment chain is not visible.</summary>
    public async Task<List<T>> FilterVisibleAsync<T>(List<T> rows, CancellationToken ct = default)
        where T : class, IVersionedContent
    {
        if (_readContext.Mode == ContentReadMode.Draft || !rows.Any(r => Parents(r.Version).Any()))
            return rows;

        await ResolveAsync(rows.SelectMany(r => Parents(r.Version)), ct);
        return rows.Where(r => IsGoverned(r.Version.NodeId) || Parents(r.Version).All(p => _visible[p])).ToList();
    }

    /// <summary><paramref name="row"/>, or null when its containment chain is not visible.</summary>
    public async Task<T?> FilterVisibleAsync<T>(T? row, CancellationToken ct = default)
        where T : class, IVersionedContent
    {
        if (row == null) return null;
        var kept = await FilterVisibleAsync([row], ct);
        return kept.Count == 0 ? null : row;
    }

    private static IEnumerable<Guid> Parents(ContentVersion version)
        => version.Relations.Where(r => r.Kind == RelationKind.Containment).Select(r => r.TargetNodeId);

    private bool IsGoverned(Guid nodeId)
        => _readContext.Mode == ContentReadMode.Snapshot
        && _readContext.Snapshot is { } snapshot
        && snapshot.GovernedNodeIds.Contains(nodeId);

    /// <summary>
    /// Fills the memo for <paramref name="nodeIds"/> and every ancestor: one query per level loads the
    /// read-context versions of the nodes not answered yet, then each node is visible when it has one and
    /// all of its own containment parents are visible.
    /// </summary>
    private async Task ResolveAsync(IEnumerable<Guid> nodeIds, CancellationToken ct)
    {
        var parentsOf = new Dictionary<Guid, List<Guid>?>();
        var pending = nodeIds.Where(n => !_visible.ContainsKey(n)).Distinct().ToList();
        while (pending.Count > 0)
        {
            var batch = pending.Where(n => !IsGoverned(n)).ToList();
            var versions = batch.Count == 0
                ? []
                : await AtReadContext(_context.Set<ContentVersion>().AsNoTracking())
                      .Where(v => batch.Contains(v.NodeId))
                      .ToListAsync(ct);

            foreach (var node in pending)
            {
                // A governed node is pinned to its snapshot version, so nothing above it is consulted.
                if (IsGoverned(node))
                {
                    parentsOf[node] = [];
                    continue;
                }
                var version = versions.FirstOrDefault(v => v.NodeId == node);
                parentsOf[node] = version == null ? null : Parents(version).ToList();
            }

            pending = parentsOf.Values
                .Where(p => p != null)
                .SelectMany(p => p!)
                .Where(n => !_visible.ContainsKey(n) && !parentsOf.ContainsKey(n))
                .Distinct()
                .ToList();
        }

        foreach (var node in parentsOf.Keys)
            Visit(node, parentsOf, []);
    }

    /// <summary>Resolves one node depth-first; a containment cycle fails closed (hidden).</summary>
    private bool Visit(Guid node, Dictionary<Guid, List<Guid>?> parentsOf, HashSet<Guid> onPath)
    {
        if (_visible.TryGetValue(node, out var known))
            return known;
        if (!onPath.Add(node))
            return false;

        var parents = parentsOf[node];
        var visible = parents != null && parents.All(p => Visit(p, parentsOf, onPath));
        onPath.Remove(node);
        _visible[node] = visible;
        return visible;
    }

    /// <summary>The version-level form of <see cref="ContentQueryExtensions.AtReadContext{T}"/> (non-governed nodes).</summary>
    private IQueryable<ContentVersion> AtReadContext(IQueryable<ContentVersion> q)
    {
        var ctx = _readContext;
        return q.Where(v => v.Culture == ctx.Culture
                         && v.Segment == ctx.Segment
                         && !v.Node.IsDeleted
                         && v.State == ContentVersionState.Published);
    }
}
