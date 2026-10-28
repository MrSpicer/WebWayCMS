namespace WebWayCMS.Data.Models;

/// <summary>
/// The kind of a stored <see cref="ContentRelation"/>. Stored on the row (rather than read from the
/// declaration) so containment visibility and the delete cascade work on a rendering-only host, which
/// has no declarations. Mirrors <c>WebWayCMS.Attributes.ContentRelationKind</c>.
/// </summary>
public enum RelationKind
{
    /// <summary>Parent–child: the source is visible only while its target is, and is deleted with it.</summary>
    Containment = 0,

    /// <summary>A plain link; a target that isn't visible resolves to nothing.</summary>
    Reference = 1,

    /// <summary>A reference whose target lives and dies with its source.</summary>
    Owned = 2
}
