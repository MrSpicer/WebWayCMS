using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// Read-only queries over the zone trees that hang off pages: a page's zones (and the zones nested in
/// their items, level by level), the pages a zone belongs to, and the items a page shows live or in
/// draft. Shared by <see cref="ContentZoneService"/>, <see cref="PageHistoryService"/> and
/// <see cref="PageHistoryRecorder"/> so none of them depends on another.
/// </summary>
public sealed class PageZoneTree
{
    private readonly CmsDbContext _context;

    public PageZoneTree(CmsDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>Items whose current draft is in the default variant (not deleted).</summary>
    public IQueryable<ContentZoneItemDTO> CurrentDraftItems()
        => _context.Set<ContentZoneItemDTO>()
            .AsNoTracking()
            .Where(i => i.Version.IsCurrentDraft
                     && i.Version.Culture == string.Empty
                     && i.Version.Segment == string.Empty
                     && !i.Version.Node.IsDeleted);

    /// <summary>Page rows in the default variant (not deleted): each page's versions, drafts and published alike.</summary>
    public IQueryable<PageDTO> Pages()
        => _context.Set<PageDTO>()
            .AsNoTracking()
            .Where(p => p.Version.Culture == string.Empty
                     && p.Version.Segment == string.Empty
                     && !p.Version.Node.IsDeleted);

    /// <summary>
    /// The zone an item's current draft sits in, or null when the item no longer exists. Resolving from
    /// the draft is safe for every caller: moves are limited to one page's tree, so an item's draft and
    /// published placements always resolve to the same owning page.
    /// </summary>
    public async Task<Guid?> GetItemZoneAsync(Guid itemNodeId, CancellationToken ct = default)
        => await CurrentDraftItems()
            .Where(i => i.Version.NodeId == itemNodeId)
            .Select(i => (Guid?)i.ContentZoneNodeId)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Walks a zone up to its page: zone → assignment → page, or → owning item → that item's zone, and so
    /// on. Returns the page (null for a global, orphaned, or cyclic zone) and every container item passed.
    /// </summary>
    public async Task<(Guid? PageNodeId, HashSet<Guid> OwnerItemNodeIds)> WalkUpAsync(Guid zoneNodeId, CancellationToken ct = default)
    {
        var owners = new HashSet<Guid>();
        var visited = new HashSet<Guid>();
        Guid? currentZoneId = zoneNodeId;

        while (currentZoneId.HasValue)
        {
            if (!visited.Add(currentZoneId.Value))
                return (null, owners);

            var assignment = await _context.Set<ContentZoneAssignmentDTO>()
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.ContentZoneNodeId == currentZoneId.Value, ct);

            if (assignment == null)
                return (null, owners);

            if (assignment.ParentPageNodeId.HasValue)
                return (assignment.ParentPageNodeId.Value, owners);

            if (!assignment.ParentItemNodeId.HasValue)
                return (null, owners);

            owners.Add(assignment.ParentItemNodeId.Value);
            currentZoneId = await GetItemZoneAsync(assignment.ParentItemNodeId.Value, ct);
        }

        return (null, owners);
    }

    /// <summary>
    /// Every page whose zone tree holds the zone — following every assignment, so a zone assigned to
    /// several pages (or nested in an item on several pages) reports each. Empty for a global zone.
    /// </summary>
    public async Task<List<Guid>> GetPagesForZoneAsync(Guid zoneNodeId, CancellationToken ct = default)
    {
        var pages = new HashSet<Guid>();
        var visited = new HashSet<Guid>();
        var frontier = new List<Guid> { zoneNodeId };

        while (frontier.Count > 0)
        {
            var zoneIds = frontier.Where(visited.Add).ToList();
            var assignments = await _context.Set<ContentZoneAssignmentDTO>()
                .AsNoTracking()
                .Where(a => zoneIds.Contains(a.ContentZoneNodeId))
                .Select(a => new { a.ParentPageNodeId, a.ParentItemNodeId })
                .ToListAsync(ct);

            pages.UnionWith(assignments.Where(a => a.ParentPageNodeId.HasValue).Select(a => a.ParentPageNodeId!.Value));

            var ownerItemIds = assignments.Where(a => a.ParentItemNodeId.HasValue).Select(a => a.ParentItemNodeId!.Value).ToList();
            frontier = await CurrentDraftItems()
                .Where(i => ownerItemIds.Contains(i.Version.NodeId))
                .Select(i => i.ContentZoneNodeId)
                .Distinct()
                .ToListAsync(ct);
        }

        return pages.ToList();
    }

