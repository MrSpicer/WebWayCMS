using WebWayCMS.Attributes;

namespace WebWayCMS.Mcp.Tests;

/// <summary>A view model whose fields declare relations, for <see cref="McpToolHelpers.DescribeFields"/>.</summary>
public sealed class RelatedUpsertViewModel
{
    [ContentRelation("articlelists", Kind = ContentRelationKind.Containment, Required = true)]
    [FormProperty(Label = "Article List", Order = 1)]
    public Guid ArticleListId { get; init; }

    // A relation needs no FormProperty to be a field.
    [ContentRelation("faqs")]
    public List<Guid> RelatedFaqIds { get; init; } = [];

    [ContentRelation("images", Kind = ContentRelationKind.Owned)]
    [FormProperty(Label = "Picture", EditorType = EditorType.Hidden, Order = 3)]
    public Guid? Hidden { get; init; }

    [ContentRelation("images")]
    [FormProperty(Label = "Custom", EditorType = EditorType.Dropdown, Order = 2)]
    public Guid CustomEditor { get; init; }
}
