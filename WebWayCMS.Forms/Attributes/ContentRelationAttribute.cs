namespace WebWayCMS.Attributes;

/// <summary>
/// Declares a content relation on a <c>Guid</c> / <c>Guid?</c> (single) or <c>IEnumerable&lt;Guid&gt;</c>
/// (many, ordered by position) property of an upsert view model. The relation is stored as rows owned
/// by the source's version, so changing it mints a version and is a draft until publish. The attribute
/// implies an entity picker for <see cref="TargetContentType"/>; a <see cref="FormPropertyAttribute"/>
/// alongside it still sets the label, order and help text.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ContentRelationAttribute : Attribute
{
    private RelationDeletePolicy _onTargetDelete = RelationDeletePolicy.Restrict;

    public ContentRelationAttribute(string targetContentType)
    {
        TargetContentType = targetContentType ?? throw new ArgumentNullException(nameof(targetContentType));
    }

    /// <summary>The target's content type key, e.g. <c>"articlelists"</c>.</summary>
    public string TargetContentType { get; }

    public ContentRelationKind Kind { get; set; } = ContentRelationKind.Reference;

    /// <summary>The save is rejected (on this field) until a target is chosen.</summary>
    public bool Required { get; set; }

    /// <summary>Containment only: the children are kept in an explicit order.</summary>
    public bool Ordered { get; set; }

    /// <summary>Reference only: what deleting the target does to this source. Defaults to <see cref="RelationDeletePolicy.Restrict"/>.</summary>
    public RelationDeletePolicy OnTargetDelete
    {
        get => _onTargetDelete;
        set
        {
            _onTargetDelete = value;
            IsOnTargetDeleteSet = true;
        }
    }

    /// <summary>True when <see cref="OnTargetDelete"/> was set explicitly (it is invalid on a containment).</summary>
    public bool IsOnTargetDeleteSet { get; private set; }
}
