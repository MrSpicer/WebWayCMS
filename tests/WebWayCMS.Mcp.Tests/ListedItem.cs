using WebWayCMS.Attributes;

namespace WebWayCMS.Mcp.Tests;

/// <summary>A child type placed under "lists" by one containment.</summary>
public sealed class ListedItem
{
    [ContentRelation("lists", Kind = ContentRelationKind.Containment, Ordered = true)]
    public Guid ListId { get; set; }

    public string? Title { get; set; }
}
