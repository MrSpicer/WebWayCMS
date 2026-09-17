using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mcp;

namespace WebWayCMS.Mcp.Tests;

[TestFixture]
public class MediaToolsetTests
{
    private IMediaLibrary _library = null!;
    private MediaToolset _toolset = null!;

    [SetUp]
    public void SetUp()
    {
        _library = Substitute.For<IMediaLibrary>();
        _toolset = new MediaToolset(_library);
    }

    [Test]
    public void Constructor_NullLibrary_Throws() =>
        Assert.That(() => new MediaToolset(null!), Throws.ArgumentNullException);

    [Test]
    public async Task ListMedia_ProjectsCatalogRowsIncludingPublicUrl()
    {
        _library.ListAsync(0, 50, Arg.Any<CancellationToken>()).Returns(new List<MediaBlobDTO>
        {
            new()
            {
                Hash = "abc",
                ContentType = "image/png",
                ByteLength = 12,
                Width = 3,
                Height = 4,
                OriginalFileName = "logo.png",
                CreatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        });

        var result = await _toolset.ListMedia();

        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Hash, Is.EqualTo("abc"));
            Assert.That(result[0].ContentType, Is.EqualTo("image/png"));
            Assert.That(result[0].Width, Is.EqualTo(3));
            Assert.That(result[0].Url, Is.EqualTo("/media/abc/logo.png"));
        });
    }

    [Test]
    public async Task ListMedia_PassesPagingThrough()
    {
        _library.ListAsync(5, 10, Arg.Any<CancellationToken>()).Returns(new List<MediaBlobDTO>());

        var result = await _toolset.ListMedia(5, 10);

        Assert.That(result, Is.Empty);
        await _library.Received(1).ListAsync(5, 10, Arg.Any<CancellationToken>());
    }
}