    /// <summary>Every zone in the page's tree (any version of an item counts as holding a nested zone).</summary>
    public async Task<List<Guid>> GetZoneTreeNodeIdsAsync(Guid pageNodeId, CancellationToken ct = default)
        => (await WalkZoneTreesAsync([pageNodeId], ct)).Select(x => x.ZoneNodeId).Distinct().ToList();

    /// <summary>
    /// Every zone under each of the given pages, as (zone, page) pairs in breadth-first order: the pages'
    /// directly assigned zones, then the zones nested in their items, level by level. Only the current
    /// frontier is queried, so the cost follows the size of these pages' trees, not the whole zone table.
    /// Items are matched on any version, so a moved item's draft and published zones both count. The
    /// visited set guards against cycles.
    /// </summary>
    public async Task<List<(Guid ZoneNodeId, Guid PageNodeId)>> WalkZoneTreesAsync(
        IReadOnlyCollection<Guid> pageNodeIds, CancellationToken ct = default)
    {
        var result = new List<(Guid ZoneNodeId, Guid PageNodeId)>();
        var visited = new HashSet<(Guid, Guid)>();

        var frontier = (await _context.Set<ContentZoneAssignmentDTO>()
                .AsNoTracking()
                .Where(a => a.ParentPageNodeId != null && pageNodeIds.Contains(a.ParentPageNodeId.Value))
                .Select(a => new { a.ContentZoneNodeId, PageNodeId = a.ParentPageNodeId!.Value })
                .ToListAsync(ct))
            .Select(x => (x.ContentZoneNodeId, x.PageNodeId))
            .ToList();

        while (frontier.Count > 0)
        {
            var added = frontier.Where(visited.Add).ToList();
            result.AddRange(added);

            var zoneIds = added.Select(x => x.ContentZoneNodeId).Distinct().ToList();
            var itemZones = await _context.Set<ContentZoneItemDTO>()
                .AsNoTracking()
                .Where(i => zoneIds.Contains(i.ContentZoneNodeId))
                .Select(i => new { i.Version.NodeId, i.ContentZoneNodeId })
                .Distinct()
                .ToListAsync(ct);
            var itemIds = itemZones.Select(x => x.NodeId).Distinct().ToList();
            if (itemIds.Count == 0)
                break;

            var nested = await _context.Set<ContentZoneAssignmentDTO>()
                .AsNoTracking()
                .Where(a => a.ParentItemNodeId != null && itemIds.Contains(a.ParentItemNodeId.Value))
                .Select(a => new { a.ContentZoneNodeId, ItemNodeId = a.ParentItemNodeId!.Value })
                .ToListAsync(ct);

            // A nested zone belongs to every page whose tree holds an item owning it.
            frontier = (from n in nested
                        join iz in itemZones on n.ItemNodeId equals iz.NodeId
                        join z in added on iz.ContentZoneNodeId equals z.ContentZoneNodeId
                        select (n.ContentZoneNodeId, z.PageNodeId))
                .Distinct()
                .ToList();
        }

        return result;
    }

    /// <summary>The active, published items the page shows live (see <see cref="WalkActiveItemsAsync"/>).</summary>
    public Task<List<ContentZoneItemDTO>> GetLiveTreeItemsAsync(Guid pageNodeId, CancellationToken ct = default)
        => WalkActiveItemsAsync(pageNodeId, i => i.Version.State == ContentVersionState.Published, ct);

