using WebWayCMS.Attributes;

namespace WebWayCMS.Core.Tests;

/// <summary>A child type placed under "ct" by an ordered containment, for the children panel.</summary>
public sealed class FakeChildUpsert
{
    [ContentRelation("ct", Kind = ContentRelationKind.Containment, Ordered = true)]
    public Guid ParentId { get; set; }
}
