using System.Text.Json;
using System.Text.Json.Serialization;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Data.Models;
using WebWayCMS.Forms;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class WidgetConfigurationJsonTests
{
    private sealed class SingleConfig
    {
        [ContentRelation("images")]
        [JsonPropertyName("image")]
        public Guid? ImageId { get; set; }

        [FormProperty(EditorType = EditorType.Text)]
        public string? Other { get; set; }
    }

    private sealed class ManyConfig
    {
        [ContentRelation("faqs")]
        public List<Guid> FaqIds { get; set; } = [];
    }

    private sealed class NoRelations
    {
        public string? Value { get; set; }
    }

    private sealed class RichTextConfig
    {
        [FormProperty(EditorType = EditorType.RichText)]
        public string Body { get; set; } = string.Empty;
    }

    private static IWidgetRegistry Registry(string name, Type type)
    {
        var registry = Substitute.For<IWidgetRegistry>();
        registry.GetByName(name).Returns(new WidgetRegistrationInfo
        {
            Name = name,
            ConfigurationTypeName = type.AssemblyQualifiedName
        });
        return registry;
    }

    [Test]
    public void ExtractRelations_NullType_ReturnsJsonUnchanged()
    {
        var (json, relations) = WidgetConfigurationJson.ExtractRelations(null, "{\"a\":1}");

        Assert.Multiple(() =>
        {
            Assert.That(json, Is.EqualTo("{\"a\":1}"));
            Assert.That(relations, Is.Empty);
        });
    }

    [Test]
    public void ExtractRelations_TypeWithoutRelations_ReturnsJsonUnchanged()
    {
        var (json, relations) = WidgetConfigurationJson.ExtractRelations(typeof(NoRelations), "{\"value\":\"x\"}");

        Assert.Multiple(() =>
        {
            Assert.That(json, Is.EqualTo("{\"value\":\"x\"}"));
            Assert.That(relations, Is.Empty);
        });
    }

    [Test]
    public void ExtractRelations_BlankJson_ReturnsEmptyObject()
    {
        var (json, relations) = WidgetConfigurationJson.ExtractRelations(typeof(SingleConfig), "   ");

        Assert.Multiple(() =>
        {
            Assert.That(json, Is.EqualTo("{}"));
            Assert.That(relations, Is.Empty);
        });
    }

    [Test]
    public void ExtractRelations_NonObjectJson_FailsClosed()
    {
        var (json, relations) = WidgetConfigurationJson.ExtractRelations(typeof(SingleConfig), "[]");

        Assert.Multiple(() =>
        {
            Assert.That(json, Is.EqualTo("{}"));
            Assert.That(relations, Is.Empty);
        });
    }

    [Test]
    public void ExtractRelations_DuplicateKeys_FailsClosed()
    {
        var (json, relations) = WidgetConfigurationJson.ExtractRelations(
            typeof(SingleConfig), "{\"image\":\"" + Guid.NewGuid() + "\",\"image\":\"" + Guid.NewGuid() + "\"}");

        Assert.Multiple(() =>
        {
            Assert.That(json, Is.EqualTo("{}"));
            Assert.That(relations, Is.Empty);
        });
    }

    [Test]
    public void ExtractRelations_SingleValue_LiftsAndRemovesIt()
    {
        var id = Guid.NewGuid();
        var (json, relations) = WidgetConfigurationJson.ExtractRelations(
            typeof(SingleConfig), $"{{\"image\":\"{id}\",\"other\":\"kept\"}}");

        Assert.Multiple(() =>
        {
            Assert.That(relations, Has.Count.EqualTo(1));
            Assert.That(relations[0].Name, Is.EqualTo("config.ImageId"));
            Assert.That(relations[0].TargetNodeId, Is.EqualTo(id));
            Assert.That(json, Does.Not.Contain("image"));
            Assert.That(json, Does.Contain("kept"));
        });
    }

    [Test]
    public void ExtractRelations_InvalidSingleValue_DropsItButRemovesTheKey()
    {
        var (json, relations) = WidgetConfigurationJson.ExtractRelations(
            typeof(SingleConfig), "{\"image\":\"not-a-guid\",\"other\":\"kept\"}");

        Assert.Multiple(() =>
        {
            Assert.That(relations, Is.Empty);
            Assert.That(json, Does.Not.Contain("image"));
        });
    }

    [Test]
    public void ExtractRelations_ManyValues_SkipsInvalidEmptyAndDuplicates()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new
        {
            FaqIds = new object[]
            {
                first, second, first, string.Empty, 42, "not-a-guid", Guid.Empty
            }
        });

        var (stripped, relations) = WidgetConfigurationJson.ExtractRelations(typeof(ManyConfig), json);

        Assert.Multiple(() =>
        {
            Assert.That(relations.Select(r => r.Name), Is.All.EqualTo("config.FaqIds"));
            Assert.That(relations.Select(r => r.TargetNodeId), Is.EqualTo(new[] { first, second }));
            Assert.That(relations.Select(r => r.Ordinal), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(stripped, Does.Not.Contain("FaqIds"));
        });
    }

    [Test]
    public void ExtractRelations_ManyValueNotAnArray_IsEmpty()
    {
        var (json, relations) = WidgetConfigurationJson.ExtractRelations(
            typeof(ManyConfig), $"{{\"FaqIds\":\"{Guid.NewGuid()}\"}}");

        Assert.Multiple(() =>
        {
            Assert.That(relations, Is.Empty);
            Assert.That(json, Does.Not.Contain("FaqIds"));
        });
    }

    [Test]
    public void ExtractRelations_NoRelationKeys_ReturnsOriginalJson()
    {
        var original = "{ \"other\" : \"kept\" }";
        var (json, relations) = WidgetConfigurationJson.ExtractRelations(typeof(SingleConfig), original);

        Assert.Multiple(() =>
        {
            Assert.That(json, Is.EqualTo(original));
            Assert.That(relations, Is.Empty);
        });
    }

    [Test]
    public void ExtractRelations_RegistryOverload_ResolvesTheComponentType()
    {
        var registry = Substitute.For<IWidgetRegistry>();
        registry.GetByName("Widget").Returns(new WidgetRegistrationInfo
        {
            Name = "Widget",
            ConfigurationTypeName = typeof(SingleConfig).AssemblyQualifiedName
        });

        var id = Guid.NewGuid();
        var (json, relations) = WidgetConfigurationJson.ExtractRelations(
            registry, "Widget", $"{{\"image\":\"{id}\"}}");

        Assert.Multiple(() =>
        {
            Assert.That(relations.Single().TargetNodeId, Is.EqualTo(id));
            Assert.That(json, Is.EqualTo("{}"));
            Assert.That(() => WidgetConfigurationJson.ExtractRelations((IWidgetRegistry)null!, "Widget", "{}"),
                Throws.ArgumentNullException);
        });
    }

    [Test]
    public void HydrateRelations_NullTypeOrNoRelations_ReturnsJson()
    {
        var relations = Array.Empty<ContentRelation>();

        Assert.Multiple(() =>
        {
            Assert.That(WidgetConfigurationJson.HydrateRelations(null, "{\"a\":1}", relations), Is.EqualTo("{\"a\":1}"));
            Assert.That(WidgetConfigurationJson.HydrateRelations(typeof(NoRelations), "{\"a\":1}", relations), Is.EqualTo("{\"a\":1}"));
            Assert.That(() => WidgetConfigurationJson.HydrateRelations(null, "{}", null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void HydrateRelations_InvalidJsonOrNonObject_FailsClosed()
    {
        var relations = Array.Empty<ContentRelation>();

        Assert.Multiple(() =>
        {
            Assert.That(WidgetConfigurationJson.HydrateRelations(typeof(SingleConfig), "{", relations), Is.EqualTo("{}"));
            Assert.That(WidgetConfigurationJson.HydrateRelations(typeof(SingleConfig), "[]", relations), Is.EqualTo("{}"));
        });
    }

    [Test]
    public void HydrateRelations_SingleAndManyAreWrittenInOrder()
    {
        var image = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var relations = new[]
        {
            new ContentRelation { Name = "config.ImageId", TargetNodeId = image, Ordinal = 0 },
            new ContentRelation { Name = "config.FaqIds", TargetNodeId = second, Ordinal = 1 },
            new ContentRelation { Name = "config.FaqIds", TargetNodeId = first, Ordinal = 0 }
        };

        var single = WidgetConfigurationJson.HydrateRelations(typeof(SingleConfig), "{}", relations);
        var many = WidgetConfigurationJson.HydrateRelations(typeof(ManyConfig), "{}", relations);

        using var singleJson = JsonDocument.Parse(single);
        using var manyJson = JsonDocument.Parse(many);
        Assert.Multiple(() =>
        {
            Assert.That(singleJson.RootElement.GetProperty("image").GetString(), Is.EqualTo(image.ToString()));
            Assert.That(manyJson.RootElement.GetProperty("FaqIds").EnumerateArray().Select(e => Guid.Parse(e.GetString()!)),
                Is.EqualTo(new[] { first, second }));
        });
    }

    [Test]
    public void HydrateRelations_BlankJsonStartsAnObject_AndNoTargetsRemovesTheKey()
    {
        var empty = WidgetConfigurationJson.HydrateRelations(typeof(SingleConfig), null, []);
        var removed = WidgetConfigurationJson.HydrateRelations(
            typeof(SingleConfig), $"{{\"image\":\"{Guid.NewGuid()}\"}}", []);

        Assert.Multiple(() =>
        {
            Assert.That(empty, Is.EqualTo("{}"));
            Assert.That(removed, Does.Not.Contain("image"));
        });
    }

    [Test]
    public void ExtractRelations_NoRelationsAndNullJson_ReturnsEmptyObject()
    {
        var (json, relations) = WidgetConfigurationJson.ExtractRelations(typeof(NoRelations), null);

        Assert.Multiple(() =>
        {
            Assert.That(json, Is.EqualTo("{}"));
            Assert.That(relations, Is.Empty);
        });
    }

    [Test]
    public void HydrateRelations_NullJsonUsesTheFallbacks()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WidgetConfigurationJson.HydrateRelations(null, null, []), Is.EqualTo("{}"));
            Assert.That(WidgetConfigurationJson.HydrateRelations(typeof(NoRelations), null, []), Is.EqualTo("{}"));
        });
    }

    [Test]
    public void Sanitize_NonObjectJson_FailsClosed()
    {
        var registry = Registry("Rich", typeof(RichTextConfig));

        Assert.That(WidgetConfigurationJson.Sanitize(registry, "Rich", "[]"), Is.EqualTo("{}"));
    }

    [Test]
    public void Sanitize_UnchangedAndNonRichTextValues_ReturnTheOriginal()
    {
        var registry = Registry("Rich", typeof(RichTextConfig));

        var unchanged = WidgetConfigurationJson.Sanitize(registry, "Rich", "{\"Body\":\"<p>Safe</p>\"}");
        var nonRich = WidgetConfigurationJson.Sanitize(registry, "Rich", "{\"Other\":1,\"Body\":\"<p>Safe</p>\"}");

        Assert.Multiple(() =>
        {
            Assert.That(unchanged, Is.EqualTo("{\"Body\":\"<p>Safe</p>\"}"));
            Assert.That(nonRich, Is.EqualTo("{\"Other\":1,\"Body\":\"<p>Safe</p>\"}"));
        });
    }

    [Test]
    public void Sanitize_DuplicateKeys_FailsClosed()
    {
        var registry = Registry("Rich", typeof(RichTextConfig));

        Assert.That(
            WidgetConfigurationJson.Sanitize(registry, "Rich", "{\"Body\":\"<p>a</p>\",\"Body\":\"<p>b</p>\"}"),
            Is.EqualTo("{}"));
    }

    [Test]
    public void ApplyRelations_WritesEveryDeclaredName()
    {
        var image = Guid.NewGuid();
        var faq = Guid.NewGuid();
        var version = new ContentVersion { Id = Guid.NewGuid() };

        WidgetConfigurationJson.ApplyRelations(version, typeof(SingleConfig),
            [new RelationValue("config.ImageId", image, 0, string.Empty)]);
        WidgetConfigurationJson.ApplyRelations(version, typeof(ManyConfig),
            [new RelationValue("config.FaqIds", faq, 0, string.Empty)]);

        Assert.Multiple(() =>
        {
            Assert.That(version.RelationWrites["config.ImageId"].Single().TargetNodeId, Is.EqualTo(image));
            Assert.That(version.RelationWrites["config.FaqIds"].Single().TargetNodeId, Is.EqualTo(faq));
            Assert.That(version.RelationWrites["config.ImageId"].Single().Kind, Is.EqualTo(RelationKind.Reference));
            Assert.That(() => WidgetConfigurationJson.ApplyRelations(null!, typeof(SingleConfig), []),
                Throws.ArgumentNullException);
            Assert.That(() => WidgetConfigurationJson.ApplyRelations(version, typeof(SingleConfig), null!),
                Throws.ArgumentNullException);
        });
    }

    [Test]
    public void ApplyRelations_NullType_IsANoOp()
    {
        var version = new ContentVersion { Id = Guid.NewGuid() };
        WidgetConfigurationJson.ApplyRelations(version, null, [new RelationValue("x", Guid.NewGuid(), 0, string.Empty)]);
        Assert.That(version.RelationWrites, Is.Empty);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void Validate_EmptyPayload_IsValidatedAsAnEmptyObject(string? json)
    {
        var registry = Substitute.For<IWidgetRegistry>();
        registry.ValidateConfiguration("W", "{}", WidgetDisplayMode.List).Returns(new[] { "Heading is required." });

        Assert.That(WidgetConfigurationJson.Validate(registry, "W", json, WidgetDisplayMode.List),
            Is.EqualTo(new[] { "Heading is required." }));
    }

    [Test]
    public void Validate_Payload_IsPassedThroughWithTheDisplayMode()
    {
        var registry = Substitute.For<IWidgetRegistry>();
        registry.ValidateConfiguration("W", "{\"a\":1}", WidgetDisplayMode.Single).Returns(new[] { "bad" });

        Assert.That(WidgetConfigurationJson.Validate(registry, "W", "{\"a\":1}", WidgetDisplayMode.Single),
            Is.EqualTo(new[] { "bad" }));
    }
}
