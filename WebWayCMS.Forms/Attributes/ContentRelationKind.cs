namespace WebWayCMS.Attributes;

/// <summary>
/// How a <see cref="ContentRelationAttribute"/> relates its source to its target. The Data tier stores
/// the same values on each relation row (<c>WebWayCMS.Data.Models.RelationKind</c>); Core maps between
/// the two, since neither project references the other.
/// </summary>
public enum ContentRelationKind
{
    /// <summary>
    /// Parent–child: the source (the child) is visible only while its target (the parent) is, and is
    /// deleted with it. A child has exactly one parent, so the property must be a single <c>Guid</c>.
    /// </summary>
    Containment = 0,

    /// <summary>A plain link: a target that isn't visible resolves to nothing, and the source still renders.</summary>
    Reference = 1,

    /// <summary>A reference whose target lives and dies with its source (deleted and published with it).</summary>
    Owned = 2
}
