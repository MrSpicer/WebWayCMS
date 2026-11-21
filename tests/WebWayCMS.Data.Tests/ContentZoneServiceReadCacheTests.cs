using Microsoft.EntityFrameworkCore;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Data.Services.Caching;

namespace WebWayCMS.Data.Tests;

/// <summary>
/// The zone service's <c>GetItemsAsync</c>/<c>GetZoneByNameAsync</c> read through
/// <see cref="IContentReadCache"/> in Published mode only — like the store.
/// </summary>
[TestFixture]
public class ContentZoneServiceReadCacheTests
{
    private string _db = null!;
    private FakeContentReadCache _cache = null!;

    [SetUp]
    public void SetUp()
    {
        _db = TestContexts.NewDb();
        _cache = new FakeContentReadCache();
    }

    private ContentZoneService NewService(ContentReadMode mode = ContentReadMode.Published, IContentReadCache? readCache = null,
        bool inTransaction = false)
    {
        var context = inTransaction ? TestContexts.CmsInTransaction(_db) : TestContexts.Cms(_db);
        var rc = new TestReadContext(mode);
        var scope = new ChangeSetScope(context, new DefaultContentUserContext());
        var zoneStore = new ContentStore<ContentZoneDTO>(context, rc, scope, new DefaultContentUserContext(), "contentzones");
        var itemStore = new ContentStore<ContentZoneItemDTO>(context, rc, scope, new DefaultContentUserContext(), "contentzoneitems");
        return new ContentZoneService(context, zoneStore, itemStore, rc, scope, Substitute.For<ICMSRouteService>(),
            new PageZoneTree(context), Substitute.For<IPageHistoryRecorder>(), readCache);
    }

    private static ContentZoneDTO PublishedZone(string name)
    {
        var nodeId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        return new ContentZoneDTO
        {
            VersionId = versionId,
            Name = name,
            Version = new ContentVersion
            {
                Id = versionId,
                NodeId = nodeId,
                Node = new ContentNode { Id = nodeId, ContentTypeKey = "contentzones" },
                State = ContentVersionState.Published,
                IsCurrentDraft = true,
                Title = name,
            },
        };
    }

    private static ContentZoneItemDTO PublishedItem(ContentVersion zoneVersion, int ordinal)
    {
        var nodeId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var item = new ContentZoneItemDTO
        {
            VersionId = versionId,
            IsActive = true,
            ComponentName = "ContentBlock",
            Version = new ContentVersion
            {
                Id = versionId,
                NodeId = nodeId,
                Node = new ContentNode { Id = nodeId, ContentTypeKey = "contentzoneitems" },
                State = ContentVersionState.Published,
                IsCurrentDraft = true,
                Title = "ContentBlock",
            },
        };
        ZoneFixture.Place(item.Version, zoneVersion.NodeId, ordinal);
        return item;
    }

    private async Task<ContentZoneDTO> SeedZoneAsync(string name, int items = 1)
    {
        var zone = PublishedZone(name);
        using var context = TestContexts.Cms(_db);
        context.AddRange(zone, zone.Version, zone.Version.Node!);
        for (var i = 0; i < items; i++)
            context.AddRange(PublishedItem(zone.Version, i));
        await context.SaveChangesAsync();
        return zone;
    }

    [Test]
    public async Task GetItemsAsync_Published_CachesAfterFirstCall()
    {
        var zone = await SeedZoneAsync("Main");

        var first = await NewService(readCache: _cache).GetItemsAsync(zone.Version.NodeId);
        var second = await NewService(readCache: _cache).GetItemsAsync(zone.Version.NodeId);

        Assert.Multiple(() =>
        {
            Assert.That(first, Has.Count.EqualTo(1));
            Assert.That(second, Has.Count.EqualTo(1));
            Assert.That(_cache.FactoryCalls, Is.EqualTo(1));
            Assert.That(_cache.Keys.Single(), Does.StartWith("zonesvc:items:"));
        });
    }

    [Test]
    public async Task GetZoneByNameAsync_Published_CachesAfterFirstCall()
    {
        await SeedZoneAsync("Main");

        var first = await NewService(readCache: _cache).GetZoneByNameAsync("Main");
        var second = await NewService(readCache: _cache).GetZoneByNameAsync("Main");

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(_cache.FactoryCalls, Is.EqualTo(1));
            Assert.That(_cache.Keys.Single(), Does.Contain("zone:Main"));
        });
    }

    [Test]
    public async Task GetZoneByNameAsync_Whitespace_NeverCached()
    {
        Assert.That(await NewService().GetZoneByNameAsync(""), Is.Null);
        Assert.That(_cache.FactoryCalls, Is.Zero);
    }

    [Test]
    public async Task Reads_DraftMode_NeverTouchTheCache()
    {
        await SeedZoneAsync("Main");

        var first = await NewService(ContentReadMode.Draft, _cache).GetZoneByNameAsync("Main");
        var second = await NewService(ContentReadMode.Draft, _cache).GetZoneByNameAsync("Main");

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(_cache.FactoryCalls, Is.Zero, "Draft reads go straight to the database");
        });
    }

    [Test]
    public async Task Reads_NullCache_NeverCache()
    {
        await SeedZoneAsync("Main");

        _ = await NewService(readCache: null).GetZoneByNameAsync("Main");
        _ = await NewService().GetZoneByNameAsync("Main");

        Assert.That(_cache.FactoryCalls, Is.Zero);
    }

    [Test]
    public async Task Reads_InOpenTransaction_BypassTheCache()
    {
        // The global-zone create re-reads inside its transaction: it must see a concurrent creator's
        // commit, never the null cached by the first (outside) read.
        var zone = await SeedZoneAsync("Main");
        var service = NewService(readCache: _cache, inTransaction: true);

        var byName = await service.GetZoneByNameAsync("Main");
        var items = await service.GetItemsAsync(zone.Version.NodeId);

        Assert.Multiple(() =>
        {
            Assert.That(byName, Is.Not.Null);
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(_cache.FactoryCalls, Is.Zero);
        });
    }

    [Test]
    public async Task Reads_PassTheCallersToken()
    {
        var zone = await SeedZoneAsync("Main");
        using var cts = new CancellationTokenSource();
        var service = NewService(readCache: _cache);

        _ = await service.GetZoneByNameAsync("Main", cts.Token);
        _ = await service.GetItemsAsync(zone.Version.NodeId, cts.Token);

        Assert.That(_cache.Tokens, Has.Count.EqualTo(2).And.All.EqualTo(cts.Token));
    }
}
