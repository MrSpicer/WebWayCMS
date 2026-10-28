using System.Collections.Concurrent;
using System.Reflection;

using WebWayCMS.Attributes;

namespace WebWayCMS.Forms;

/// <summary>
/// Reflection over a model's <see cref="ContentRelationAttribute"/> properties, cached per type: which
/// properties declare relations, and moving their values to and from neutral <see cref="RelationValue"/>s.
/// </summary>
public static class RelationProperties
{
    /// <summary>
    /// Prefix for relations declared on a configuration type (widget or page config), so they can't
    /// collide with the owning content type's own upsert relations.
    /// </summary>
    public const string ConfigPrefix = "config.";

    private static readonly ConcurrentDictionary<Type, IReadOnlyList<RelationPropertyInfo>> Cache = new();

    /// <summary>
    /// The relation properties of <paramref name="modelType"/>. Throws when one is not a
    /// <c>Guid</c>, <c>Guid?</c> or <c>IEnumerable&lt;Guid&gt;</c> with a public getter and setter.
    /// </summary>
    public static IReadOnlyList<RelationPropertyInfo> Describe(Type modelType)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        return Cache.GetOrAdd(modelType, Build);
    }

    /// <summary>
    /// Every non-empty relation value on <paramref name="model"/>. A many property yields its distinct
    /// targets in list order (the ordinal is the position among them). <paramref name="namePrefix"/> is
    /// prepended to each name, so a configuration type's relations can be stored under <c>config.</c>.
    /// </summary>
    public static IReadOnlyList<RelationValue> Read(object model, string namePrefix = "")
    {
        ArgumentNullException.ThrowIfNull(model);

        var values = new List<RelationValue>();
        foreach (var relation in Describe(model.GetType()))
        {
            var raw = relation.Property.GetValue(model);
            var targets = relation.IsMany
                ? ((IEnumerable<Guid>?)raw ?? []).Where(g => g != Guid.Empty).Distinct()
                : raw is Guid g && g != Guid.Empty ? [g] : Enumerable.Empty<Guid>();

            values.AddRange(targets.Select((target, i) =>
                new RelationValue(namePrefix + relation.Name, target, i, string.Empty)));
        }
        return values;
    }

    /// <summary>
    /// Sets every relation property of <paramref name="model"/> from <paramref name="values"/> (matched by
    /// name with <paramref name="namePrefix"/>, ordered by ordinal). A property with no values is cleared:
    /// <c>Guid.Empty</c>, <c>null</c> or an empty collection.
    /// </summary>
    public static void Write(object model, IEnumerable<RelationValue> values, string namePrefix = "")
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(values);

        var byName = values.ToLookup(v => v.Name, StringComparer.Ordinal);
        foreach (var relation in Describe(model.GetType()))
        {
            var targets = byName[namePrefix + relation.Name].OrderBy(v => v.Ordinal).Select(v => v.TargetNodeId).ToList();
            relation.Property.SetValue(model, ToPropertyValue(relation, targets));
        }
    }

    private static object? ToPropertyValue(RelationPropertyInfo relation, List<Guid> targets)
    {
        if (relation.IsMany)
            return relation.Property.PropertyType.IsArray ? targets.ToArray() : targets;

        if (targets.Count == 0)
            return relation.IsOptional ? null : Guid.Empty;
        return targets[0];
    }

    private static IReadOnlyList<RelationPropertyInfo> Build(Type modelType)
    {
        var relations = new List<RelationPropertyInfo>();
        foreach (var prop in modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attr = prop.GetCustomAttribute<ContentRelationAttribute>();
            if (attr == null)
                continue;

            if (!prop.CanRead || !prop.CanWrite)
                throw Invalid(modelType, prop, "must have a public getter and setter");

            var type = prop.PropertyType;
            if (type == typeof(Guid))
                relations.Add(new RelationPropertyInfo(prop, attr, IsMany: false, IsOptional: false));
            else if (type == typeof(Guid?))
                relations.Add(new RelationPropertyInfo(prop, attr, IsMany: false, IsOptional: true));
            else if (IsGuidCollection(type))
                relations.Add(new RelationPropertyInfo(prop, attr, IsMany: true, IsOptional: true));
            else
                throw Invalid(modelType, prop, "must be a Guid, Guid? or IEnumerable<Guid>");
        }
        return relations;
    }

    /// <summary>A <c>Guid[]</c>, or a type a <c>List&lt;Guid&gt;</c> can be assigned to that reads as <c>IEnumerable&lt;Guid&gt;</c>.</summary>
    private static bool IsGuidCollection(Type type)
        => type == typeof(Guid[])
        || (typeof(IEnumerable<Guid>).IsAssignableFrom(type) && type.IsAssignableFrom(typeof(List<Guid>)));

    private static InvalidOperationException Invalid(Type modelType, PropertyInfo prop, string rule)
        => new($"[ContentRelation] property '{modelType.Name}.{prop.Name}' {rule}.");
}
