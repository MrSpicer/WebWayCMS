using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Models.ContentZone;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class WidgetMetadataJsonTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not json")]
    [TestCase("null")]
    public void Parse_MissingOrMalformed_ReturnsEmptyMetadata(string? json)
    {
        var metadata = WidgetMetadataJson.Parse(json);

        Assert.Multiple(() =>
        {
            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata.AdminLabel, Is.Null);
            Assert.That(metadata.AnchorId, Is.Null);
        });
    }

    [Test]
    public void Parse_Valid_IsCaseInsensitive()
    {
        var metadata = WidgetMetadataJson.Parse("{\"adminLabel\":\"Hero\",\"AnchorId\":\"intro\"}");

        Assert.Multiple(() =>
        {
            Assert.That(metadata.AdminLabel, Is.EqualTo("Hero"));
            Assert.That(metadata.AnchorId, Is.EqualTo("intro"));
        });
    }

    [Test]
    public void Normalize_Valid_ReturnsCanonicalJsonWithoutNulls()
    {
        var error = WidgetMetadataJson.Normalize(
            "{\"cssClass\":\"a b\",\"anchorId\":\"intro\",\"adminNote\":null,\"unknown\":1}", null, out var normalized);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(normalized, Is.EqualTo("{\"DisplayMode\":\"Single\",\"CssClass\":\"a b\",\"AnchorId\":\"intro\"}"));
        });
    }

    [TestCase("not json")]
    [TestCase("[1,2]")]
    [TestCase("null")]
    public void Normalize_NotAnObject_Fails(string json)
    {
        var error = WidgetMetadataJson.Normalize(json, null, out var normalized);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Not.Null);
            Assert.That(error!.Success, Is.False);
            Assert.That(error.ErrorMessage, Is.EqualTo("Metadata must be a JSON object."));
            Assert.That(error.ErrorField, Is.EqualTo("MetadataJson"));
            Assert.That(normalized, Is.Null);
        });
    }

    [TestCase("{\"anchorId\":\"a onmouseover=alert(1)\"}", "AnchorId")]
    [TestCase("{\"cssClass\":\"x\\\" onclick=\\\"y\"}", "CssClass")]
    public void Normalize_FieldFailsValidation_Fails(string json, string field)
    {
        var error = WidgetMetadataJson.Normalize(json, null, out var normalized);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Not.Null);
            Assert.That(error!.ErrorField, Is.EqualTo(field));
            Assert.That(normalized, Is.Null);
        });
    }

    [Test]
    public void Normalize_TooLong_Fails()
    {
        // Every field is individually capped, so the public cap cannot be hit with today's fields;
        // the backstop is exercised through the internal overload with a smaller limit.
        var json = "{\"adminNote\":\"" + new string('x', 100) + "\"}";

        var error = WidgetMetadataJson.Normalize(json, null, 50, out var normalized);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Not.Null);
            Assert.That(error!.ErrorMessage, Does.Contain("at most 50"));
            Assert.That(error.ErrorField, Is.EqualTo("MetadataJson"));
            Assert.That(normalized, Is.Null);
        });
    }

    [Test]
    public void Parse_DisplayMode_ReadsNameOrNumber_DefaultsToSingle()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WidgetMetadataJson.Parse("{}").DisplayMode, Is.EqualTo(WidgetDisplayMode.Single));
            Assert.That(WidgetMetadataJson.Parse("{\"displayMode\":\"List\"}").DisplayMode, Is.EqualTo(WidgetDisplayMode.List));
            Assert.That(WidgetMetadataJson.Parse("{\"DisplayMode\":2}").DisplayMode, Is.EqualTo(WidgetDisplayMode.List));
        });
    }

    [Test]
    public void Normalize_List_WidgetSupportsList_Succeeds()
    {
        var component = new WidgetRegistrationInfo { Name = "Faq", SupportsList = true };

        var error = WidgetMetadataJson.Normalize("{\"displayMode\":\"List\"}", component, out var normalized);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(normalized, Is.EqualTo("{\"DisplayMode\":\"List\"}"));
        });
    }

    [Test]
    public void Normalize_List_WidgetWithoutListSupport_Fails()
    {
        var component = new WidgetRegistrationInfo { Name = "Layout", SupportsList = false };

        var error = WidgetMetadataJson.Normalize("{\"displayMode\":\"List\"}", component, out var normalized);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Not.Null);
            Assert.That(error!.ErrorMessage, Is.EqualTo("Widget 'Layout' does not support list mode."));
            Assert.That(error.ErrorField, Is.EqualTo("DisplayMode"));
            Assert.That(normalized, Is.Null);
        });
    }

    [Test]
    public void Normalize_List_WidgetWithoutListSupport_ListAlreadyStored_Succeeds()
    {
        var component = new WidgetRegistrationInfo { Name = "Layout", SupportsList = false };

        var error = WidgetMetadataJson.Normalize("{\"displayMode\":\"List\"}", component, listAlreadyStored: true, out var normalized);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(normalized, Is.EqualTo("{\"DisplayMode\":\"List\"}"));
        });
    }

    [Test]
    public void StoresList_TrueOnlyForStoredListMode()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WidgetMetadataJson.StoresList("{\"DisplayMode\":\"List\"}"), Is.True);
            Assert.That(WidgetMetadataJson.StoresList("{\"DisplayMode\":\"Single\"}"), Is.False);
            Assert.That(WidgetMetadataJson.StoresList(null), Is.False);
        });
    }

    [Test]
    public void Normalize_List_UnknownWidget_Fails()
    {
        var error = WidgetMetadataJson.Normalize("{\"displayMode\":\"List\"}", null, out var normalized);

        Assert.Multiple(() =>
        {
            Assert.That(error!.ErrorField, Is.EqualTo("DisplayMode"));
            Assert.That(normalized, Is.Null);
        });
    }

    [TestCase("{\"displayMode\":3}")]
    [TestCase("{\"displayMode\":0}")]
    public void Normalize_DisplayModeNotExactlyOneMode_Fails(string json)
    {
        var component = new WidgetRegistrationInfo { Name = "Faq", SupportsList = true };

        var error = WidgetMetadataJson.Normalize(json, component, out var normalized);

        Assert.Multiple(() =>
        {
            Assert.That(error!.ErrorMessage, Is.EqualTo("Display mode must be Single or List."));
            Assert.That(normalized, Is.Null);
        });
    }
}
