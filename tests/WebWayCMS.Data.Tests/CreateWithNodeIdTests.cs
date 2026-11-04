using Microsoft.EntityFrameworkCore;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class CreateWithNodeIdTests
{
    private string _db = null!;

    [SetUp]
    public void SetUp() => _db = TestContexts.NewDb();

    private static ContentZoneDTO NewZone() => new() { Name = "Z", Version = new ContentVersion { Title = "Z", Node = new ContentNode() } };

    [Test]
    public async Task CreateWithNodeIdAsync_CreatesOnTheGivenNode_AndRefusesASecond()
    {
        var nodeId = Guid.NewGuid();

        var created = await TestStore.Create<ContentZoneDTO>(TestContexts.Cms(_db), "contentzones").CreateWithNodeIdAsync(NewZone(), nodeId);
        var again = await TestStore.Create<ContentZoneDTO>(TestContexts.Cms(_db), "contentzones").CreateWithNodeIdAsync(NewZone(), nodeId);

        var draft = await TestStore.Create<ContentZoneDTO>(TestContexts.Cms(_db), "contentzones").GetCurrentDraftAsync(nodeId);
        Assert.Multiple(() =>
        {
            Assert.That(created.Success, Is.True);
            Assert.That(created.NodeId, Is.EqualTo(nodeId));
            Assert.That(draft!.Version.Node.ContentTypeKey, Is.EqualTo("contentzones"));
            Assert.That(again, Is.EqualTo(new ContentWriteResult(false, ContentStore<ContentZoneDTO>.AlreadyExistsMessage)));
        });
    }

    [Test]
    public void CreateWithNodeIdAsync_InvalidArguments_Throw()
    {
        var store = TestStore.Create<ContentZoneDTO>(TestContexts.Cms(_db), "contentzones");

        Assert.Multiple(() =>
        {
            Assert.That(async () => await store.CreateWithNodeIdAsync(null!, Guid.NewGuid()), Throws.ArgumentNullException);
            Assert.That(async () => await store.CreateWithNodeIdAsync(NewZone(), Guid.Empty), Throws.ArgumentException);
        });
    }

    [Test]
    public async Task CreateWithNodeIdAsync_LosesARaceOnSave_ReportsAlreadyExists_AndLeavesNothingTracked()
    {
        var nodeId = Guid.NewGuid();
        await using var ctx = TestContexts.Cms(_db);
        var store = TestStore.Create<ContentZoneDTO>(ctx, "contentzones");
        // The node is created by someone else after the existence check: here, already tracked by this context.
        ctx.Set<ContentNode>().Attach(new ContentNode { Id = nodeId, ContentTypeKey = "contentzones" });

        var result = await store.CreateWithNodeIdAsync(NewZone(), nodeId);

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorMessage, Is.EqualTo(ContentStore<ContentZoneDTO>.AlreadyExistsMessage));
            Assert.That(ctx.ChangeTracker.Entries<ContentZoneDTO>(), Is.Empty);
        });
    }
}
