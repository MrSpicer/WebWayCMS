using WebWayCMS.Attributes;

namespace WebWayCMS.Mcp.Tests;

/// <summary>A child type placed under "contentblocks" by two containments (for the child tools' relation choice).</summary>
public sealed class ContainedNote
{
    [ContentRelation("contentblocks", Kind = ContentRelationKind.Containment)]
    public Guid BlockId { get; set; }

    [ContentRelation("contentblocks", Kind = ContentRelationKind.Containment)]
    public Guid? OtherBlockId { get; set; }

    public string? Title { get; set; }
}
