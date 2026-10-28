using System.Reflection;

using WebWayCMS.Attributes;

namespace WebWayCMS.Forms;

/// <summary>A <c>[ContentRelation]</c> property of a model type, as found by <see cref="RelationProperties.Describe"/>.</summary>
public sealed record RelationPropertyInfo(
    PropertyInfo Property,
    ContentRelationAttribute Attribute,
    bool IsMany,
    bool IsOptional)
{
    /// <summary>The relation name: the property name.</summary>
    public string Name => Property.Name;
}
