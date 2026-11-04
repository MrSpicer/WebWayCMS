using WebWayCMS.Attributes;

namespace WebWayCMS.Core.Tests;

/// <summary>A child type whose containment under "ct" opts out of the parent's admin.</summary>
public sealed class FakeHiddenChildUpsert
{
    [ContentRelation("ct", Kind = ContentRelationKind.Containment, ShowInParentAdmin = false)]
    public Guid ParentId { get; set; }
}
