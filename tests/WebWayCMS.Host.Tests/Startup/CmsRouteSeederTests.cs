using NUnit.Framework;

using WebWayCMS.Startup;

namespace WebWayCMS.Host.Tests.Startup;

[TestFixture]
public class CmsRouteSeederTests
{
    [TestCase("/test", "/test")]
    [TestCase("/", "/")]
    [TestCase("", "/")]
    [TestCase("  ", "/")]
    [TestCase("/test/", "/test")]
    [TestCase("test", "/test")]
    [TestCase("TEST", "/test")]
    public void NormalizeRoutePattern_NormalizesCorrectly(string input, string expected)
    {
        Assert.That(CmsRouteSeeder.NormalizeRoutePattern(input), Is.EqualTo(expected));
    }
}