    /// <summary>The active current drafts the page shows in its editor (see <see cref="WalkActiveItemsAsync"/>).</summary>
    public Task<List<ContentZoneItemDTO>> GetDraftTreeItemsAsync(Guid pageNodeId, CancellationToken ct = default)
        => WalkActiveItemsAsync(pageNodeId, i => i.Version.IsCurrentDraft, ct);

    /// <summary>
    /// The active items (at the version <paramref name="version"/> selects) in the page's zones, then in
    /// the zones those items own, level by level. An inactive (removed) container is not rendered, so the
    /// items nested in it are not part of what the page shows.
    /// </summary>
    private async Task<List<ContentZoneItemDTO>> WalkActiveItemsAsync(
        Guid pageNodeId, Expression<Func<ContentZoneItemDTO, bool>> version, CancellationToken ct)
    {
        var items = new List<ContentZoneItemDTO>();
        var visited = new HashSet<Guid>();

        var frontier = await _context.Set<ContentZoneAssignmentDTO>()
            .AsNoTracking()
            .Where(a => a.ParentPageNodeId == pageNodeId)
            .Select(a => a.ContentZoneNodeId)
            .ToListAsync(ct);

        while (frontier.Count > 0)
        {
            var zoneIds = frontier.Where(visited.Add).ToList();
            var level = await _context.Set<ContentZoneItemDTO>()
                .AsNoTracking()
                .Where(version)
                .Where(i => zoneIds.Contains(i.ContentZoneNodeId)
                         && i.IsActive
                         && i.Version.Culture == string.Empty
                         && i.Version.Segment == string.Empty
                         && !i.Version.Node.IsDeleted)
                .ToListAsync(ct);
            items.AddRange(level);

            var ownerIds = level.Select(i => i.Version.NodeId).ToList();
            frontier = await _context.Set<ContentZoneAssignmentDTO>()
                .AsNoTracking()
                .Where(a => a.ParentItemNodeId != null && ownerIds.Contains(a.ParentItemNodeId.Value))
                .Select(a => a.ContentZoneNodeId)
                .ToListAsync(ct);
            frontier = frontier.Where(id => !visited.Contains(id)).ToList();
        }

        return items;
    }

    /// <summary>
    /// The pages (among <paramref name="pageNodeIds"/>) whose zone tree differs from what is live. A
    /// current draft counts only when its content or placement differs from the item's published version
    /// — a draft equal to it (left by a discard or a revert) does not — and an inactive item that never
    /// went live is not a change either.
    /// </summary>
    public async Task<HashSet<Guid>> GetPagesWithPendingZoneChangesAsync(IEnumerable<Guid> pageNodeIds, CancellationToken ct = default)
    {
        var pageIds = pageNodeIds.ToList();
        if (pageIds.Count == 0)
            return [];

        var tree = await WalkZoneTreesAsync(pageIds, ct);
        var zoneIds = tree.Select(x => x.ZoneNodeId).Distinct().ToList();

        var drafts = await CurrentDraftItems()
            .Where(i => zoneIds.Contains(i.ContentZoneNodeId) && i.Version.State != ContentVersionState.Published)
            .ToListAsync(ct);
        if (drafts.Count == 0)
            return [];

        var nodeIds = drafts.Select(i => i.Version.NodeId).ToList();
        var published = (await _context.Set<ContentZoneItemDTO>()
                .AsNoTracking()
                .Where(i => nodeIds.Contains(i.Version.NodeId)
                         && i.Version.State == ContentVersionState.Published
                         && i.Version.Culture == string.Empty
                         && i.Version.Segment == string.Empty)
                .ToListAsync(ct))
            .ToDictionary(i => i.Version.NodeId);

        var pendingZones = drafts
            .Where(d => published.TryGetValue(d.Version.NodeId, out var live)
                ? !PageSetEquality.ItemsEqual(live, d)
                : d.IsActive)
            .Select(d => d.ContentZoneNodeId)
            .ToHashSet();

        return tree.Where(x => pendingZones.Contains(x.ZoneNodeId)).Select(x => x.PageNodeId).ToHashSet();
    }
}
