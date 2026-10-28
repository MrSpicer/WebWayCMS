namespace WebWayCMS.Data.Models;

/// <summary>
/// One content-to-content link, owned by the source's <see cref="ContentVersion"/> (so a relation change
/// mints a version and is a draft until publish). It is the only place a relation's value lives.
/// </summary>
public record ContentRelation
{
    public const int NameMaxLength = 128;
    public const int QualifierMaxLength = 256;

    public Guid Id { get; set; }

    /// <summary>The source version that owns this relation (Cascade: a version's relations go with it).</summary>
    public Guid SourceVersionId { get; set; }

    /// <summary>The target item: always a node, never a version. No DB FK; the engine enforces integrity.</summary>
    public Guid TargetNodeId { get; set; }

    /// <summary>Containment, Reference or Owned.</summary>
    public RelationKind Kind { get; set; }

    /// <summary>The declaring property, e.g. <c>"ArticleListId"</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Order within (<see cref="SourceVersionId"/>, <see cref="Name"/>).</summary>
    public int Ordinal { get; set; }

    /// <summary><c>""</c> by default; a relation that needs one (e.g. a zone slot) stores it here.</summary>
    public string Qualifier { get; set; } = string.Empty;
}

/// <summary>
/// A relation value a caller wants the next saved version to carry (see
/// <see cref="ContentVersion.RelationWrites"/>).
/// </summary>
public readonly record struct RelationWrite(Guid TargetNodeId, int Ordinal, string Qualifier, RelationKind Kind);
