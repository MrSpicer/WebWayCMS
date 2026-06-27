using NUnit.Framework;

namespace WebWayCMS.Mcp.Tests;

[TestFixture]
public class McpOptionsTests
{
    [Test]
    public void Defaults_AreDisabledWithMcpPath()
    {
        var options = new McpOptions();

        Assert.Multiple(() =>
        {
            Assert.That(options.Enabled, Is.False);
            Assert.That(options.ApiKey, Is.Null);
            Assert.That(options.Path, Is.EqualTo("/mcp"));
            Assert.That(McpOptions.SectionName, Is.EqualTo("Mcp"));
        });
    }

    [Test]
    public void Properties_AreSettable()
    {
        var options = new McpOptions { Enabled = true, ApiKey = "secret", Path = "/tools" };

        Assert.Multiple(() =>
        {
            Assert.That(options.Enabled, Is.True);
            Assert.That(options.ApiKey, Is.EqualTo("secret"));
            Assert.That(options.Path, Is.EqualTo("/tools"));
        });
    }
}
