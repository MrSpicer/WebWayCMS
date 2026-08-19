using System.Text.Json;
using System.Text.Json.Nodes;

using NUnit.Framework;

using WebWayCMS.Content;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class ContentFieldMergerTests
{
    private static JsonElement Json(string raw) => JsonSerializer.Deserialize<JsonElement>(raw);

    private static JsonElement JsonString(string content) => JsonSerializer.SerializeToElement(content);

    [Test]
    public void TryMerge_Object_OverlaysFieldsAndPreservesOthers()
    {
        var baseModel = new FakeModel { Title = "orig", Body = "keep" };

        var merged = (FakeModel)ContentFieldMerger.TryMerge(baseModel, Json("{\"title\":\"new\"}"))!;

        Assert.Multiple(() =>
        {
            Assert.That(merged.Title, Is.EqualTo("new"));
            Assert.That(merged.Body, Is.EqualTo("keep"));
        });
    }

    [Test]
    public void TryMerge_StringEncodedObject_OverlaysFields()
    {
        var baseModel = new FakeModel { Title = "orig", Body = "keep" };

        var merged = (FakeModel)ContentFieldMerger.TryMerge(baseModel, JsonString("{\"title\":\"new\"}"))!;

        Assert.Multiple(() =>
        {
            Assert.That(merged.Title, Is.EqualTo("new"));
            Assert.That(merged.Body, Is.EqualTo("keep"));
        });
    }

    [Test]
    public void TryMerge_NonObject_ReturnsNull()
    {
        Assert.That(ContentFieldMerger.TryMerge(new FakeModel(), Json("123")), Is.Null);
    }

    [Test]
    public void TryMerge_BlankString_ReturnsNull()
    {
        Assert.That(ContentFieldMerger.TryMerge(new FakeModel(), JsonString("   ")), Is.Null);
    }

    [Test]
    public void TryMerge_InvalidJsonString_ReturnsNull()
    {
        Assert.That(ContentFieldMerger.TryMerge(new FakeModel(), JsonString("{not json")), Is.Null);
    }

    [Test]
    public void TryMerge_StringEncodedNonObject_ReturnsNull()
    {
        Assert.That(ContentFieldMerger.TryMerge(new FakeModel(), JsonString("[1,2]")), Is.Null);
    }

    [Test]
    public void TryMerge_StringEncodedNumber_ReturnsNull()
    {
        Assert.That(ContentFieldMerger.TryMerge(new FakeModel(), JsonString("42")), Is.Null);
    }

    [Test]
    public void TryMerge_NullFieldValue_OverwritesWithNull()
    {
        var baseModel = new FakeModel { Id = Guid.NewGuid() };

        var merged = (FakeModel)ContentFieldMerger.TryMerge(baseModel, Json("{\"id\":null}"))!;

        Assert.That(merged.Id, Is.Null);
    }

    [Test]
    public void TryMerge_JsonObjectOverload_OverlaysFields()
    {
        var baseModel = new FakeModel { Title = "orig" };
        var overlay = new JsonObject { ["title"] = "new" };

        var merged = (FakeModel)ContentFieldMerger.TryMerge(baseModel, overlay)!;

        Assert.That(merged.Title, Is.EqualTo("new"));
    }

    [Test]
    public void TryGetObject_Object_ReturnsJsonObject()
    {
        var result = ContentFieldMerger.TryGetObject(Json("{\"a\":1}"));

        Assert.That(result, Is.Not.Null);
        Assert.That(result!["a"]!.GetValue<int>(), Is.EqualTo(1));
    }

    [Test]
    public void TryGetObject_StringEncodedObject_ReturnsJsonObject()
    {
        var result = ContentFieldMerger.TryGetObject(JsonString("{\"a\":1}"));

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public void TryGetObject_BlankString_ReturnsNull()
    {
        Assert.That(ContentFieldMerger.TryGetObject(JsonString("   ")), Is.Null);
    }

    [Test]
    public void TryGetObject_InvalidJsonString_ReturnsNull()
    {
        Assert.That(ContentFieldMerger.TryGetObject(JsonString("{not json")), Is.Null);
    }

    [Test]
    public void TryGetObject_StringEncodedNonObject_ReturnsNull()
    {
        Assert.That(ContentFieldMerger.TryGetObject(JsonString("[1,2]")), Is.Null);
    }

    [Test]
    public void TryGetObject_StringEncodedNumber_ReturnsNull()
    {
        Assert.That(ContentFieldMerger.TryGetObject(JsonString("42")), Is.Null);
    }

    [Test]
    public void TryGetObject_NonObjectElement_ReturnsNull()
    {
        Assert.That(ContentFieldMerger.TryGetObject(Json("123")), Is.Null);
    }

    private sealed class FakeModel
    {
        public string Title { get; init; } = string.Empty;

        public string Body { get; set; } = string.Empty;

        public Guid? Id { get; set; }
    }
}
