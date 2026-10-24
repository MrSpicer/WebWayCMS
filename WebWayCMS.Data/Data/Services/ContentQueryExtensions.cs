using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// The single place version selection happens, replacing the hand-copied anti-joins. Both branches
/// are single-row index seeks against the filtered unique indexes on ContentVersion.
/// </summary>
public static class ContentQueryExtensions
{
    public static IQueryable<T> AtReadContext<T>(this IQueryable<T> q, IContentReadContext ctx)
        where T : class, IVersionedContent
    {
        // A snapshot read pins the nodes it governs to the captured versions and reads everything else
        // as published. A snapshot mode without a snapshot falls through to the published branch.
        if (ctx.Mode == ContentReadMode.Snapshot && ctx.Snapshot is { } snapshot)
        {
            var governed = snapshot.GovernedNodeIds;
            var versions = snapshot.VersionIds;
            return q.Where(e => e.Version.Culture == ctx.Culture
                             && e.Version.Segment == ctx.Segment
                             && !e.Version.Node.IsDeleted
                             && (governed.Contains(e.Version.NodeId)
                                    ? versions.Contains(e.VersionId)
                                    : e.Version.State == ContentVersionState.Published));
        }

        return q.Where(e => e.Version.Culture == ctx.Culture
                         && e.Version.Segment == ctx.Segment
                         && !e.Version.Node.IsDeleted
                         && (ctx.Mode == ContentReadMode.Draft
                                ? e.Version.IsCurrentDraft
                                : e.Version.State == ContentVersionState.Published));
    }
}
