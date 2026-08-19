using Microsoft.EntityFrameworkCore;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class ContentSeedRecordServiceTests
{
    private string _db = null!;

    [SetUp]
    public void SetUp()
    {
        _db = TestContexts.NewDb();
    }

    private ContentSeedRecordService NewService()
        => new(TestContexts.Cms(_db));

    private static ContentSeedRecordDTO Record(
        Guid? seedId = null, Guid? nodeId = null, string hash = "abc") => new()
    {
        SeedId = seedId ?? Guid.NewGuid(),
        ContentTypeKey = "pages",
        NodeId = nodeId ?? Guid.NewGuid(),
        ContentHash = hash,
        Source = "a.json",
        AppliedUtc = DateTime.UtcNow,
    };

    [Test]
    public void Constructor_NullContext_Throws()
    {
        Assert.That(() => new ContentSeedRecordService(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task GetAsync_Missing_ReturnsNull()
    {
        var result = await NewService().GetAsync(Guid.NewGuid());

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetAsync_Existing_ReturnsRecord()
    {
        var record = Record();
        await using (var ctx = TestContexts.Cms(_db))
        {
            ctx.Set<ContentSeedRecordDTO>().Add(record);
            await ctx.SaveChangesAsync();
        }

        var result = await NewService().GetAsync(record.SeedId);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.NodeId, Is.EqualTo(record.NodeId));
            Assert.That(result.ContentHash, Is.EqualTo(record.ContentHash));
        });
    }

    [Test]
    public async Task UpsertAsync_New_Inserts()
    {
        var record = Record();
        await NewService().UpsertAsync(record);

        await using var ctx = TestContexts.Cms(_db);
        var stored = await ctx.Set<ContentSeedRecordDTO>().FindAsync(record.SeedId);
        Assert.Multiple(() =>
        {
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored!.NodeId, Is.EqualTo(record.NodeId));
        });
    }

    [Test]
    public async Task UpsertAsync_Existing_Updates()
    {
        var record = Record();
        await NewService().UpsertAsync(record);

        var changed = Record(seedId: record.SeedId, nodeId: record.NodeId, hash: "newhash");
        await NewService().UpsertAsync(changed);

        await using var ctx = TestContexts.Cms(_db);
        var stored = await ctx.Set<ContentSeedRecordDTO>().FindAsync(record.SeedId);
        var count = await ctx.Set<ContentSeedRecordDTO>().CountAsync();
        Assert.Multiple(() =>
        {
            Assert.That(stored!.ContentHash, Is.EqualTo("newhash"));
            Assert.That(count, Is.EqualTo(1));
        });
    }

    [Test]
    public void UpsertAsync_Null_Throws()
    {
        Assert.That(async () => await NewService().UpsertAsync(null!), Throws.ArgumentNullException);
    }
}
