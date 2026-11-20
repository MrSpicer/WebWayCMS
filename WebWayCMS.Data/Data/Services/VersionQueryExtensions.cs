using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// The admin-side version filters (the default culture/segment variant, its current drafts, its published
/// versions), in one place, so a query can't drop a condition another copy kept. Read-context selection (what a
/// visitor sees) stays in <see cref="ContentQueryExtensions.AtReadContext{T}"/>. Each overload pair covers a
/// content type's rows and the <see cref="ContentVersion"/> rows themselves.
/// </summary>
public static class VersionQueryExtensions
{
    /// <summary>Versions of the default variant (empty culture and segment).</summary>
    public static IQueryable<T> DefaultVariant<T>(this IQueryable<T> q) where T : class, IVersionedContent
        => q.Where(e => e.Version.Culture == string.Empty && e.Version.Segment == string.Empty);

    /// <inheritdoc cref="DefaultVariant{T}(IQueryable{T})"/>
    public static IQueryable<ContentVersion> DefaultVariant(this IQueryable<ContentVersion> q)
        => q.Where(v => v.Culture == string.Empty && v.Segment == string.Empty);

    /// <summary>The default variant's current draft of every node that is not deleted.</summary>
    public static IQueryable<T> CurrentDrafts<T>(this IQueryable<T> q) where T : class, IVersionedContent
        => q.DefaultVariant().Where(e => e.Version.IsCurrentDraft && !e.Version.Node.IsDeleted);

    /// <inheritdoc cref="CurrentDrafts{T}(IQueryable{T})"/>
    public static IQueryable<ContentVersion> CurrentDrafts(this IQueryable<ContentVersion> q)
        => q.DefaultVariant().Where(v => v.IsCurrentDraft && !v.Node.IsDeleted);

    /// <summary>The default variant's published version of every node that is not deleted.</summary>
    public static IQueryable<T> Published<T>(this IQueryable<T> q) where T : class, IVersionedContent
        => q.DefaultVariant().Where(e => e.Version.State == ContentVersionState.Published && !e.Version.Node.IsDeleted);
}
