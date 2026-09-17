using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class MediaControllerTests
{
    private IMediaLibrary _library = null!;
    private MediaController _controller = null!;

    private const string Hash = "d3adb33f";

    [SetUp]
    public void SetUp()
    {
        _library = Substitute.For<IMediaLibrary>();
        _controller = new MediaController(_library);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new MvcHarness().NewHttpContext([])
        };
    }

    private static MediaBlobDTO Blob() => new()
    {
        Hash = Hash,
        ContentType = "image/png",
        ByteLength = 3,
        Width = 10,
        Height = 20,
        OriginalFileName = "a.png",
        CreatedUtc = DateTime.UtcNow
    };

    [Test]
    public void Constructor_NullLibrary_Throws() =>
        Assert.That(() => new MediaController(null!), Throws.ArgumentNullException);

    [Test]
    public async Task Get_BlankHash_ReturnsNotFound()
    {
        var result = await _controller.Get("  ", null);
        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public async Task Get_UnknownHash_ReturnsNotFound()
    {
        _library.GetMetadataAsync(Hash, Arg.Any<CancellationToken>()).Returns((MediaBlobDTO?)null);

        var result = await _controller.Get(Hash, null);
        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public async Task Get_KnownHash_ReturnsFileWithImmutableCacheHeaders()
    {
        _library.GetMetadataAsync(Hash, Arg.Any<CancellationToken>()).Returns(Blob());
        _library.GetBytesAsync(Hash, Arg.Any<CancellationToken>()).Returns([1, 2, 3]);

        var result = await _controller.Get(Hash, "a.png");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.InstanceOf<FileContentResult>());
            Assert.That(((FileContentResult)result).ContentType, Is.EqualTo("image/png"));
            Assert.That(((FileContentResult)result).FileContents, Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(_controller.Response.Headers[HeaderNames.CacheControl].ToString(),
                Is.EqualTo(MediaController.CacheControlValue));
            Assert.That(_controller.Response.Headers[HeaderNames.ETag].ToString(), Is.EqualTo($"\"{Hash}\""));
        });
    }

    [Test]
    public async Task Get_MatchingIfNoneMatch_Returns304()
    {
        _library.GetMetadataAsync(Hash, Arg.Any<CancellationToken>()).Returns(Blob());
        _controller.Request.Headers.IfNoneMatch = $"\"{Hash}\"";

        var result = await _controller.Get(Hash, null);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.InstanceOf<StatusCodeResult>());
            Assert.That(((StatusCodeResult)result).StatusCode, Is.EqualTo(304));
        });
    }

    [Test]
    public async Task Get_WildcardIfNoneMatch_Returns304()
    {
        _library.GetMetadataAsync(Hash, Arg.Any<CancellationToken>()).Returns(Blob());
        _controller.Request.Headers.IfNoneMatch = "*";

        var result = await _controller.Get(Hash, null);
        Assert.That(((StatusCodeResult)result).StatusCode, Is.EqualTo(304));
    }

    [Test]
    public async Task Get_NonMatchingIfNoneMatch_ReturnsFile()
    {
        _library.GetMetadataAsync(Hash, Arg.Any<CancellationToken>()).Returns(Blob());
        _library.GetBytesAsync(Hash, Arg.Any<CancellationToken>()).Returns([9]);
        _controller.Request.Headers.IfNoneMatch = "\"something-else\"";

        var result = await _controller.Get(Hash, null);
        Assert.That(result, Is.InstanceOf<FileContentResult>());
    }

    [Test]
    public async Task Get_CataloguedButBytesMissing_ReturnsNotFound()
    {
        _library.GetMetadataAsync(Hash, Arg.Any<CancellationToken>()).Returns(Blob());
        _library.GetBytesAsync(Hash, Arg.Any<CancellationToken>()).Returns((byte[]?)null);

        var result = await _controller.Get(Hash, null);
        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }
}
