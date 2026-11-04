namespace WebWayCMS.Data.Models;

/// <summary>
/// A relation value a caller wants the next saved version to carry (see
/// <see cref="ContentVersion.RelationWrites"/>).
/// </summary>
public readonly record struct RelationWrite(Guid TargetNodeId, int Ordinal, string Qualifier, RelationKind Kind);
