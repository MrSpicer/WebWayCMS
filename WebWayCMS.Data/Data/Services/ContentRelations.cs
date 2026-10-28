using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <inheritdoc cref="IContentRelations"/>
public sealed class ContentRelations : IContentRelations
{
    private readonly CmsDbContext _context;

    public ContentRelations(CmsDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<RelationReferrer>> GetLiveReferrersAsync(
        Guid targetNodeId, RelationKind? kind = null, CancellationToken ct = default)
    {
        var rows = await LiveVersions()
            .SelectMany(v => v.Relations
                .Where(r => r.TargetNodeId == targetNodeId && (kind == null || r.Kind == kind))
                .Select(r => new
                {
                    v.NodeId,
                    v.Node.ContentTypeKey,
                    v.Title,
                    v.IsCurrentDraft,
                    Published = v.State == ContentVersionState.Published,
                    r.Name,
                    r.Kind
                }))
            .ToListAsync(ct);

        return rows
            .GroupBy(r => (r.NodeId, r.Name))
            .Select(g =>
            {
                // Name the source by its current draft's title when it has one there.
                var named = g.FirstOrDefault(r => r.IsCurrentDraft) ?? g.First();
                return new RelationReferrer(
                    g.Key.NodeId, named.ContentTypeKey, named.Title, g.Key.Name, named.Kind,
                    OnCurrentDraft: g.Any(r => r.IsCurrentDraft),
                    OnPublished: g.Any(r => r.Published));
            })
            .ToList();
    }

    public Task<IReadOnlyList<RelationReferrer>> GetLiveChildrenAsync(Guid parentNodeId, CancellationToken ct = default)
        => GetLiveReferrersAsync(parentNodeId, RelationKind.Containment, ct);

    public async Task<IReadOnlyList<Guid>> GetLiveOwnedTargetsAsync(Guid sourceNodeId, CancellationToken ct = default)
        => await LiveVersions()
            .Where(v => v.NodeId == sourceNodeId)
            .SelectMany(v => v.Relations.Where(r => r.Kind == RelationKind.Owned).Select(r => r.TargetNodeId))
            .Distinct()
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, RelationNode>> GetNodesAsync(IEnumerable<Guid> nodeIds, CancellationToken ct = default)
    {
        var ids = nodeIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, RelationNode>();

        return await _context.Set<ContentVersion>()
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(v => ids.Contains(v.NodeId)
                     && v.Culture == string.Empty
                     && v.Segment == string.Empty
                     && v.IsCurrentDraft
                     && !v.Node.IsDeleted)
            .Select(v => new RelationNode(v.NodeId, v.Node.ContentTypeKey, v.Title))
            .ToDictionaryAsync(n => n.NodeId, ct);
    }

    /// <summary>Every variant's current draft and published version of a node that is not deleted.</summary>
    private IQueryable<ContentVersion> LiveVersions()
        => _context.Set<ContentVersion>()
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(v => (v.IsCurrentDraft || v.State == ContentVersionState.Published) && !v.Node.IsDeleted);
}
