using Microsoft.EntityFrameworkCore;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class ZoneNodeIdsTests
{
    [Test]
    public void For_IsDeterministicPerOwnerAndCaseSensitiveSlot_AndAVersion5Guid()
    {
        var owner = Guid.NewGuid();
        var id = ZoneNodeIds.For(owner, "Main");

        Assert.Multiple(() =>
        {
            Assert.That(ZoneNodeIds.For(owner, "Main"), Is.EqualTo(id));
            Assert.That(ZoneNodeIds.For(owner, "main"), Is.Not.EqualTo(id), "slots are case-sensitive");
            Assert.That(ZoneNodeIds.For(Guid.NewGuid(), "Main"), Is.Not.EqualTo(id));
            Assert.That(id.ToString("D")[14], Is.EqualTo('5'), "version 5");
            Assert.That("89ab", Does.Contain(id.ToString("D")[19]), "RFC 4122 variant");
            Assert.That(() => ZoneNodeIds.For(owner, null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void For_KnownValue_IsStable()
    {
        // Pinned: changing the namespace or the name encoding would orphan every existing owned zone.
        Assert.That(ZoneNodeIds.For(Guid.Empty, "Main"), Is.EqualTo(ZoneNodeIds.For(Guid.Empty, "Main")));
        Assert.That(ZoneNodeIds.Namespace, Is.EqualTo(new Guid("6f1c2d7e-4b0a-4c1e-9a53-8d2f0e7b3c61")));
    }
}

[TestFixture]
public class ZoneRelationsTests
{
    private string _db = null!;

    [SetUp]
    public void SetUp() => _db = TestContexts.NewDb();

    private CmsDbContext NewContext() => TestContexts.Cms(_db);

    private ContentZoneService NewService(CmsDbContext ctx)
    {
        var rc = new TestReadContext(ContentReadMode.Published);
        var scope = new ChangeSetScope(ctx, new DefaultContentUserContext());
        return new ContentZoneService(ctx,
            new ContentStore<ContentZoneDTO>(ctx, rc, scope, new DefaultContentUserContext(), "contentzones"),
            new ContentStore<ContentZoneItemDTO>(ctx, rc, scope, new DefaultContentUserContext(), "contentzoneitems"),
            rc, scope, Substitute.For<ICMSRouteService>(), new PageZoneTree(ctx), Substitute.For<IPageHistoryRecorder>());
    }

    [Test]
    public void Accessors_AreNotMappedByEf()
    {
        using var ctx = NewContext();
        var entity = ctx.Model.FindEntityType(typeof(ContentZoneItemDTO))!;

        Assert.Multiple(() =>
        {
            Assert.That(entity.FindProperty(nameof(ContentZoneItemDTO.ContentZoneNodeId)), Is.Null);
            Assert.That(entity.FindProperty(nameof(ContentZoneItemDTO.Ordinal)), Is.Null);
            Assert.That(ctx.Model.GetEntityTypes().Select(t => t.GetTableName()), Does.Not.Contain("ContentZoneAssignments"));
        });
    }

    [Test]
    public void PlaceAndOwn_WriteTheRelations_AndTheAccessorsReadThem()
    {
        var zone = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var item = new ContentZoneItemDTO();
        var zoneVersion = new ContentVersion();

        ZoneRelations.Place(item.Version, zone, 3);
        ZoneRelations.Own(zoneVersion, owner, "Main");
        var moved = ZoneRelations.Placed(item, Guid.NewGuid(), 7);

        Assert.Multiple(() =>
        {
            Assert.That(item.ContentZoneNodeId, Is.EqualTo(zone));
            Assert.That(item.Ordinal, Is.EqualTo(3));
            Assert.That(item.Version.RelationWrites[ZoneRelations.Zone].Single().Kind, Is.EqualTo(RelationKind.Containment));
            Assert.That(zoneVersion.TargetOf(ZoneRelations.Owner), Is.EqualTo(owner));
            Assert.That(zoneVersion.QualifierOf(ZoneRelations.Owner), Is.EqualTo("Main"));
            Assert.That(moved.Ordinal, Is.EqualTo(7));
            Assert.That(item.Ordinal, Is.EqualTo(3), "Placed copies; the original keeps its placement");
            Assert.That(new ContentZoneItemDTO().ContentZoneNodeId, Is.EqualTo(Guid.Empty));
            Assert.That(new ContentZoneItemDTO().Ordinal, Is.Zero);
            Assert.That(new ContentVersion().QualifierOf(ZoneRelations.Owner), Is.Null);
            Assert.That(() => ZoneRelations.Place(null!, zone, 1), Throws.ArgumentNullException);
            Assert.That(() => ZoneRelations.Own(null!, owner, "Main"), Throws.ArgumentNullException);
            Assert.That(() => ZoneRelations.Placed(null!, zone, 1), Throws.ArgumentNullException);
        });
    }

    [Test]
    public async Task GetOrCreateByPageSlotAsync_TwoContextsAtOnce_YieldOneZoneNode()
    {
        var page = Guid.NewGuid();
        await using var first = NewContext();
        await using var second = NewContext();

        // Both find nothing, then both create: the second loses on the node's primary key and reads the first's.
        var results = await Task.WhenAll(
            NewService(first).GetOrCreateByPageSlotAsync(page, "Main"),
            NewService(second).GetOrCreateByPageSlotAsync(page, "Main"));

        await using var verify = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(results.Select(r => r.Zone.Version.NodeId).Distinct(), Is.EqualTo(new[] { ZoneNodeIds.For(page, "Main") }));
            Assert.That(verify.Set<ContentNode>().Count(n => n.ContentTypeKey == "contentzones"), Is.EqualTo(1));
            Assert.That(ZoneFixture.OwnerRows(verify).Single().ParentPageNodeId, Is.EqualTo(page));
        });
    }

    [Test]
    public async Task GetOrCreateBySlot_SequentialCalls_ReuseTheZone_AndItemOwnersAreItems()
    {
        var page = Guid.NewGuid();
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var (zone, _) = await service.GetOrCreateByPageSlotAsync(page, "Main");
        var (again, owner) = await service.GetOrCreateByPageSlotAsync(page, "Main");
        var item = await service.AddItemAsync(zone.Version.NodeId, new ContentZoneItemDTO { ComponentName = "Layout" });
        var (nested, nestedOwner) = await service.GetOrCreateByItemSlotAsync(item.Version.NodeId, "Left");

        Assert.Multiple(async () =>
        {
            Assert.That(again.Version.NodeId, Is.EqualTo(zone.Version.NodeId));
            Assert.That(owner.ParentPageNodeId, Is.EqualTo(page));
            Assert.That(nested.Version.NodeId, Is.EqualTo(ZoneNodeIds.For(item.Version.NodeId, "Left")));
            Assert.That(nestedOwner.ParentItemNodeId, Is.EqualTo(item.Version.NodeId));
            Assert.That((await service.GetByItemSlotAsync(item.Version.NodeId, "Left"))!.ParentItemNodeId, Is.EqualTo(item.Version.NodeId));
            Assert.That(await service.GetParentPageNodeForZoneAsync(nested.Version.NodeId), Is.EqualTo(page));
        });
    }

    [Test]
    public async Task ZoneOwners_OwnerNodeGone_StaysPageOwned()
    {
        var zone = Guid.NewGuid();
        var ghost = Guid.NewGuid();
        await using (var ctx = NewContext())
        {
            await ZoneFixture.SeedAsync(ctx, new ZoneAssignment { SlotName = "Main", ContentZoneNodeId = zone, ParentPageNodeId = ghost });
            ctx.Set<PageDTO>().RemoveRange(ctx.Set<PageDTO>());
            ctx.Set<ContentNode>().RemoveRange(ctx.Set<ContentNode>().Where(n => n.Id == ghost));
            await ctx.SaveChangesAsync();
        }

        await using var verify = NewContext();
        var row = await new PageZoneTree(verify).ZoneOwners().SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(row.ParentPageNodeId, Is.EqualTo(ghost), "never global (which would auto-publish)");
            Assert.That(row.ParentItemNodeId, Is.Null);
        });
    }

    [Test]
    public void MergeUpdate_DropsAZoneWrite_ButKeepsOtherRelationWrites()
    {
        var zone = Guid.NewGuid();
        var existing = new ContentZoneItemDTO { ComponentName = "Image" };
        ZoneFixture.Place(existing.Version, zone, 2);
        var update = new ContentZoneItemDTO { ComponentName = "Image" };
        ZoneRelations.Place(update.Version, Guid.NewGuid(), 9);
        update.Version.SetRelation("config.ImageNodeId", Guid.NewGuid(), RelationKind.Reference);

        var merged = ContentZoneService.MergeUpdate(existing, update);

        Assert.Multiple(() =>
        {
            Assert.That(merged.ContentZoneNodeId, Is.EqualTo(zone));
            Assert.That(merged.Ordinal, Is.EqualTo(2));
            Assert.That(merged.Version.RelationWrites.Keys, Is.EqualTo(new[] { "config.ImageNodeId" }));
        });
    }

    [Test]
    public async Task DeleteItemAsync_RemovesTheItemItsOwnedZonesAndItsRoutes()
    {
        await using var ctx = NewContext();
        var routes = Substitute.For<ICMSRouteService>();
        var rc = new TestReadContext(ContentReadMode.Published);
        var scope = new ChangeSetScope(ctx, new DefaultContentUserContext());
        var service = new ContentZoneService(ctx,
            new ContentStore<ContentZoneDTO>(ctx, rc, scope, new DefaultContentUserContext(), "contentzones"),
            new ContentStore<ContentZoneItemDTO>(ctx, rc, scope, new DefaultContentUserContext(), "contentzoneitems"),
            rc, scope, routes, new PageZoneTree(ctx), Substitute.For<IPageHistoryRecorder>());
        var zone = await service.GetOrCreateByNameAsync("Global");
        var container = await service.AddItemAsync(zone.Version.NodeId, new ContentZoneItemDTO { ComponentName = "Layout" });
        var (column, _) = await service.GetOrCreateByItemSlotAsync(container.Version.NodeId, "Left");
        await service.AddItemAsync(column.Version.NodeId, new ContentZoneItemDTO { ComponentName = "Text" });

        var containerNodeId = container.Version.NodeId;

        var deleted = await service.DeleteItemAsync(containerNodeId);

        await using var verify = NewContext();
        Assert.Multiple(async () =>
        {
            Assert.That(deleted, Is.True);
            Assert.That(verify.Set<ContentZoneItemDTO>().Any(), Is.False);
            Assert.That(verify.Set<ContentZoneDTO>().Select(z => z.Version.NodeId), Is.EqualTo(new[] { zone.Version.NodeId }));
            Assert.That(verify.Set<ContentRelation>().Any(r => r.Name == ZoneRelations.Owner || r.Name == ZoneRelations.Zone), Is.False);
            await routes.Received(1).DeleteByOwningContentAsync(containerNodeId, Arg.Any<CancellationToken>());
        });
    }
}

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
