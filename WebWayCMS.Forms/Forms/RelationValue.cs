namespace WebWayCMS.Forms;

/// <summary>
/// One relation value read from (or written to) a model's <c>[ContentRelation]</c> property: the
/// relation <paramref name="Name"/> (the property name), the target node, its position among the
/// property's values and a qualifier (<c>""</c> unless the relation needs one).
/// </summary>
public readonly record struct RelationValue(string Name, Guid TargetNodeId, int Ordinal, string Qualifier);
