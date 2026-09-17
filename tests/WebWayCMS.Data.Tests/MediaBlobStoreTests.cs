using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class MediaBlobStoreTests
{
    private CmsDbContext _ctx = null!;
    private MediaBlobStore _store = null!;

    private const string Hash = "abc";

    [SetUp]
    public void SetUp()
    {
        _ctx = TestContexts.Cms(TestContexts.NewDb());
        _store = new MediaBlobStore(_ctx);
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public void Constructor_NullContext_Throws() =>
        Assert.That(() => new MediaBlobStore(null!), Throws.ArgumentNullException);

    [Test]
    public async Task PutAsync_StoresAndRoundTripsBytes()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };

        var put = await _store.PutAsync(Hash, bytes);
        var read = await _store.GetAsync(Hash);

        Assert.Multiple(() =>
        {
            Assert.That(put.Success, Is.True);
            Assert.That(put.ErrorMessage, Is.Null);
            Assert.That(read, Is.EqualTo(bytes));
        });
    }

    [Test]
    public async Task PutAsync_BlankHash_Fails()
    {
        var put = await _store.PutAsync("  ", [1]);
        Assert.Multiple(() =>
        {
            Assert.That(put.Success, Is.False);
            Assert.That(put.ErrorMessage, Does.Contain("hash is required"));
        });
    }

    [Test]
    public async Task PutAsync_NullBytes_Fails()
    {
        var put = await _store.PutAsync(Hash, null!);
        Assert.That(put.ErrorMessage, Does.Contain("cannot be empty"));
    }

    [Test]
    public async Task PutAsync_EmptyBytes_Fails()
    {
        var put = await _store.PutAsync(Hash, []);
        Assert.That(put.ErrorMessage, Does.Contain("cannot be empty"));
    }

    [Test]
    public async Task PutAsync_SameHashTwice_IsANoOpAndDoesNotDuplicate()
    {
        await _store.PutAsync(Hash, [1, 2, 3]);
        var second = await _store.PutAsync(Hash, [1, 2, 3]);

        Assert.Multiple(() =>
        {
            Assert.That(second.Success, Is.True);
            Assert.That(_ctx.Set<MediaBlobBytesDTO>().Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GetAsync_UnknownOrBlankHash_ReturnsNull()
    {
        Assert.Multiple(async () =>
        {
            Assert.That(await _store.GetAsync("nope"), Is.Null);
            Assert.That(await _store.GetAsync("  "), Is.Null);
        });
    }

    [Test]
    public async Task ExistsAsync_ReflectsStoredState()
    {
        await _store.PutAsync(Hash, [7]);

        Assert.Multiple(async () =>
        {
            Assert.That(await _store.ExistsAsync(Hash), Is.True);
            Assert.That(await _store.ExistsAsync("other"), Is.False);
            Assert.That(await _store.ExistsAsync(" "), Is.False);
        });
    }

    [Test]
    public async Task DeleteAsync_RemovesStoredBytes()
    {
        await _store.PutAsync(Hash, [1]);

        Assert.Multiple(async () =>
        {
            Assert.That(await _store.DeleteAsync(Hash), Is.True);
            Assert.That(await _store.GetAsync(Hash), Is.Null);
        });
    }

    [Test]
    public async Task DeleteAsync_UnknownOrBlankHash_ReturnsFalse()
    {
        Assert.Multiple(async () =>
        {
            Assert.That(await _store.DeleteAsync("missing"), Is.False);
            Assert.That(await _store.DeleteAsync(""), Is.False);
        });
    }
}
