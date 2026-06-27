using System.Text.Json;

using Microsoft.AspNetCore.Mvc;

using ModelContextProtocol;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;

namespace WebWayCMS.Mcp.Tests;

[TestFixture]
public class McpToolHelpersTests
{
    private static JsonElement Json(string raw) => JsonSerializer.Deserialize<JsonElement>(raw);

    /// <summary>Produces a JSON <em>string</em> element whose text content is <paramref name="content"/>,
    /// mimicking clients that send the "fields" object encoded as a string.</summary>
    private static JsonElement JsonString(string content) => JsonSerializer.SerializeToElement(content);

    [Test]
    public void ResolveHandler_WhenRegistered_ReturnsHandler()
    {
        var registry = Substitute.For<IAdminHandlerRegistry>();
        var handler = Substitute.For<IAdminCrudHandler>();
        registry.GetHandler("pages").Returns(handler);

        Assert.That(McpToolHelpers.ResolveHandler(registry, "pages"), Is.SameAs(handler));
    }

    [Test]
    public void ResolveHandler_WhenMissing_Throws()
    {
        var registry = Substitute.For<IAdminHandlerRegistry>();
        registry.GetHandler(Arg.Any<string>()).Returns((IAdminCrudHandler?)null);

        Assert.That(() => McpToolHelpers.ResolveHandler(registry, "nope"),
            Throws.TypeOf<McpException>());
    }

    [Test]
    public void ResolveChild_WhenMatching_ReturnsChild()
    {
        var handler = Substitute.For<IAdminCrudHandler>();
        var child = Substitute.For<IAdminCrudChildHandler>();
        child.ChildType.Returns("items");
        handler.ChildHandler.Returns(child);

        Assert.That(McpToolHelpers.ResolveChild(handler, "ITEMS"), Is.SameAs(child));
    }

    [Test]
    public void ResolveChild_WhenNoChildHandler_Throws()
    {
        var handler = Substitute.For<IAdminCrudHandler>();
        handler.ChildHandler.Returns((IAdminCrudChildHandler?)null);

        Assert.That(() => McpToolHelpers.ResolveChild(handler, "items"),
            Throws.TypeOf<McpException>());
    }

    [Test]
    public void ResolveChild_WhenTypeMismatch_Throws()
    {
        var handler = Substitute.For<IAdminCrudHandler>();
        var child = Substitute.For<IAdminCrudChildHandler>();
        child.ChildType.Returns("items");
        handler.ChildHandler.Returns(child);
        handler.ContentType.Returns("contentzones");

        Assert.That(() => McpToolHelpers.ResolveChild(handler, "articles"),
            Throws.TypeOf<McpException>());
    }

    [Test]
    public void Unwrap_ExtractsValueFromJsonAndObjectResults_AndNullOtherwise()
    {
        Assert.Multiple(() =>
        {
            Assert.That(McpToolHelpers.Unwrap(new JsonResult("a")), Is.EqualTo("a"));
            Assert.That(McpToolHelpers.Unwrap(new OkObjectResult("b")), Is.EqualTo("b"));
            Assert.That(McpToolHelpers.Unwrap(new NotFoundResult()), Is.Null);
        });
    }

    [Test]
    public void Merge_OverlaysProvidedFields_AndPreservesOthers()
    {
        var baseModel = new FakeUpsertViewModel { Title = "orig", Body = "keep" };

        var merged = (FakeUpsertViewModel)McpToolHelpers.Merge(baseModel, Json("{\"title\":\"new\"}"));

        Assert.Multiple(() =>
        {
            Assert.That(merged.Title, Is.EqualTo("new"));
            Assert.That(merged.Body, Is.EqualTo("keep"));
        });
    }

    [Test]
    public void Merge_WhenFieldsNotObject_ReturnsBaseValues()
    {
        var baseModel = new FakeUpsertViewModel { Title = "orig" };

        var merged = (FakeUpsertViewModel)McpToolHelpers.Merge(baseModel, Json("123"));

        Assert.That(merged.Title, Is.EqualTo("orig"));
    }

    [Test]
    public void Merge_WhenFieldsAreStringEncodedObject_OverlaysProvidedFields()
    {
        var baseModel = new FakeUpsertViewModel { Title = "orig", Body = "keep" };

        var merged = (FakeUpsertViewModel)McpToolHelpers.Merge(baseModel, JsonString("{\"title\":\"new\"}"));

        Assert.Multiple(() =>
        {
            Assert.That(merged.Title, Is.EqualTo("new"));
            Assert.That(merged.Body, Is.EqualTo("keep"));
        });
    }

    [Test]
    public void Merge_WhenFieldsAreBlankString_ReturnsBaseValues()
    {
        var baseModel = new FakeUpsertViewModel { Title = "orig" };

        var merged = (FakeUpsertViewModel)McpToolHelpers.Merge(baseModel, JsonString("   "));

        Assert.That(merged.Title, Is.EqualTo("orig"));
    }

    [Test]
    public void Merge_WhenFieldsAreInvalidJsonString_ReturnsBaseValues()
    {
        var baseModel = new FakeUpsertViewModel { Title = "orig" };

        var merged = (FakeUpsertViewModel)McpToolHelpers.Merge(baseModel, JsonString("{not json"));

        Assert.That(merged.Title, Is.EqualTo("orig"));
    }

    [Test]
    public void Merge_WhenFieldHasNullValue_OverwritesWithNull()
    {
        var baseModel = new FakeUpsertViewModel { Id = Guid.NewGuid() };

        var merged = (FakeUpsertViewModel)McpToolHelpers.Merge(baseModel, Json("{\"id\":null}"));

        Assert.That(merged.Id, Is.Null);
    }

    [Test]
    public void Merge_WhenFieldsAreStringEncodedNonObject_ReturnsBaseValues()
    {
        var baseModel = new FakeUpsertViewModel { Title = "orig" };

        var merged = (FakeUpsertViewModel)McpToolHelpers.Merge(baseModel, JsonString("[1,2]"));

        Assert.That(merged.Title, Is.EqualTo("orig"));
    }

    [Test]
    public void DescribeFields_ReturnsNonHiddenFormProperties_OrderedByOrder()
    {
        var fields = McpToolHelpers.DescribeFields(new FakeUpsertViewModel());

        // Ignored (no FormProperty) and Id (hidden) are excluded; ordered by Order.
        Assert.That(fields.Select(f => f.Name), Is.EqualTo(new[] { "title", "body", "slug" }));

        var title = fields.Single(f => f.Name == "title");
        var body = fields.Single(f => f.Name == "body");
        var slug = fields.Single(f => f.Name == "slug");

        Assert.Multiple(() =>
        {
            Assert.That(title.Required, Is.True);                 // via FormProperty.IsRequired
            Assert.That(title.HelpText, Is.Null);                 // empty help text -> null
            Assert.That(body.Required, Is.False);
            Assert.That(body.HelpText, Is.EqualTo("The body"));
            Assert.That(body.EditorType, Is.EqualTo("RichText"));
            Assert.That(slug.Required, Is.True);                  // via [Required]
            Assert.That(slug.Label, Is.EqualTo("Slug"));          // empty label -> property name
        });
    }
}