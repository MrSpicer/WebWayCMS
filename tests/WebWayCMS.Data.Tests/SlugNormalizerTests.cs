using NUnit.Framework;

using WebWayCMS.Data.Slugs;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class SlugNormalizerTests
{
    [Test]
    public void Normalize_BlankSource_ReturnsEmpty()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SlugNormalizer.Normalize(null), Is.EqualTo(string.Empty));
            Assert.That(SlugNormalizer.Normalize(""), Is.EqualTo(string.Empty));
            Assert.That(SlugNormalizer.Normalize("   "), Is.EqualTo(string.Empty));
        });
    }

    [Test]
    public void Normalize_PlainTitle_SlugifiesToLowercase()
    {
        Assert.That(SlugNormalizer.Normalize("Hello World"), Is.EqualTo("hello-world"));
    }

    [Test]
    public void Normalize_Punctuation_CollapsesToSingleHyphens()
    {
        Assert.That(SlugNormalizer.Normalize("Spaced Slug Page!!"), Is.EqualTo("spaced-slug-page"));
    }

    [Test]
    public void Normalize_ConsecutiveWhitespace_CollapsesToSingleHyphen()
    {
        Assert.That(SlugNormalizer.Normalize("  Two   Spaces  "), Is.EqualTo("two-spaces"));
    }

    [Test]
    public void Normalize_LeadingTrailingHyphens_AreTrimmed()
    {
        Assert.That(SlugNormalizer.Normalize("-Hello World-"), Is.EqualTo("hello-world"));
    }

    [Test]
    public void Normalize_AccentedLatin_FoldsToAscii()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SlugNormalizer.Normalize("Café Ünïcode"), Is.EqualTo("cafe-unicode"));
            Assert.That(SlugNormalizer.Normalize("Über Page"), Is.EqualTo("uber-page"));
        });
    }

    [Test]
    public void Normalize_NonLatinSource_FallsBackToPercentEncoding()
    {
        var slug = SlugNormalizer.Normalize("日本語ページ");
        Assert.Multiple(() =>
        {
            Assert.That(slug, Is.EqualTo(Uri.EscapeDataString("日本語ページ")));
            Assert.That(slug, Is.Not.Empty);
        });
    }

    [Test]
    public void Normalize_PunctuationOnlySource_FallsBackToPercentEncoding()
    {
        Assert.That(SlugNormalizer.Normalize("!!!"), Is.EqualTo(Uri.EscapeDataString("!!!")));
    }

    [Test]
    public void Normalize_MixedNonLatinAndLatin_KeepsLatinOnly()
    {
        Assert.That(SlugNormalizer.Normalize("日本語 Page"), Is.EqualTo("page"));
    }

    [Test]
    public void Normalize_AlreadyCleanSlug_IsUnchanged()
    {
        Assert.That(SlugNormalizer.Normalize("hello-world"), Is.EqualTo("hello-world"));
    }

    [Test]
    public void Normalize_UnreservedPunctuation_IsCollapsedToHyphens()
    {
        Assert.That(SlugNormalizer.Normalize("a.b_c~d"), Is.EqualTo("a-b-c-d"));
    }

    [Test]
    public void Normalize_AlreadyPercentEncoded_IsPassedThrough()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SlugNormalizer.Normalize("%E6%97%A5"), Is.EqualTo("%E6%97%A5"));
            Assert.That(SlugNormalizer.Normalize("%e6"), Is.EqualTo("%e6"));
        });
    }

    [Test]
    public void Normalize_InvalidPercentSequences_AreSlugifiedNotPassedThrough()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SlugNormalizer.Normalize("50%"), Is.EqualTo("50"));
            Assert.That(SlugNormalizer.Normalize("%0x"), Is.EqualTo("0x"));
            Assert.That(SlugNormalizer.Normalize("%z0"), Is.EqualTo("z0"));
            Assert.That(SlugNormalizer.Normalize("%-5"), Is.EqualTo("5"));
        });
    }

    [Test]
    public void Normalize_IsIdempotent()
    {
        var samples = new[] { "Hello World", "Café Ünïcode", "日本語ページ", "!!!", "two  spaces", "-already-clean-", "50%" };
        foreach (var sample in samples)
            Assert.That(SlugNormalizer.Normalize(SlugNormalizer.Normalize(sample)), Is.EqualTo(SlugNormalizer.Normalize(sample)));
    }
}
