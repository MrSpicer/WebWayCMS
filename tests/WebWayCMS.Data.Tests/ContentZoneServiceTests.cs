using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class ContentZoneServiceTests
{
    private string _db = null!;

    [SetUp]
    public void SetUp() => _db = TestContexts.NewDb();

    private CmsDbContext NewContext() => TestContexts.Cms(_db);

    private ContentZoneService NewService(
        CmsDbContext? ctx = null,
        IContentStore<ContentZoneDTO>? zoneStore = null,
        IContentStore<ContentZoneItemDTO>? itemStore = null,
        IContentReadContext? readContext = null)
    {
        var context = ctx ?? NewContext();
        var rc = readContext ?? new TestReadContext(ContentReadMode.Published);
        var scope = new ChangeSetScope(context, new DefaultContentUserContext());
        zoneStore ??= new ContentStore<ContentZoneDTO>(context, rc, scope, new DefaultContentUserContext(), "contentzones");
        itemStore ??= new ContentStore<ContentZoneItemDTO>(context, rc, scope, new DefaultContentUserContext(), "contentzoneitems");
        return new ContentZoneService(context, zoneStore, itemStore, rc, scope);
    }

    private static ContentZoneDTO Zone(Guid? nodeId = null, int version = 0, string name = "Zone",
        ContentVersionState state = ContentVersionState.Published, bool deleted = false, bool currentDraft = true)
    {
        var n = nodeId ?? Guid.NewGuid();
        var versionId = Guid.NewGuid();
        return new ContentZoneDTO
        {
            VersionId = versionId,
            Name = name,
            Version = new ContentVersion
            {
                Id = versionId,
                NodeId = n,
                Node = new ContentNode { Id = n, ContentTypeKey = "contentzones", IsDeleted = deleted },
                VersionNumber = version,
                State = state,
                IsCurrentDraft = currentDraft,
                Title = name
            }
        };
    }

    private static ContentZoneItemDTO Item(Guid zoneNodeId, Guid? itemNodeId = null, int version = 0, int ordinal = 0,
        bool active = true, string component = "ContentBlock",
        ContentVersionState state = ContentVersionState.Published, bool currentDraft = true)
    {
        var n = itemNodeId ?? Guid.NewGuid();
        var versionId = Guid.NewGuid();
        return new ContentZoneItemDTO
        {
            VersionId = versionId,
            ContentZoneNodeId = zoneNodeId,
            Ordinal = ordinal,
            IsActive = active,
            ComponentName = component,
            Version = new ContentVersion
            {
                Id = versionId,
                NodeId = n,
                Node = new ContentNode { Id = n, ContentTypeKey = "contentzoneitems" },
                VersionNumber = version,
                State = state,
                IsCurrentDraft = currentDraft,
                Title = component
            }
        };
    }

    private async Task SeedZonesAsync(params ContentZoneDTO[] zones)
    {
        await using var ctx = NewContext();
        ctx.Set<ContentZoneDTO>().AddRange(zones);
        await ctx.SaveChangesAsync();
    }

    private async Task SeedItemsAsync(params ContentZoneItemDTO[] items)
    {
        await using var ctx = NewContext();
        ctx.Set<ContentZoneItemDTO>().AddRange(items);
        await ctx.SaveChangesAsync();
    }

    private async Task SeedAssignmentsAsync(params ContentZoneAssignmentDTO[] assignments)
    {
        await using var ctx = NewContext();
        ctx.Set<ContentZoneAssignmentDTO>().AddRange(assignments);
        await ctx.SaveChangesAsync();
    }

    [Test]
    public void Constructor_NullArgs_Throws()
    {
        var ctx = NewContext();
        var readContext = new TestReadContext(ContentReadMode.Published);
        var scope = new ChangeSetScope(ctx, new DefaultContentUserContext());
        var zoneStore = new ContentStore<ContentZoneDTO>(ctx, readContext, scope, new DefaultContentUserContext(), "contentzones");
        var itemStore = new ContentStore<ContentZoneItemDTO>(ctx, readContext, scope, new DefaultContentUserContext(), "contentzoneitems");

        Assert.Multiple(() =>
        {
            Assert.That(() => new ContentZoneService(null!, zoneStore, itemStore, readContext, scope), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneService(ctx, null!, itemStore, readContext, scope), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneService(ctx, zoneStore, null!, readContext, scope), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneService(ctx, zoneStore, itemStore, null!, scope), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneService(ctx, zoneStore, itemStore, readContext, null!), Throws.ArgumentNullException);
        });
    }

    // ─── item resolution ──────────────────────────────────────────────────────

    [Test]
    public async Task GetItemsAsync_ReturnsActiveItemsOrdered()
    {
        var zoneNodeId = Guid.NewGuid();
        await SeedItemsAsync(
            Item(zoneNodeId, Guid.NewGuid(), ordinal: 2),
            Item(zoneNodeId, Guid.NewGuid(), ordinal: 1),
            Item(zoneNodeId, Guid.NewGuid(), ordinal: 3, active: false));

        var items = await NewService().GetItemsAsync(zoneNodeId);

        Assert.That(items.Select(i => i.Ordinal), Is.EqualTo(new[] { 1, 2 }));
    }

    // ─── zone reads ───────────────────────────────────────────────────────────

    [Test]
    public async Task GetZoneByNodeAsync_FoundAndNotFound()
    {
        var zone = Zone(Guid.NewGuid(), 0, "Main");
        await SeedZonesAsync(zone);

        Assert.Multiple(async () =>
        {
            Assert.That(await NewService().GetZoneByNodeAsync(zone.Version.Node.Id), Is.Not.Null);
            Assert.That(await NewService().GetZoneByNodeAsync(Guid.NewGuid()), Is.Null);
        });
    }

    [Test]
    public async Task GetZoneByNameAsync_Whitespace_ReturnsNull()
    {
        Assert.That(await NewService().GetZoneByNameAsync(" "), Is.Null);
    }

    [Test]
    public async Task GetZoneByNameAsync_ReturnsZone()
    {
        await SeedZonesAsync(Zone(Guid.NewGuid(), 0, "Main"));

        var result = await NewService().GetZoneByNameAsync("Main");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("Main"));
    }

    // ─── page-slot assignment ─────────────────────────────────────────────────

    [Test]
    public async Task GetByPageSlotAsync_FoundAndNotFound()
    {
        var pageNodeId = Guid.NewGuid();
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Main", ContentZoneNodeId = Guid.NewGuid(), ParentPageNodeId = pageNodeId });

        Assert.Multiple(async () =>
        {
            Assert.That(await NewService().GetByPageSlotAsync(pageNodeId, "Main"), Is.Not.Null);
            Assert.That(await NewService().GetByPageSlotAsync(pageNodeId, "Other"), Is.Null);
        });
    }

    [Test]
    public async Task GetOrCreateByPageSlotAsync_NoAssignment_CreatesZoneAndAssignment()
    {
        var pageNodeId = Guid.NewGuid();

        var (zone, assignment) = await NewService().GetOrCreateByPageSlotAsync(pageNodeId, "Main");

        Assert.Multiple(() =>
        {
            Assert.That(zone.Name, Is.EqualTo("Main"));
            Assert.That(assignment.ParentPageNodeId, Is.EqualTo(pageNodeId));
            Assert.That(assignment.ContentZoneNodeId, Is.EqualTo(zone.Version.Node.Id));
            Assert.That(assignment.ContentZoneNode, Is.SameAs(zone.Version.Node));
        });
    }

    [Test]
    public async Task GetOrCreateByPageSlotAsync_ExistingAssignmentAndZone_ReturnsExisting()
    {
        var pageNodeId = Guid.NewGuid();
        var zoneNodeId = Guid.NewGuid();
        await SeedZonesAsync(Zone(zoneNodeId, 0, "Main"));
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Main", ContentZoneNodeId = zoneNodeId, ParentPageNodeId = pageNodeId });

        var (zone, assignment) = await NewService().GetOrCreateByPageSlotAsync(pageNodeId, "Main");

        Assert.Multiple(() =>
        {
            Assert.That(zone.Version.Node.Id, Is.EqualTo(zoneNodeId));
            Assert.That(assignment.ParentPageNodeId, Is.EqualTo(pageNodeId));
        });
    }

    [Test]
    public async Task GetOrCreateByPageSlotAsync_AssignmentButZoneDeleted_ReturnsTransientZone()
    {
        var pageNodeId = Guid.NewGuid();
        var zoneNodeId = Guid.NewGuid();
        await SeedZonesAsync(Zone(zoneNodeId, 0, "Main", deleted: true));
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Main", ContentZoneNodeId = zoneNodeId, ParentPageNodeId = pageNodeId });

        var (zone, assignment) = await NewService().GetOrCreateByPageSlotAsync(pageNodeId, "Main");

        Assert.Multiple(() =>
        {
            Assert.That(assignment, Is.Not.Null);
            Assert.That(zone.Name, Is.EqualTo("Main"));
        });
    }

    [Test]
    public async Task GetOrCreateByPageSlotAsync_ZoneBecomesAvailableInTransaction_ReturnsZone()
    {
        var ctx = NewContext();
        var pageNodeId = Guid.NewGuid();
        var zoneNodeId = Guid.NewGuid();
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Main", ContentZoneNodeId = zoneNodeId, ParentPageNodeId = pageNodeId });

        var zone = Zone(zoneNodeId, 0, "Main");
        var zoneStore = Substitute.For<IContentStore<ContentZoneDTO>>();
        zoneStore.GetAsync(zoneNodeId, Arg.Any<CancellationToken>()).Returns((ContentZoneDTO?)null, zone);

        var service = NewService(ctx, zoneStore: zoneStore);

        var (result, assignment) = await service.GetOrCreateByPageSlotAsync(pageNodeId, "Main");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.SameAs(zone));
            Assert.That(assignment.ContentZoneNodeId, Is.EqualTo(zoneNodeId));
        });
    }

    [Test]
    public async Task GetOrCreateByPageSlotAsync_CreateFails_Throws()
    {
        var ctx = NewContext();
        var zoneStore = Substitute.For<IContentStore<ContentZoneDTO>>();
        zoneStore.SaveDraftAsync(Arg.Any<ContentZoneDTO>(), null, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ContentWriteResult>(new InvalidOperationException("boom")));

        var service = NewService(ctx, zoneStore: zoneStore);

        Assert.That(async () => await service.GetOrCreateByPageSlotAsync(Guid.NewGuid(), "Main"),
            Throws.InvalidOperationException.With.Message.EqualTo("boom"));
    }

    // ─── zone-slot assignment ─────────────────────────────────────────────────

    [Test]
    public async Task GetByZoneSlotAsync_FoundAndNotFound()
    {
        var parentZone = Guid.NewGuid();
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Sub", ContentZoneNodeId = Guid.NewGuid(), ParentZoneNodeId = parentZone });

        Assert.Multiple(async () =>
        {
            Assert.That(await NewService().GetByZoneSlotAsync(parentZone, "Sub"), Is.Not.Null);
            Assert.That(await NewService().GetByZoneSlotAsync(parentZone, "Other"), Is.Null);
        });
    }

    [Test]
    public async Task GetOrCreateByZoneSlotAsync_NoAssignment_Creates()
    {
        var parentZone = Guid.NewGuid();

        var (zone, assignment) = await NewService().GetOrCreateByZoneSlotAsync(parentZone, "Sub");

        Assert.Multiple(() =>
        {
            Assert.That(zone.Name, Is.EqualTo("Sub"));
            Assert.That(assignment.ParentZoneNodeId, Is.EqualTo(parentZone));
            Assert.That(assignment.ContentZoneNode, Is.SameAs(zone.Version.Node));
        });
    }

    [Test]
    public async Task GetOrCreateByZoneSlotAsync_ExistingAssignmentAndZone_ReturnsExisting()
    {
        var parentZone = Guid.NewGuid();
        var zoneNodeId = Guid.NewGuid();
        await SeedZonesAsync(Zone(zoneNodeId, 0, "Sub"));
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Sub", ContentZoneNodeId = zoneNodeId, ParentZoneNodeId = parentZone });

        var (zone, _) = await NewService().GetOrCreateByZoneSlotAsync(parentZone, "Sub");

        Assert.That(zone.Version.Node.Id, Is.EqualTo(zoneNodeId));
    }

    [Test]
    public async Task GetOrCreateByZoneSlotAsync_AssignmentButZoneDeleted_ReturnsTransientZone()
    {
        var parentZone = Guid.NewGuid();
        var zoneNodeId = Guid.NewGuid();
        await SeedZonesAsync(Zone(zoneNodeId, 0, "Sub", deleted: true));
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Sub", ContentZoneNodeId = zoneNodeId, ParentZoneNodeId = parentZone });

        var (zone, assignment) = await NewService().GetOrCreateByZoneSlotAsync(parentZone, "Sub");

        Assert.Multiple(() =>
        {
            Assert.That(assignment, Is.Not.Null);
            Assert.That(zone.Name, Is.EqualTo("Sub"));
        });
    }

    [Test]
    public async Task GetOrCreateByZoneSlotAsync_ZoneBecomesAvailableInTransaction_ReturnsZone()
    {
        var ctx = NewContext();
        var parentZone = Guid.NewGuid();
        var zoneNodeId = Guid.NewGuid();
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Sub", ContentZoneNodeId = zoneNodeId, ParentZoneNodeId = parentZone });

        var zone = Zone(zoneNodeId, 0, "Sub");
        var zoneStore = Substitute.For<IContentStore<ContentZoneDTO>>();
        zoneStore.GetAsync(zoneNodeId, Arg.Any<CancellationToken>()).Returns((ContentZoneDTO?)null, zone);

        var service = NewService(ctx, zoneStore: zoneStore);

        var (result, _) = await service.GetOrCreateByZoneSlotAsync(parentZone, "Sub");

        Assert.That(result, Is.SameAs(zone));
    }

    [Test]
    public async Task GetOrCreateByZoneSlotAsync_CreateFails_Throws()
    {
        var ctx = NewContext();
        var zoneStore = Substitute.For<IContentStore<ContentZoneDTO>>();
        zoneStore.SaveDraftAsync(Arg.Any<ContentZoneDTO>(), null, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ContentWriteResult>(new InvalidOperationException("boom")));

        var service = NewService(ctx, zoneStore: zoneStore);

        Assert.That(async () => await service.GetOrCreateByZoneSlotAsync(Guid.NewGuid(), "Sub"),
            Throws.InvalidOperationException.With.Message.EqualTo("boom"));
    }

    // ─── GetOrCreateByName ────────────────────────────────────────────────────

    [Test]
    public async Task GetOrCreateByNameAsync_Existing_ReturnsIt()
    {
        await SeedZonesAsync(Zone(Guid.NewGuid(), 0, "Global"));

        var zone = await NewService().GetOrCreateByNameAsync("Global");

        Assert.That(zone.Name, Is.EqualTo("Global"));
    }

    [Test]
    public async Task GetOrCreateByNameAsync_Missing_Creates()
    {
        var zone = await NewService().GetOrCreateByNameAsync("Fresh");

        Assert.Multiple(() =>
        {
            Assert.That(zone.Name, Is.EqualTo("Fresh"));
            Assert.That(zone.Version.State, Is.EqualTo(ContentVersionState.Published));
        });
    }

    [Test]
    public async Task GetOrCreateByNameAsync_CreateFails_Throws()
    {
        var ctx = NewContext();
        var zoneStore = Substitute.For<IContentStore<ContentZoneDTO>>();
        zoneStore.SaveDraftAsync(Arg.Any<ContentZoneDTO>(), null, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ContentWriteResult>(new InvalidOperationException("boom")));

        var service = NewService(ctx, zoneStore: zoneStore);

        Assert.That(async () => await service.GetOrCreateByNameAsync("Fresh"),
            Throws.InvalidOperationException.With.Message.EqualTo("boom"));
    }

    // ─── queries by page / parent / counts ────────────────────────────────────

    [Test]
    public async Task GetAllAssignmentsForPageAsync_ReturnsPageAssignments()
    {
        var pageNodeId = Guid.NewGuid();
        await SeedAssignmentsAsync(
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "A", ContentZoneNodeId = Guid.NewGuid(), ParentPageNodeId = pageNodeId },
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "B", ContentZoneNodeId = Guid.NewGuid(), ParentPageNodeId = Guid.NewGuid() });

        var result = await NewService().GetAllAssignmentsForPageAsync(pageNodeId);

        Assert.That(result.Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task GetAllByPageAsync_ReturnsAssignedZones()
    {
        var pageNodeId = Guid.NewGuid();
        var zoneNodeId = Guid.NewGuid();
        await SeedZonesAsync(Zone(zoneNodeId, 0, "Main"));
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Main", ContentZoneNodeId = zoneNodeId, ParentPageNodeId = pageNodeId });

        var zones = await NewService().GetAllByPageAsync(pageNodeId);

        Assert.That(zones.Select(z => z.Version.Node.Id), Is.EqualTo(new[] { zoneNodeId }));
    }

    [Test]
    public async Task GetAllByParentZoneAsync_ReturnsAssignedZones()
    {
        var parentZone = Guid.NewGuid();
        var zoneNodeId = Guid.NewGuid();
        await SeedZonesAsync(Zone(zoneNodeId, 0, "Sub"));
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Sub", ContentZoneNodeId = zoneNodeId, ParentZoneNodeId = parentZone });

        var zones = await NewService().GetAllByParentZoneAsync(parentZone);

        Assert.That(zones.Select(z => z.Version.Node.Id), Is.EqualTo(new[] { zoneNodeId }));
    }

    [Test]
    public async Task GetZoneNodeIdsWithChildrenAsync_EmptyInput_ReturnsEmpty()
    {
        Assert.That(await NewService().GetZoneNodeIdsWithChildrenAsync(Array.Empty<Guid>()), Is.Empty);
    }

    [Test]
    public async Task GetZoneNodeIdsWithChildrenAsync_ReturnsParentsWithChildren()
    {
        var parentWithChild = Guid.NewGuid();
        var parentWithout = Guid.NewGuid();
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Sub", ContentZoneNodeId = Guid.NewGuid(), ParentZoneNodeId = parentWithChild });

        var result = await NewService().GetZoneNodeIdsWithChildrenAsync(new[] { parentWithChild, parentWithout });

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain(parentWithChild));
            Assert.That(result, Does.Not.Contain(parentWithout));
        });
    }

    [Test]
    public async Task GetAssignmentCountsByNodeIdAsync_EmptyInput_ReturnsEmpty()
    {
        Assert.That(await NewService().GetAssignmentCountsByNodeIdAsync(Array.Empty<Guid>()), Is.Empty);
    }

    [Test]
    public async Task GetAssignmentCountsByNodeIdAsync_CountsPerZone()
    {
        var zoneNodeId = Guid.NewGuid();
        await SeedAssignmentsAsync(
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "A", ContentZoneNodeId = zoneNodeId, ParentPageNodeId = Guid.NewGuid() },
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "B", ContentZoneNodeId = zoneNodeId, ParentPageNodeId = Guid.NewGuid() });

        var counts = await NewService().GetAssignmentCountsByNodeIdAsync(new[] { zoneNodeId });

        Assert.That(counts[zoneNodeId], Is.EqualTo(2));
    }

    // ─── parent resolution ────────────────────────────────────────────────────

    [Test]
    public async Task GetParentPageNodeForZoneAsync_DirectPage_ReturnsPage()
    {
        var pageNodeId = Guid.NewGuid();
        var zoneNodeId = Guid.NewGuid();
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Main", ContentZoneNodeId = zoneNodeId, ParentPageNodeId = pageNodeId });

        var result = await NewService().GetParentPageNodeForZoneAsync(zoneNodeId);

        Assert.That(result, Is.EqualTo(pageNodeId));
    }

    [Test]
    public async Task GetParentPageNodeForZoneAsync_NestedZone_ReturnsPage()
    {
        var pageNodeId = Guid.NewGuid();
        var childZone = Guid.NewGuid();
        var parentZone = Guid.NewGuid();
        await SeedAssignmentsAsync(
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Sub", ContentZoneNodeId = childZone, ParentZoneNodeId = parentZone },
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Main", ContentZoneNodeId = parentZone, ParentPageNodeId = pageNodeId });

        var result = await NewService().GetParentPageNodeForZoneAsync(childZone);

        Assert.That(result, Is.EqualTo(pageNodeId));
    }

    [Test]
    public async Task GetParentPageNodeForZoneAsync_NoAssignment_ReturnsNull()
    {
        Assert.That(await NewService().GetParentPageNodeForZoneAsync(Guid.NewGuid()), Is.Null);
    }

    [Test]
    public async Task GetParentPageNodeForZoneAsync_Cycle_ReturnsNull()
    {
        var zoneA = Guid.NewGuid();
        var zoneB = Guid.NewGuid();
        await SeedAssignmentsAsync(
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "A", ContentZoneNodeId = zoneA, ParentZoneNodeId = zoneB },
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "B", ContentZoneNodeId = zoneB, ParentZoneNodeId = zoneA });

        var result = await NewService().GetParentPageNodeForZoneAsync(zoneA);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetParentPageNodeForZoneAsync_OrphanZone_ReturnsNull()
    {
        var zoneNodeId = Guid.NewGuid();
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "X", ContentZoneNodeId = zoneNodeId });

        var result = await NewService().GetParentPageNodeForZoneAsync(zoneNodeId);

        Assert.That(result, Is.Null);
    }

    // ─── item writes ──────────────────────────────────────────────────────────

    [Test]
    public async Task GetItemByNodeIdAsync_FoundAndNotFound()
    {
        var item = Item(Guid.NewGuid(), Guid.NewGuid());
        await SeedItemsAsync(item);

        Assert.Multiple(async () =>
        {
            Assert.That(await NewService().GetItemByNodeIdAsync(item.Version.Node.Id), Is.Not.Null);
            Assert.That(await NewService().GetItemByNodeIdAsync(Guid.NewGuid()), Is.Null);
        });
    }

    [Test]
    public void AddItemAsync_Null_Throws()
    {
        Assert.That(async () => await NewService().AddItemAsync(Guid.NewGuid(), null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task AddItemAsync_ZoneNotFound_Throws()
    {
        Assert.That(async () => await NewService().AddItemAsync(Guid.NewGuid(), new ContentZoneItemDTO { ComponentName = "X" }),
            Throws.InvalidOperationException);
    }

    [Test]
    public async Task AddItemAsync_AutoAssignsNodeAndOrdinal()
    {
        var zoneNodeId = Guid.NewGuid();
        await SeedZonesAsync(Zone(zoneNodeId, 0, "Main"));
        await SeedItemsAsync(Item(zoneNodeId, Guid.NewGuid(), ordinal: 5));

        var item = await NewService().AddItemAsync(zoneNodeId, new ContentZoneItemDTO { ComponentName = "ContentBlock" });

        Assert.Multiple(() =>
        {
            Assert.That(item.Version.Node.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(item.ContentZoneNodeId, Is.EqualTo(zoneNodeId));
            Assert.That(item.Ordinal, Is.EqualTo(6));
            Assert.That(item.Version.State, Is.EqualTo(ContentVersionState.Published));
        });
    }

    [Test]
    public async Task AddItemAsync_PresetOrdinal_IsKept()
    {
        var zoneNodeId = Guid.NewGuid();
        await SeedZonesAsync(Zone(zoneNodeId, 0, "Main"));

        var item = await NewService().AddItemAsync(zoneNodeId, new ContentZoneItemDTO { ComponentName = "X", Ordinal = 9 });

        Assert.That(item.Ordinal, Is.EqualTo(9));
    }

    [Test]
    public async Task AddItemAsync_SaveFails_Throws()
    {
        var ctx = NewContext();
        var zoneNodeId = Guid.NewGuid();
        await SeedZonesAsync(Zone(zoneNodeId, 0, "Zone"));

        var itemStore = Substitute.For<IContentStore<ContentZoneItemDTO>>();
        itemStore.SaveDraftAsync(Arg.Any<ContentZoneItemDTO>(), null, Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(false, "boom"));

        var service = NewService(ctx, itemStore: itemStore);

        Assert.That(async () => await service.AddItemAsync(zoneNodeId, new ContentZoneItemDTO { ComponentName = "X" }),
            Throws.InvalidOperationException.With.Message.Contains("boom"));
    }

    [Test]
    public void UpdateItemAsync_Null_Throws()
    {
        Assert.That(async () => await NewService().UpdateItemAsync(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task UpdateItemAsync_NoNode_ReturnsFalse()
    {
        Assert.That(await NewService().UpdateItemAsync(new ContentZoneItemDTO { ComponentName = "X" }), Is.False);
    }

    [Test]
    public async Task UpdateItemAsync_NonExistent_ReturnsFalse()
    {
        Assert.That(await NewService().UpdateItemAsync(new ContentZoneItemDTO
        {
            ComponentName = "X",
            Version = new ContentVersion { Node = new ContentNode { Id = Guid.NewGuid() } }
        }), Is.False);
    }

    [Test]
    public async Task UpdateItemAsync_Existing_UpdatesAndPublishes()
    {
        var zoneNodeId = Guid.NewGuid();
        var itemNodeId = Guid.NewGuid();
        await SeedItemsAsync(Item(zoneNodeId, itemNodeId, 0, ordinal: 1, component: "Old"));

        var ok = await NewService().UpdateItemAsync(new ContentZoneItemDTO
        {
            ComponentName = "New",
            IsActive = true,
            Version = new ContentVersion { Node = new ContentNode { Id = itemNodeId } }
        });

        var ctx = NewContext();
        var store = TestStore.Create<ContentZoneItemDTO>(ctx, "contentzoneitems");
        var published = await store.GetAsync(itemNodeId);
        var versions = await store.GetAllVersionsAsync(itemNodeId);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(published!.ComponentName, Is.EqualTo("New"));
            Assert.That(published.Ordinal, Is.EqualTo(1), "ordinal preserved from existing");
            Assert.That(versions, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task UpdateItemAsync_SaveFails_ReturnsFalse()
    {
        var ctx = NewContext();
        var itemNodeId = Guid.NewGuid();
        var existing = Item(Guid.NewGuid(), itemNodeId, 0);

        var itemStore = Substitute.For<IContentStore<ContentZoneItemDTO>>();
        itemStore.GetCurrentDraftAsync(itemNodeId, Arg.Any<CancellationToken>()).Returns(existing);
        itemStore.SaveDraftAsync(Arg.Any<ContentZoneItemDTO>(), null, Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(false, "boom"));

        var service = NewService(ctx, itemStore: itemStore);

        var ok = await service.UpdateItemAsync(new ContentZoneItemDTO
        {
            ComponentName = "New",
            IsActive = true,
            Version = new ContentVersion { Node = new ContentNode { Id = itemNodeId } }
        });

        Assert.That(ok, Is.False);
    }

    [Test]
    public async Task RemoveItemAsync_NotFoundAndFound()
    {
        var item = Item(Guid.NewGuid(), Guid.NewGuid());
        await SeedItemsAsync(item);

        Assert.Multiple(async () =>
        {
            Assert.That(await NewService().RemoveItemAsync(Guid.NewGuid()), Is.False);
            Assert.That(await NewService().RemoveItemAsync(item.Version.Node.Id), Is.True);
        });
    }

    [Test]
    public async Task ReorderItemsAsync_AssignsSequentialOrdinals_IgnoringUnknownIds()
    {
        var zoneNodeId = Guid.NewGuid();
        var aNodeId = Guid.NewGuid();
        var bNodeId = Guid.NewGuid();
        await SeedItemsAsync(
            Item(zoneNodeId, aNodeId, ordinal: 1),
            Item(zoneNodeId, bNodeId, ordinal: 2));

        var ok = await NewService().ReorderItemsAsync(zoneNodeId, new List<Guid> { bNodeId, aNodeId, Guid.NewGuid() });

        var ctx = NewContext();
        var store = TestStore.Create<ContentZoneItemDTO>(ctx, "contentzoneitems");
        var a = await store.GetAsync(aNodeId);
        var b = await store.GetAsync(bNodeId);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(b!.Ordinal, Is.EqualTo(1));
            Assert.That(a!.Ordinal, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task ReorderItemsAsync_SaveFails_SkipsPublish()
    {
        var ctx = NewContext();
        var zoneNodeId = Guid.NewGuid();
        var itemNodeId = Guid.NewGuid();

        var itemStore = Substitute.For<IContentStore<ContentZoneItemDTO>>();
        itemStore.GetCurrentDraftAsync(itemNodeId, Arg.Any<CancellationToken>()).Returns(Item(zoneNodeId, itemNodeId, 0, ordinal: 1));
        itemStore.SaveDraftAsync(Arg.Any<ContentZoneItemDTO>(), null, Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(false, "boom"));

        var service = NewService(ctx, itemStore: itemStore);

        var ok = await service.ReorderItemsAsync(zoneNodeId, new List<Guid> { itemNodeId });

        Assert.That(ok, Is.True);
        await itemStore.DidNotReceive().PublishAsync(itemNodeId, Arg.Any<CancellationToken>());
    }

    // ─── zone deletion ────────────────────────────────────────────────────────

    [Test]
    public async Task DeleteZoneAsync_DeletesZoneItemsAndAssignments()
    {
        var zoneNodeId = Guid.NewGuid();
        var itemNodeId = Guid.NewGuid();
        await SeedZonesAsync(Zone(zoneNodeId, 0, "Main"));
        await SeedItemsAsync(Item(zoneNodeId, itemNodeId, 0));
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Main", ContentZoneNodeId = zoneNodeId, ParentPageNodeId = Guid.NewGuid() });

        var ok = await NewService().DeleteZoneAsync(zoneNodeId);

        await using var verify = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(verify.Set<ContentZoneDTO>().Any(), Is.False);
            Assert.That(verify.Set<ContentZoneItemDTO>().Any(), Is.False);
            Assert.That(verify.Set<ContentZoneAssignmentDTO>().Any(), Is.False);
        });
    }

    // ─── ChangeSetScope / ContentStore edge-case coverage ─────────────────────

    [Test]
    public void ChangeSetScope_Constructor_NullArgs_Throws()
    {
        var ctx = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(() => new ChangeSetScope(null!, new DefaultContentUserContext()), Throws.ArgumentNullException);
            Assert.That(() => new ChangeSetScope(ctx, null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void ChangeSetScope_CurrentUserId_ReflectsAmbientScope()
    {
        var ctx = NewContext();
        var userId = Guid.NewGuid();
        var userContext = Substitute.For<IContentUserContext>();
        userContext.CurrentUserId.Returns(userId);

        var scope = new ChangeSetScope(ctx, userContext);

        Assert.That(scope.CurrentUserId, Is.Null);

        using (scope.Begin(ChangeSetKind.Save, Guid.NewGuid(), null))
        {
            Assert.That(scope.CurrentUserId, Is.EqualTo(userId));
        }
    }

    [Test]
    public void ChangeSetScope_NonLifoDispose_SkipsDetach()
    {
        var ctx = NewContext();
        var scope = new ChangeSetScope(ctx, new DefaultContentUserContext());

        var outer = scope.Begin(ChangeSetKind.Save, null, null);
        var inner = scope.Begin(ChangeSetKind.Save, null, null);

        outer.Dispose();
        inner.Dispose();

        Assert.Pass();
    }

    [Test]
    public async Task ContentStore_SaveDraft_NullTitle_CoalescesToEmpty()
    {
        var ctx = NewContext();
        var store = TestStore.Create<ContentZoneDTO>(ctx, "contentzones");

        var dto = new ContentZoneDTO
        {
            Name = "Z",
            Version = new ContentVersion { Title = null! }
        };

        var result = await store.SaveDraftAsync(dto, null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(dto.Version.Title, Is.EqualTo(string.Empty));
        });
    }
}
