using Microsoft.AspNetCore.Mvc;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class UrlHelperExtensionsTests
{
    private IUrlHelper _url = null!;

    [SetUp]
    public void SetUp()
    {
        _url = Substitute.For<IUrlHelper>();
        _url.IsLocalUrl(Arg.Any<string?>()).Returns(call => ((string?)call[0])?.StartsWith('/') == true);
    }

    [Test]
    public void LocalOrDefault_LocalUrl_ReturnsIt()
        => Assert.That(_url.LocalOrDefault("/wadmin/pages", "/fallback"), Is.EqualTo("/wadmin/pages"));

    [Test]
    public void LocalOrDefault_ExternalUrl_ReturnsFallback()
        => Assert.That(_url.LocalOrDefault("https://evil.example/", "/fallback"), Is.EqualTo("/fallback"));

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void LocalOrDefault_Empty_ReturnsFallbackOrNull(string? candidate)
    {
        Assert.Multiple(() =>
        {
            Assert.That(_url.LocalOrDefault(candidate, "/fallback"), Is.EqualTo("/fallback"));
            Assert.That(_url.LocalOrDefault(candidate), Is.Null);
        });
    }

    [Test]
    public void LocalOrDefault_NullHelper_Throws()
        => Assert.That(() => ((IUrlHelper)null!).LocalOrDefault("/x"), Throws.ArgumentNullException);
}
