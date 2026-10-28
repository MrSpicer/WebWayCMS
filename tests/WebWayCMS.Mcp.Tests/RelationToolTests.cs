using ModelContextProtocol;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Controllers.Admin.Handlers;

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

[TestFixture]
public class RelationToolTests
{
    [Test]
    public void DescribeFields_RelationFieldsCarryTheirRelationBlock()
    {
        var fields = McpToolHelpers.DescribeFields(new RelatedUpsertViewModel()).ToDictionary(f => f.Name);

        Assert.Multiple(() =>
        {
            Assert.That(fields.Keys, Is.EquivalentTo(new[] { "articleListId", "relatedFaqIds", "customEditor" }));
            var list = fields["articleListId"];
            Assert.That(list.Relation, Is.EqualTo(new McpRelationDescription("containment", "articlelists", false, true)));
            Assert.That(list.Required, Is.True);
            Assert.That(list.EditorType, Is.EqualTo("Guid"));
            Assert.That(list.Label, Is.EqualTo("Article List"));
            var related = fields["relatedFaqIds"];
            Assert.That(related.Relation, Is.EqualTo(new McpRelationDescription("reference", "faqs", true, false)));
            Assert.That((related.Label, related.EditorType, related.Order, related.HelpText), Is.EqualTo(("RelatedFaqIds", "Guid", 0, (string?)null)));
            Assert.That(fields["customEditor"].EditorType, Is.EqualTo("Dropdown"), "an explicit editor wins");
        });
    }

    [Test]
    public async Task DeleteContent_ReportsDescendants_AndThrowsTheRefusalReason()
    {
        var registry = Substitute.For<IAdminHandlerRegistry>();
        var handler = Substitute.For<IAdminCrudHandler>();
        registry.GetHandler("articlelists").Returns(handler);
        handler.TryDeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((true, (string?)null, 3), (false, (string?)null, 0), (false, (string?)"'Logo' is used by 1 item(s): 'Home'.", 0));
        var tools = new ContentToolset(registry, [handler]);

        var cascaded = await tools.DeleteContent("articlelists", Guid.NewGuid());
        var failed = await tools.DeleteContent("articlelists", Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(cascaded, Is.EqualTo(new McpDeleteResult(true, 3)));
            Assert.That(failed, Is.EqualTo(new McpDeleteResult(false)));
            Assert.That(async () => await tools.DeleteContent("articlelists", Guid.NewGuid()),
                Throws.TypeOf<McpException>().With.Message.EqualTo("'Logo' is used by 1 item(s): 'Home'."));
        });
    }
}
