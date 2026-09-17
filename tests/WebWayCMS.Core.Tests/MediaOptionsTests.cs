using NUnit.Framework;

using WebWayCMS.Media;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class MediaOptionsTests
{
    [Test]
    public void Defaults_AreTenMegabytesAndAnOpenFormatList()
    {
        var options = new MediaOptions();

        Assert.Multiple(() =>
        {
            Assert.That(MediaOptions.SectionName, Is.EqualTo("Media"));
            Assert.That(options.MaxUploadBytes, Is.EqualTo(10 * 1024 * 1024));
            Assert.That(options.MaxUploadBytes, Is.EqualTo(MediaOptions.DefaultMaxUploadBytes));
            Assert.That(options.AllowedContentTypes, Is.Empty);
        });
    }
}
