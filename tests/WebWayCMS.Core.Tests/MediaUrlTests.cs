using NUnit.Framework;

using WebWayCMS.Media;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class MediaUrlTests
{
    private const string Hash = "abc123";

    [Test]
    public void For_NullOrWhitespaceHash_ReturnsEmpty()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MediaUrl.For(null), Is.Empty);
            Assert.That(MediaUrl.For(""), Is.Empty);
            Assert.That(MediaUrl.For("   "), Is.Empty);
        });
    }

    [Test]
    public void For_NoFileName_UsesFallbackTail() =>
        Assert.That(MediaUrl.For(Hash), Is.EqualTo($"/media/{Hash}/{MediaUrl.FallbackFileName}"));

    [Test]
    public void For_KeepsSafeFileName() =>
        Assert.That(MediaUrl.For(Hash, "photo_1-final.png"), Is.EqualTo($"/media/{Hash}/photo_1-final.png"));

    [Test]
    public void For_ReplacesSpacesWithHyphens() =>
        Assert.That(MediaUrl.For(Hash, "my holiday photo.jpg"), Is.EqualTo($"/media/{Hash}/my-holiday-photo.jpg"));

    [Test]
    public void SafeFileName_StripsDirectoryComponents()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MediaUrl.SafeFileName("/etc/passwd"), Is.EqualTo("passwd"));
            Assert.That(MediaUrl.SafeFileName(@"C:\windows\evil.png"), Is.EqualTo("evil.png"));
        });
    }

    [Test]
    public void SafeFileName_DropsUnsafeCharacters() =>
        Assert.That(MediaUrl.SafeFileName("a?b#c%d&e.png"), Is.EqualTo("abcde.png"));

    [Test]
    public void SafeFileName_NullOrWhitespace_ReturnsFallback()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MediaUrl.SafeFileName(null), Is.EqualTo(MediaUrl.FallbackFileName));
            Assert.That(MediaUrl.SafeFileName("  "), Is.EqualTo(MediaUrl.FallbackFileName));
        });
    }

    [Test]
    public void SafeFileName_AllCharactersStripped_ReturnsFallback() =>
        Assert.That(MediaUrl.SafeFileName("???"), Is.EqualTo(MediaUrl.FallbackFileName));

    [Test]
    public void SafeFileName_TrimsLeadingAndTrailingDotsAndHyphens() =>
        Assert.That(MediaUrl.SafeFileName("..hidden.png.."), Is.EqualTo("hidden.png"));

    [Test]
    public void SafeFileName_OnlyDots_ReturnsFallback() =>
        Assert.That(MediaUrl.SafeFileName("..."), Is.EqualTo(MediaUrl.FallbackFileName));
}
