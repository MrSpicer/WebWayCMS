using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Data.Services.Caching;

namespace WebWayCMS.Data.Tests;

/// <summary>
/// The store's read-context methods read through <see cref="IContentReadCache"/> in Published mode
/// only — Draft and Snapshot reads (per-request / governed) and a null cache always hit the database.
/// </summary>
[TestFixture]
public class ContentStoreReadCacheTests
{
    private string _db = null!;
    private FakeContentReadCache _cache = null!;

    [SetUp]
    public void SetUp()
    {
        _db = TestContexts.NewDb();
        _cache = new FakeContentReadCache();
    }

    private ContentStore<ContentBlockDTO> NewStore(ContentReadMode mode = ContentReadMode.Published, IContentReadCache? readCache = null)
        => TestStore.Create<ContentBlockDTO>(TestContexts.Cms(_db), "contentblocks", mode, readCache);

    private async Task<ContentBlockDTO> CreatePublishedAsync()
    {
        var store = NewStore();
        var dto = new ContentBlockDTO { Content = "c", Version = new ContentVersion { Title = "T" } };
        await store.SaveDraftAsync(dto, null);
        await store.PublishAsync(dto.Version.Node!.Id);
        return dto;
    }

    [Test]
    public async Task GetAsync_Published_CachesAfterFirstCall()
    {
        var dto = await CreatePublishedAsync();

        var first = await NewStore(readCache: _cache).GetAsync(dto.Version.Node!.Id);
        var second = await NewStore(readCache: _cache).GetAsync(dto.Version.Node!.Id);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(_cache.FactoryCalls, Is.EqualTo(1));
            Assert.That(_cache.Keys.Single(), Does.StartWith("contentblocks:get:"));
        });
    }

    [Test]
    public async Task GetAllAsync_Published_CachesAfterFirstCall()
    {
        await CreatePublishedAsync();

        var first = await NewStore(readCache: _cache).GetAllAsync();
        var second = await NewStore(readCache: _cache).GetAllAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first, Has.Count.GreaterThan(0));
            Assert.That(second, Has.Count.EqualTo(first.Count));
            Assert.That(_cache.FactoryCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GetBySlugAsync_Published_CachesAfterFirstCall()
    {
        var store = NewStore();
        var dto = new ContentBlockDTO { Content = "c", Version = new ContentVersion { Title = "T", Slug = "my-slug" } };
        await store.SaveDraftAsync(dto, null);
        await store.PublishAsync(dto.Version.Node!.Id);

        var first = await NewStore(readCache: _cache).GetBySlugAsync("my-slug");
        var second = await NewStore(readCache: _cache).GetBySlugAsync("my-slug");

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(_cache.FactoryCalls, Is.EqualTo(1));
            Assert.That(_cache.Keys.Single(), Does.Contain("slug:my-slug"));
        });
    }

    [Test]
    public async Task GetBySlugAsync_Whitespace_NeverCached()
    {
        var store = NewStore(readCache: _cache);

        Assert.That(await store.GetBySlugAsync("  "), Is.Null);

        Assert.That(_cache.FactoryCalls, Is.Zero);
        Assert.That(_cache.Keys, Is.Empty);
    }

    [Test]
    public async Task FindRelatedAsync_Published_CachesAfterFirstCall()
    {
        var store = NewStore();
        // A draft item related to a target, then published: FindRelatedAsync reads it back.
        var target = Guid.NewGuid();
        var related = new ContentBlockDTO { Content = "r", Version = new ContentVersion { Title = "R" } };
        await store.SaveDraftAsync(related, null);
        var nodeId = related.Version.Node!.Id;
        await store.PublishAsync(nodeId);

        var first = await NewStore(readCache: _cache).FindRelatedAsync("Test", target);
        var second = await NewStore(readCache: _cache).FindRelatedAsync("Test", target);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(_cache.FactoryCalls, Is.EqualTo(1));
            Assert.That(_cache.Keys.Single(), Does.Contain("related:Test"));
        });
    }

    [TestCase(ContentReadMode.Draft)]
    [TestCase(ContentReadMode.Snapshot)]
    public async Task Reads_NonPublishedModes_NeverTouchTheCache(ContentReadMode mode)
    {
        await CreatePublishedAsync();

        var first = await NewStore(mode, _cache).GetAllAsync();
        var second = await NewStore(mode, _cache).GetAllAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first, Has.Count.GreaterThan(0));
            Assert.That(second, Has.Count.EqualTo(first.Count));
            Assert.That(_cache.FactoryCalls, Is.Zero, "Draft/Snapshot reads go straight to the database");
            Assert.That(_cache.Keys, Is.Empty);
        });
    }

    [Test]
    public async Task Reads_NullCache_NeverCache()
    {
        await CreatePublishedAsync();

        _ = await NewStore(readCache: null).GetAllAsync();
        _ = await NewStore(readCache: null).GetAllAsync();
        _ = await NewStore().GetAllAsync(); // no cache registered at all

        Assert.That(_cache.FactoryCalls, Is.Zero);
    }

    [Test]
    public async Task FindAsync_NeverCached()
    {
        await CreatePublishedAsync();

        _ = await NewStore(readCache: _cache).FindAsync(b => b.Content == "c");
        _ = await NewStore(readCache: _cache).FindAsync(b => b.Content == "c");

        Assert.That(_cache.FactoryCalls, Is.Zero);
    }

    [Test]
    public async Task Reads_InOpenTransaction_BypassTheCache()
    {
        var dto = await CreatePublishedAsync();
        var store = TestStore.Create<ContentBlockDTO>(TestContexts.CmsInTransaction(_db), "contentblocks", readCache: _cache);

        var got = await store.GetAsync(dto.Version.Node!.Id);
        _ = await store.GetAllAsync();
        _ = await store.GetBySlugAsync("x");
        _ = await store.FindRelatedAsync("r", Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(got, Is.Not.Null);
            Assert.That(_cache.FactoryCalls, Is.Zero, "a read inside a transaction may see uncommitted writes");
        });
    }

    [Test]
    public async Task Reads_PassTheCallersToken()
    {
        var dto = await CreatePublishedAsync();
        using var cts = new CancellationTokenSource();
        var store = NewStore(readCache: _cache);

        _ = await store.GetAsync(dto.Version.Node!.Id, cts.Token);
        _ = await store.GetAllAsync(cts.Token);
        _ = await store.GetBySlugAsync("x", cts.Token);
        _ = await store.FindRelatedAsync("r", Guid.NewGuid(), cts.Token);

        Assert.That(_cache.Tokens, Has.Count.EqualTo(4).And.All.EqualTo(cts.Token));
    }
}
