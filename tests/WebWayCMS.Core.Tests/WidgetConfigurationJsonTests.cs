using System.Text.Json.Serialization;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Controllers;
using WebWayCMS.Models.ContentZone;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class WidgetConfigurationJsonTests
{
    private IWidgetRegistry _registry = null!;

    public sealed class RichConfig
    {
        [FormProperty(Label = "Body", EditorType = EditorType.RichText)]
        public string Body { get; set; } = string.Empty;

        [FormProperty(Label = "Teaser", EditorType = EditorType.RichText)]
        [JsonPropertyName("teaser_html")]
        public string Teaser { get; set; } = string.Empty;

        [FormProperty(Label = "Heading")]
        public string Heading { get; set; } = string.Empty;
    }

    public sealed class PlainConfig
    {
        [FormProperty(Label = "Heading")]
        public string Heading { get; set; } = string.Empty;
    }

    [SetUp]
    public void SetUp()
    {
        _registry = Substitute.For<IWidgetRegistry>();
        Register("Rich", typeof(RichConfig).FullName);
        Register("Plain", typeof(PlainConfig).FullName);
    }

    private void Register(string name, string? typeName)
        => _registry.GetByName(name).Returns(new WidgetRegistrationInfo { Name = name, ConfigurationTypeName = typeName! });

    private string Sanitize(string? json, string component = "Rich")
        => WidgetConfigurationJson.Sanitize(_registry, component, json);

    // ─── ResolveConfigurationType ─────────────────────────────────────────────

    [Test]
    public void ResolveConfigurationType_NullRegistry_Throws()
    {
        Assert.That(() => WidgetConfigurationJson.ResolveConfigurationType(null!, "Rich"), Throws.ArgumentNullException);
    }

    [Test]
    public void ResolveConfigurationType_ResolvesCoreAndOtherAssemblyTypes()
    {
        Register("Core", typeof(GenericPageConfiguration).FullName);

        Assert.Multiple(() =>
        {
            Assert.That(WidgetConfigurationJson.ResolveConfigurationType(_registry, "Core"), Is.EqualTo(typeof(GenericPageConfiguration)));
            Assert.That(WidgetConfigurationJson.ResolveConfigurationType(_registry, "Rich"), Is.EqualTo(typeof(RichConfig)));
        });
    }

    [Test]
    public void ResolveConfigurationType_UnregisteredBlankOrUnknown_ReturnsNull()
    {
        Register("Blank", " ");
        Register("Missing", "No.Such.Type");

        Assert.Multiple(() =>
        {
            Assert.That(WidgetConfigurationJson.ResolveConfigurationType(_registry, "Nope"), Is.Null);
            Assert.That(WidgetConfigurationJson.ResolveConfigurationType(_registry, "Blank"), Is.Null);
            Assert.That(WidgetConfigurationJson.ResolveConfigurationType(_registry, "Missing"), Is.Null);
        });
    }

    // ─── Sanitize ─────────────────────────────────────────────────────────────

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Sanitize_Empty_ReturnsEmptyObject(string? json)
    {
        Assert.That(Sanitize(json), Is.EqualTo("{}"));
    }

    [Test]
    public void Sanitize_UnresolvableOrNoRichTextType_ReturnsVerbatim()
    {
        const string json = """{"Body": "<script>x</script>"}""";

        Assert.Multiple(() =>
        {
            Assert.That(Sanitize(json, "Unknown"), Is.EqualTo(json));
            Assert.That(Sanitize(json, "Plain"), Is.EqualTo(json));
        });
    }

    [Test]
    public void Sanitize_NothingToClean_ReturnsVerbatim()
    {
        const string json = """{ "Body": "<p>Safe</p>", "Heading": "<b>raw</b>", "Extra": 1 }""";

        Assert.That(Sanitize(json), Is.EqualTo(json));
    }

    [Test]
    public void Sanitize_DirtyRichText_CleansOnlyRichTextAndKeepsOtherKeys()
    {
        var result = Sanitize("""{"body": "<p>Café & co</p><script>alert(1)</script>", "teaser_html": "<img src=x onerror=alert(1)>", "Heading": "<b>h</b>", "Extra": {"keep": true}}""");

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Not.Contain("script"));
            Assert.That(result, Does.Not.Contain("onerror"));
            // Relaxed escaping: markup and non-ASCII stay readable instead of < / é.
            Assert.That(result, Does.Contain("<p>Café &amp; co</p>"));
            Assert.That(result, Does.Contain("\"Heading\":\"<b>h</b>\""));
            Assert.That(result, Does.Contain("\"Extra\":{\"keep\":true}"));
        });
    }

    [Test]
    public void Sanitize_NonStringRichTextValues_AreLeftAlone()
    {
        const string json = """{"Body": 5, "teaser_html": null}""";

        Assert.That(Sanitize(json), Is.EqualTo(json));
    }

    [TestCase("not-json")]
    [TestCase("null")]
    [TestCase("""["<script>x</script>"]""")]
    [TestCase("""{"Body": "<p>a</p>", "Body": "<script>x</script>"}""")]
    public void Sanitize_UnreadablePayload_FailsClosed(string json)
    {
        Assert.That(Sanitize(json), Is.EqualTo("{}"));
    }
}
