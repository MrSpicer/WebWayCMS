using WebWayCMS.Attributes;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Services.Relations;

/// <summary>
/// Maps the declaration-side <see cref="ContentRelationKind"/> (Forms) to the stored
/// <see cref="RelationKind"/> (Data). The two enums share their values by design.
/// </summary>
public static class RelationKindMap
{
    public static RelationKind ToData(this ContentRelationKind kind) => (RelationKind)(int)kind;

    public static ContentRelationKind ToDeclaration(this RelationKind kind) => (ContentRelationKind)(int)kind;
}
