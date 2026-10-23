using NUnit.Framework;

using WebWayCMS.Controllers;
using WebWayCMS.Security;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class HeadMarkupSanitizerTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void SanitizeMeta_Empty_ReturnsEmpty(string? html)
    {
        Assert.That(HeadMarkupSanitizer.SanitizeMeta(html), Is.Empty);
    }

    [Test]
    public void SanitizeMeta_KeepsMetaAndLinkTags()
    {
        var result = HeadMarkupSanitizer.SanitizeMeta(
            """<meta name="description" content="A &amp; B"><meta property="og:image" content="https://x.test/a.png"><link rel="canonical" href="https://x.test/page">""");

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("<meta name=\"description\" content=\"A &amp; B\">"));
            Assert.That(result, Does.Contain("<meta property=\"og:image\" content=\"https://x.test/a.png\">"));
            Assert.That(result, Does.Contain("<link rel=\"canonical\" href=\"https://x.test/page\">"));
        });
    }

    [Test]
    public void SanitizeMeta_StripsScriptsHandlersRefreshAndUnsafeHrefs()
    {
        var result = HeadMarkupSanitizer.SanitizeMeta(
            """<script>alert(1)</script><meta name="x" content="y" onload="alert(1)"><meta http-equiv="refresh" content="0;url=https://evil.test"><link rel="icon" href="javascript:alert(1)"><div>text</div>""");

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Not.Contain("script"));
            Assert.That(result, Does.Not.Contain("onload"));
            Assert.That(result, Does.Not.Contain("http-equiv"));
            Assert.That(result, Does.Not.Contain("javascript"));
            Assert.That(result, Does.Not.Contain("<div"));
            Assert.That(result, Does.Contain("<meta name=\"x\" content=\"y\">"));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    public void SanitizeCss_Empty_ReturnsEmpty(string? css)
    {
        Assert.That(HeadMarkupSanitizer.SanitizeCss(css), Is.Empty);
    }

    [Test]
    public void SanitizeCss_KeepsCssVerbatim()
    {
        const string css = ".nav > li::after { content: \"a&b\"; }";

        Assert.That(HeadMarkupSanitizer.SanitizeCss(css), Is.EqualTo(css));
    }

    [Test]
    public void SanitizeCss_CannotCloseTheStyleElement()
    {
        var result = HeadMarkupSanitizer.SanitizeCss("p{}</style><script>alert(1)</script></STYLE>");

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Not.Contain("</"));
            Assert.That(result, Is.EqualTo("p{}<\\/style><script>alert(1)<\\/script><\\/STYLE>"));
        });
    }

    [Test]
    public void GenericPageConfiguration_SanitizedForRender_AppliesFieldRules()
    {
        var config = new GenericPageConfiguration
        {
            Style = "a > b {}</style>",
            Meta = """<meta name="d" content="c"><script>x</script>"""
        };

        var safe = config.SanitizedForRender();

        Assert.Multiple(() =>
        {
            Assert.That(safe, Is.Not.SameAs(config));
            Assert.That(safe.Style, Is.EqualTo("a > b {}<\\/style>"));
            Assert.That(safe.Meta, Is.EqualTo("<meta name=\"d\" content=\"c\">"));
            Assert.That(config.Meta, Does.Contain("script"));
        });
    }
}
