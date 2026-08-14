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
        => q.Where(e => e.Version.Culture == ctx.Culture
                     && e.Version.Segment == ctx.Segment
                     && !e.Version.Node.IsDeleted
                     && (ctx.Mode == ContentReadMode.Draft
                            ? e.Version.IsCurrentDraft
                            : e.Version.State == ContentVersionState.Published));
}
