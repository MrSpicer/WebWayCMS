using NUnit.Framework;

using WebWayCMS.Services.ContentSeeding;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class ContentSeedOptionsTests
{
    [Test]
    public void Defaults_AreExpected()
    {
        var options = new ContentSeedOptions();

        Assert.Multiple(() =>
        {
            Assert.That(options.Enabled, Is.True);
            Assert.That(options.Path, Is.EqualTo("contentseed"));
            Assert.That(options.ResourceSuffix, Is.EqualTo(".contentseed.json"));
            Assert.That(ContentSeedOptions.SectionName, Is.EqualTo("ContentSeed"));
        });
    }
}
