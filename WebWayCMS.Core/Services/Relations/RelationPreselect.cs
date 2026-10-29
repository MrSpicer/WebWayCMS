using Microsoft.AspNetCore.Http;

using WebWayCMS.Forms;

namespace WebWayCMS.Services.Relations;

/// <summary>
/// Pre-fills a new item's relations from the create URL: a query key named like a <c>[ContentRelation]</c>
/// property (case-insensitive, e.g. <c>?ArticleListId={id}</c>) sets that property, so a parent's
/// "New {child}" link places the child under it. Values that are not Guids are ignored.
/// </summary>
public static class RelationPreselect
{
    public static void Apply(object model, IQueryCollection query)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(query);

        var declared = RelationProperties.Describe(model.GetType());
        if (declared.Count == 0)
            return;

        var values = RelationProperties.Read(model).ToList();
        var changed = false;
        foreach (var relation in declared)
        {
            var key = query.Keys.FirstOrDefault(k => string.Equals(k, relation.Name, StringComparison.OrdinalIgnoreCase));
            if (key == null)
                continue;

            var targets = query[key]
                .SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(v => Guid.TryParse(v, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            if (targets.Count == 0)
                continue;

            values.RemoveAll(v => v.Name == relation.Name);
            values.AddRange(targets.Select((t, i) => new RelationValue(relation.Name, t, i, string.Empty)));
            changed = true;
        }

        if (changed)
            RelationProperties.Write(model, values);
    }
}
