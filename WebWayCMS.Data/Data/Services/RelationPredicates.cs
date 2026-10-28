using System.Linq.Expressions;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>EF-translatable predicates and keys over a versioned item's relation rows.</summary>
public static class RelationPredicates
{
    /// <summary>The item's version has relation <paramref name="name"/> to <paramref name="target"/>.</summary>
    public static Expression<Func<T, bool>> RelatesTo<T>(string name, Guid target) where T : class, IVersionedContent
        => e => e.Version.Relations.Any(r => r.Name == name && r.TargetNodeId == target);

    /// <summary>The ordinal of the item's relation <paramref name="name"/> to <paramref name="target"/>, for ordering.</summary>
    public static Expression<Func<T, int>> OrdinalOf<T>(string name, Guid target) where T : class, IVersionedContent
        => e => e.Version.Relations
                 .Where(r => r.Name == name && r.TargetNodeId == target)
                 .Select(r => r.Ordinal)
                 .FirstOrDefault();
}
