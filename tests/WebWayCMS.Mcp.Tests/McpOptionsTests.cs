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
            Assert.That(options.SeedTokens, Is.Empty);
            Assert.That(options.AllowSeedTokensOutsideDevelopment, Is.False);
            Assert.That(options.Path, Is.EqualTo("/mcp"));
            Assert.That(McpOptions.SectionName, Is.EqualTo("Mcp"));
        });
    }

    [Test]
    public void Properties_AreSettable()
    {
        var seed = new McpSeedToken { UserEmail = "admin@example.com", Name = "Dev", Token = "t" };
        var options = new McpOptions { Enabled = true, Path = "/tools", SeedTokens = [seed], AllowSeedTokensOutsideDevelopment = true };

        Assert.Multiple(() =>
        {
            Assert.That(options.Enabled, Is.True);
            Assert.That(options.Path, Is.EqualTo("/tools"));
            Assert.That(options.SeedTokens.Single(), Is.SameAs(seed));
            Assert.That(options.AllowSeedTokensOutsideDevelopment, Is.True);
            Assert.That((seed.UserEmail, seed.Name, seed.Token), Is.EqualTo(("admin@example.com", "Dev", "t")));
        });
    }

    [Test]
    public void SeedToken_DefaultsToTheSeededName()
    {
        var seed = new McpSeedToken();

        Assert.That((seed.UserEmail, seed.Name, seed.Token), Is.EqualTo((string.Empty, "Seeded", string.Empty)));
    }
}
