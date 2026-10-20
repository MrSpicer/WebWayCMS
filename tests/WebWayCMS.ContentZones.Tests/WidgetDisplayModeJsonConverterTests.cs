using System.Text.Json;

using NUnit.Framework;

using WebWayCMS.Attributes;

namespace WebWayCMS.ContentZones.Tests;

[TestFixture]
public class WidgetDisplayModeJsonConverterTests
{
    private static WidgetDisplayMode Read(string json)
        => JsonSerializer.Deserialize<WidgetMetadata>("{\"DisplayMode\":" + json + "}")!.DisplayMode;

    [TestCase("\"List\"", WidgetDisplayMode.List)]
    [TestCase("\"single\"", WidgetDisplayMode.Single)]
    [TestCase("2", WidgetDisplayMode.List)]
    [TestCase("\"\"", WidgetDisplayMode.Single)]
    [TestCase("\"  \"", WidgetDisplayMode.Single)]
    [TestCase("null", WidgetDisplayMode.Single)]
    public void Read_NamesNumbersAndEmpty(string json, WidgetDisplayMode expected)
        => Assert.That(Read(json), Is.EqualTo(expected));

    [TestCase("\"Grid\"")]
    [TestCase("true")]
    public void Read_Invalid_Throws(string json)
        => Assert.That(() => Read(json), Throws.InstanceOf<JsonException>());

    [Test]
    public void Write_UsesTheName()
    {
        var json = JsonSerializer.Serialize(new WidgetMetadata { DisplayMode = WidgetDisplayMode.List });

        Assert.That(json, Does.Contain("\"DisplayMode\":\"List\""));
    }
}
