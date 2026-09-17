using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class MediaLibraryTests
{
    private CmsDbContext _ctx = null!;
    private MediaBlobStore _blobStore = null!;
    private MediaLibrary _library = null!;

    private static readonly byte[] Bytes = [1, 2, 3, 4, 5];

    [SetUp]
    public void SetUp()
    {
        _ctx = TestContexts.Cms(TestContexts.NewDb());
        _blobStore = new MediaBlobStore(_ctx);
        _library = new MediaLibrary(_ctx, _blobStore);
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new MediaLibrary(null!, _blobStore), Throws.ArgumentNullException);
            Assert.That(() => new MediaLibrary(_ctx, null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void ComputeHash_IsStableLowercaseHexSha256()
    {
        var hash = _library.ComputeHash(Bytes);

        Assert.Multiple(() =>
        {
            Assert.That(hash, Has.Length.EqualTo(64));
            Assert.That(hash, Is.EqualTo(hash.ToLowerInvariant()));
            Assert.That(_library.ComputeHash(Bytes), Is.EqualTo(hash));
            Assert.That(_library.ComputeHash([9, 9]), Is.Not.EqualTo(hash));
        });
    }

    [Test]
    public void ComputeHash_Null_Throws() =>
        Assert.That(() => _library.ComputeHash(null!), Throws.ArgumentNullException);

    [Test]
    public async Task AddAsync_CatalogsAndStoresBytes()
    {
        var (success, error, blob) = await _library.AddAsync(Bytes, "image/png", "a.png", 10, 20);

        Assert.Multiple(async () =>
        {
            Assert.That(success, Is.True);
            Assert.That(error, Is.Null);
            Assert.That(blob!.Hash, Is.EqualTo(_library.ComputeHash(Bytes)));
            Assert.That(blob.ContentType, Is.EqualTo("image/png"));
            Assert.That(blob.ByteLength, Is.EqualTo(Bytes.Length));
            Assert.That(blob.Width, Is.EqualTo(10));
            Assert.That(blob.Height, Is.EqualTo(20));
            Assert.That(blob.OriginalFileName, Is.EqualTo("a.png"));
            Assert.That(await _blobStore.GetAsync(blob.Hash), Is.EqualTo(Bytes));
        });
    }

    [Test]
    public async Task AddAsync_IdenticalBytes_DeduplicatesToOneCatalogRow()
    {
        var first = await _library.AddAsync(Bytes, "image/png", "a.png", 10, 20);
        var second = await _library.AddAsync(Bytes, "image/png", "copy.png", 10, 20);

        Assert.Multiple(() =>
        {
            Assert.That(second.Success, Is.True);
            Assert.That(second.Blob!.Hash, Is.EqualTo(first.Blob!.Hash));
            // The original catalog entry wins -- the second upload adds no new row.
            Assert.That(second.Blob.OriginalFileName, Is.EqualTo("a.png"));
            Assert.That(_ctx.Set<MediaBlobDTO>().Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AddAsync_EmptyOrNullBytes_Fails()
    {
        Assert.Multiple(async () =>
        {
            Assert.That((await _library.AddAsync([], "image/png", "a.png", 1, 1)).ErrorMessage, Does.Contain("cannot be empty"));
            Assert.That((await _library.AddAsync(null!, "image/png", "a.png", 1, 1)).ErrorMessage, Does.Contain("cannot be empty"));
        });
    }

    [Test]
    public async Task AddAsync_BlankContentType_Fails()
    {
        var result = await _library.AddAsync(Bytes, "  ", "a.png", 1, 1);
        Assert.That(result.ErrorMessage, Does.Contain("content type is required"));
    }

    [Test]
    public async Task AddAsync_NullFileName_StoresEmptyName()
    {
        var result = await _library.AddAsync(Bytes, "image/png", null!, 1, 1);
        Assert.That(result.Blob!.OriginalFileName, Is.Empty);
    }

    [Test]
    public async Task AddAsync_BlobStoreFailure_ReportsAndCatalogsNothing()
    {
        var library = new MediaLibrary(_ctx, new FailingBlobStore());

        var result = await library.AddAsync(Bytes, "image/png", "a.png", 1, 1);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("disk on fire"));
            Assert.That(_ctx.Set<MediaBlobDTO>().Count(), Is.Zero);
        });
    }

    [Test]
    public async Task GetMetadataAsync_UnknownOrBlankHash_ReturnsNull()
    {
        Assert.Multiple(async () =>
        {
            Assert.That(await _library.GetMetadataAsync("nope"), Is.Null);
            Assert.That(await _library.GetMetadataAsync("  "), Is.Null);
        });
    }

    [Test]
    public async Task GetBytesAsync_DelegatesToBlobStore()
    {
        var added = await _library.AddAsync(Bytes, "image/png", "a.png", 1, 1);
        Assert.That(await _library.GetBytesAsync(added.Blob!.Hash), Is.EqualTo(Bytes));
    }

    [Test]
    public async Task ListAsync_ReturnsNewestFirstAndClampsPaging()
    {
        await _library.AddAsync([1], "image/png", "one.png", 1, 1);
        await Task.Delay(5);
        await _library.AddAsync([2], "image/png", "two.png", 1, 1);

        var all = await _library.ListAsync();
        var clamped = await _library.ListAsync(skip: -5, take: 0);

        Assert.Multiple(() =>
        {
            Assert.That(all[0].OriginalFileName, Is.EqualTo("two.png"));
            Assert.That(clamped, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task DeleteAsync_BlankHash_Fails()
    {
        var result = await _library.DeleteAsync(" ");
        Assert.That(result.ErrorMessage, Does.Contain("hash is required"));
    }

    [Test]
    public async Task DeleteAsync_UnknownHash_Fails()
    {
        var result = await _library.DeleteAsync("missing");
        Assert.That(result.ErrorMessage, Does.Contain("no longer exists"));
    }

    [Test]
    public async Task DeleteAsync_RemovesCatalogRowAndBytes()
    {
        var added = await _library.AddAsync(Bytes, "image/png", "a.png", 1, 1);

        var result = await _library.DeleteAsync(added.Blob!.Hash);

        Assert.Multiple(async () =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(_ctx.Set<MediaBlobDTO>().Count(), Is.Zero);
            Assert.That(await _blobStore.GetAsync(added.Blob.Hash), Is.Null);
        });
    }

    [Test]
    public async Task DeleteAsync_StillReferencedByAnyImageVersion_IsRefused()
    {
        var added = await _library.AddAsync(Bytes, "image/png", "a.png", 1, 1);

        _ctx.Set<ImageDTO>().Add(new ImageDTO
        {
            VersionId = Guid.NewGuid(),
            BlobHash = added.Blob!.Hash,
            AltText = "alt",
            Version = new ContentVersion { Node = new ContentNode { Id = Guid.NewGuid() } }
        });
        await _ctx.SaveChangesAsync();

        var result = await _library.DeleteAsync(added.Blob.Hash);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo(MediaLibrary.ReferencedMessage));
            Assert.That(_ctx.Set<MediaBlobDTO>().Count(), Is.EqualTo(1));
        });
    }

    private sealed class FailingBlobStore : IMediaBlobStore
    {
        public Task<byte[]?> GetAsync(string hash, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);
        public Task<bool> ExistsAsync(string hash, CancellationToken ct = default) => Task.FromResult(false);
        public Task<(bool Success, string? ErrorMessage)> PutAsync(string hash, byte[] bytes, CancellationToken ct = default)
            => Task.FromResult((false, (string?)"disk on fire"));
        public Task<bool> DeleteAsync(string hash, CancellationToken ct = default) => Task.FromResult(false);
    }
}
