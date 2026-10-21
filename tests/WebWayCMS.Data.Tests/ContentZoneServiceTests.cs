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
        IContentReadContext? readContext = null,
        ICMSRouteService? routeService = null)
    {
        var context = ctx ?? NewContext();
        var rc = readContext ?? new TestReadContext(ContentReadMode.Published);
        var scope = new ChangeSetScope(context, new DefaultContentUserContext());
        zoneStore ??= new ContentStore<ContentZoneDTO>(context, rc, scope, new DefaultContentUserContext(), "contentzones");
        itemStore ??= new ContentStore<ContentZoneItemDTO>(context, rc, scope, new DefaultContentUserContext(), "contentzoneitems");
        routeService ??= Substitute.For<ICMSRouteService>();
        return new ContentZoneService(context, zoneStore, itemStore, rc, scope, routeService);
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
        var routeService = Substitute.For<ICMSRouteService>();

        Assert.Multiple(() =>
        {
            Assert.That(() => new ContentZoneService(null!, zoneStore, itemStore, readContext, scope, routeService), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneService(ctx, null!, itemStore, readContext, scope, routeService), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneService(ctx, zoneStore, null!, readContext, scope, routeService), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneService(ctx, zoneStore, itemStore, null!, scope, routeService), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneService(ctx, zoneStore, itemStore, readContext, null!, routeService), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneService(ctx, zoneStore, itemStore, readContext, scope, null!), Throws.ArgumentNullException);
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
    public async Task GetOrCreateByPageSlotAsync_UnpublishedZone_FoundViaDraftLookup()
    {
        var pageNodeId = Guid.NewGuid();
        var zoneNodeId = Guid.NewGuid();
        await SeedZonesAsync(Zone(zoneNodeId, 0, "Main", state: ContentVersionState.Draft));
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Main", ContentZoneNodeId = zoneNodeId, ParentPageNodeId = pageNodeId });

        var (zone, assignment) = await NewService().GetOrCreateByPageSlotAsync(pageNodeId, "Main");

        Assert.Multiple(() =>
        {
            Assert.That(zone.Version.Node.Id, Is.EqualTo(zoneNodeId));
            Assert.That(assignment.ContentZoneNodeId, Is.EqualTo(zoneNodeId));
        });
    }

    [Test]
    public async Task GetOrCreateByPageSlotAsync_DanglingAssignment_RepairsAndRepoints()
    {
        var pageNodeId = Guid.NewGuid();
        var zoneNodeId = Guid.NewGuid();
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Main", ContentZoneNodeId = zoneNodeId, ParentPageNodeId = pageNodeId });

        var (zone, assignment) = await NewService().GetOrCreateByPageSlotAsync(pageNodeId, "Main");

        Assert.Multiple(() =>
        {
            Assert.That(zone.Version.Node.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(zone.Version.Node.Id, Is.Not.EqualTo(zoneNodeId));
            Assert.That(assignment.ContentZoneNodeId, Is.EqualTo(zone.Version.Node.Id));
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
    public async Task GetOrCreateByNameAsync_UnpublishedZone_DoesNotDuplicate()
    {
        var zoneNodeId = Guid.NewGuid();
        await SeedZonesAsync(Zone(zoneNodeId, 0, "Global", state: ContentVersionState.Draft));

        var zone = await NewService().GetOrCreateByNameAsync("Global");

        Assert.That(zone.Version.Node.Id, Is.EqualTo(zoneNodeId));
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
    public async Task ReorderItemsAsync_AssignsSequentialOrdinals()
    {
        var zoneNodeId = Guid.NewGuid();
        var aNodeId = Guid.NewGuid();
        var bNodeId = Guid.NewGuid();
        await SeedItemsAsync(
            Item(zoneNodeId, aNodeId, ordinal: 1),
            Item(zoneNodeId, bNodeId, ordinal: 2));

        var ok = await NewService().ReorderItemsAsync(zoneNodeId, new List<Guid> { bNodeId, aNodeId });

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
    public async Task ReorderItemsAsync_UnknownId_ReturnsFalseWithoutWriting()
    {
        var zoneNodeId = Guid.NewGuid();
        var aNodeId = Guid.NewGuid();
        await SeedItemsAsync(Item(zoneNodeId, aNodeId, ordinal: 1));

        var ok = await NewService().ReorderItemsAsync(zoneNodeId, new List<Guid> { aNodeId, Guid.NewGuid() });

        var ctx = NewContext();
        var store = TestStore.Create<ContentZoneItemDTO>(ctx, "contentzoneitems");
        var a = await store.GetAsync(aNodeId);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(a!.Ordinal, Is.EqualTo(1), "nothing was written");
        });
    }

    [Test]
    public async Task ReorderItemsAsync_ForeignZoneId_ReturnsFalseWithoutWriting()
    {
        var zoneNodeId = Guid.NewGuid();
        var otherZoneNodeId = Guid.NewGuid();
        var foreignNodeId = Guid.NewGuid();
        await SeedItemsAsync(Item(otherZoneNodeId, foreignNodeId, ordinal: 1));

        var ok = await NewService().ReorderItemsAsync(zoneNodeId, new List<Guid> { foreignNodeId });

        var ctx = NewContext();
        var store = TestStore.Create<ContentZoneItemDTO>(ctx, "contentzoneitems");
        var foreign = await store.GetAsync(foreignNodeId);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(foreign!.Ordinal, Is.EqualTo(1), "nothing was written");
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

        Assert.That(ok, Is.False);
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

    [Test]
    public async Task DeleteZoneAsync_RoutableItem_SweepsWidgetRoute()
    {
        var ctx = NewContext();
        var routeService = Substitute.For<ICMSRouteService>();
        var zoneNodeId = Guid.NewGuid();
        var itemNodeId = Guid.NewGuid();
        await SeedZonesAsync(Zone(zoneNodeId, 0, "Main"));
        await SeedItemsAsync(Item(zoneNodeId, itemNodeId, 0));

        var ok = await NewService(ctx: ctx, routeService: routeService).DeleteZoneAsync(zoneNodeId);

        Assert.That(ok, Is.True);
        await routeService.Received(1).DeleteByOwningContentAsync(itemNodeId, Arg.Any<CancellationToken>());
    }

    // ─── page deletion cleanup ────────────────────────────────────────────────

    [Test]
    public async Task DeletePageZonesAsync_NoAssignments_IsNoOp()
    {
        await NewService().DeletePageZonesAsync(Guid.NewGuid());

        await using var verify = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(verify.Set<ContentZoneDTO>().Any(), Is.False);
            Assert.That(verify.Set<ContentZoneAssignmentDTO>().Any(), Is.False);
        });
    }

    [Test]
    public async Task DeletePageZonesAsync_DeletesPageZonesAndNestedChildren()
    {
        var pageNodeId = Guid.NewGuid();
        var mainZoneId = Guid.NewGuid();
        var childZoneId = Guid.NewGuid();
        await SeedZonesAsync(Zone(mainZoneId, 0, "Main"), Zone(childZoneId, 0, "Sub"));
        await SeedItemsAsync(Item(mainZoneId, Guid.NewGuid()), Item(childZoneId, Guid.NewGuid()));
        await SeedAssignmentsAsync(
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Main", ContentZoneNodeId = mainZoneId, ParentPageNodeId = pageNodeId },
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Sub", ContentZoneNodeId = childZoneId, ParentZoneNodeId = mainZoneId });

        await NewService().DeletePageZonesAsync(pageNodeId);

        await using var verify = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(verify.Set<ContentZoneDTO>().Any(), Is.False);
            Assert.That(verify.Set<ContentZoneItemDTO>().Any(), Is.False);
            Assert.That(verify.Set<ContentZoneAssignmentDTO>().Any(), Is.False);
        });
    }

    [Test]
    public async Task DeletePageZonesAsync_SharedZone_KeepsZoneButRemovesPageAssignment()
    {
        var pageNodeId1 = Guid.NewGuid();
        var pageNodeId2 = Guid.NewGuid();
        var sharedZoneId = Guid.NewGuid();
        await SeedZonesAsync(Zone(sharedZoneId, 0, "Shared"));
        await SeedAssignmentsAsync(
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Main", ContentZoneNodeId = sharedZoneId, ParentPageNodeId = pageNodeId1 },
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Main", ContentZoneNodeId = sharedZoneId, ParentPageNodeId = pageNodeId2 });

        await NewService().DeletePageZonesAsync(pageNodeId1);

        await using var verify = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(verify.Set<ContentZoneDTO>().Any(), Is.True);
            var remaining = verify.Set<ContentZoneAssignmentDTO>().Single();
            Assert.That(remaining.ParentPageNodeId, Is.EqualTo(pageNodeId2));
        });
    }

    [Test]
    public async Task DeletePageZonesAsync_ChildZoneSharedWithAnotherPage_Survives()
    {
        var pageNodeId1 = Guid.NewGuid();
        var pageNodeId2 = Guid.NewGuid();
        var parentZoneId = Guid.NewGuid();
        var childZoneId = Guid.NewGuid();
        await SeedZonesAsync(Zone(parentZoneId, 0, "Parent"), Zone(childZoneId, 0, "Child"));
        await SeedItemsAsync(Item(parentZoneId, Guid.NewGuid()), Item(childZoneId, Guid.NewGuid()));
        await SeedAssignmentsAsync(
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Main", ContentZoneNodeId = parentZoneId, ParentPageNodeId = pageNodeId1 },
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Sub", ContentZoneNodeId = childZoneId, ParentZoneNodeId = parentZoneId },
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Extra", ContentZoneNodeId = childZoneId, ParentPageNodeId = pageNodeId2 });

        await NewService().DeletePageZonesAsync(pageNodeId1);

        await using var verify = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(verify.Set<ContentZoneDTO>().Any(z => z.Version.NodeId == parentZoneId), Is.False);
            Assert.That(verify.Set<ContentZoneDTO>().Any(z => z.Version.NodeId == childZoneId), Is.True);
            Assert.That(verify.Set<ContentZoneItemDTO>().Any(i => i.ContentZoneNodeId == childZoneId), Is.True);
            Assert.That(verify.Set<ContentZoneAssignmentDTO>().Any(a => a.ContentZoneNodeId == childZoneId && a.ParentPageNodeId == pageNodeId2), Is.True);
        });
    }

    [Test]
    public async Task DeletePageZonesAsync_PageAssigningOneZoneToTwoSlots_DeletesZone()
    {
        var pageNodeId = Guid.NewGuid();
        var zoneNodeId = Guid.NewGuid();
        await SeedZonesAsync(Zone(zoneNodeId, 0, "Shared"));
        await SeedItemsAsync(Item(zoneNodeId, Guid.NewGuid()));
        await SeedAssignmentsAsync(
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "A", ContentZoneNodeId = zoneNodeId, ParentPageNodeId = pageNodeId },
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "B", ContentZoneNodeId = zoneNodeId, ParentPageNodeId = pageNodeId });

        await NewService().DeletePageZonesAsync(pageNodeId);

        await using var verify = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(verify.Set<ContentZoneDTO>().Any(), Is.False);
            Assert.That(verify.Set<ContentZoneItemDTO>().Any(), Is.False);
            Assert.That(verify.Set<ContentZoneAssignmentDTO>().Any(), Is.False);
        });
    }

    [Test]
    public async Task DeleteZoneTreeAsync_SelfCycle_Terminates()
    {
        var zoneNodeId = Guid.NewGuid();
        await SeedZonesAsync(Zone(zoneNodeId, 0, "Loop"));
        await SeedAssignmentsAsync(
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Loop", ContentZoneNodeId = zoneNodeId, ParentZoneNodeId = zoneNodeId });

        await NewService().DeleteZoneTreeAsync(zoneNodeId);

        await using var verify = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(verify.Set<ContentZoneDTO>().Any(), Is.False);
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

    // ─── page-owned zones: drafts until the page is published ─────────────────

    /// <summary>Seeds a published zone assigned to a page and returns the zone node id.</summary>
    private async Task<Guid> SeedPageZoneAsync(Guid pageNodeId, string slot = "Main")
    {
        var zoneNodeId = Guid.NewGuid();
        await SeedZonesAsync(Zone(zoneNodeId, 0, slot));
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = slot, ContentZoneNodeId = zoneNodeId, ParentPageNodeId = pageNodeId });
        return zoneNodeId;
    }

    private ContentStore<ContentZoneItemDTO> ItemStore(CmsDbContext ctx)
        => TestStore.Create<ContentZoneItemDTO>(ctx, "contentzoneitems");

    [Test]
    public async Task AddItemAsync_PageZone_StaysDraft()
    {
        var zoneNodeId = await SeedPageZoneAsync(Guid.NewGuid());

        var item = await NewService().AddItemAsync(zoneNodeId, new ContentZoneItemDTO { ComponentName = "ContentBlock" });

        Assert.Multiple(async () =>
        {
            Assert.That(item.Version.State, Is.EqualTo(ContentVersionState.Draft));
            Assert.That(await NewService().GetItemsAsync(zoneNodeId), Is.Empty, "not live");
            Assert.That(await NewService(readContext: new TestReadContext(ContentReadMode.Draft)).GetItemsAsync(zoneNodeId), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task UpdateItemAsync_PageZone_PublishedItem_LeavesLiveVersionUntouched()
    {
        var zoneNodeId = await SeedPageZoneAsync(Guid.NewGuid());
        var itemNodeId = Guid.NewGuid();
        await SeedItemsAsync(Item(zoneNodeId, itemNodeId, component: "Old"));

        var ok = await NewService().UpdateItemAsync(new ContentZoneItemDTO
        {
            ComponentName = "New",
            IsActive = true,
            Version = new ContentVersion { Node = new ContentNode { Id = itemNodeId } }
        });

        var store = ItemStore(NewContext());
        var live = await store.GetAsync(itemNodeId);
        var draft = await store.GetCurrentDraftAsync(itemNodeId);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(live!.ComponentName, Is.EqualTo("Old"));
            Assert.That(draft!.ComponentName, Is.EqualTo("New"));
            Assert.That(draft.Version.State, Is.EqualTo(ContentVersionState.Draft));
        });
    }

    [Test]
    public async Task ReorderItemsAsync_PageZone_OrdinalsAreDrafts()
    {
        var zoneNodeId = await SeedPageZoneAsync(Guid.NewGuid());
        var a = Item(zoneNodeId, ordinal: 1);
        var b = Item(zoneNodeId, ordinal: 2);
        await SeedItemsAsync(a, b);

        var ok = await NewService().ReorderItemsAsync(zoneNodeId, [b.Version.Node.Id, a.Version.Node.Id]);

        var live = await NewService().GetItemsAsync(zoneNodeId);
        var draft = await NewService(readContext: new TestReadContext(ContentReadMode.Draft)).GetItemsAsync(zoneNodeId);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(live.Select(i => i.Version.NodeId), Is.EqualTo(new[] { a.Version.Node.Id, b.Version.Node.Id }));
            Assert.That(draft.Select(i => i.Version.NodeId), Is.EqualTo(new[] { b.Version.Node.Id, a.Version.Node.Id }));
        });
    }

    [Test]
    public async Task RemoveItemAsync_PageZone_PublishedItem_SavesInactiveDraft()
    {
        var zoneNodeId = await SeedPageZoneAsync(Guid.NewGuid());
        var item = Item(zoneNodeId);
        await SeedItemsAsync(item);

        var ok = await NewService().RemoveItemAsync(item.Version.Node.Id);

        var store = ItemStore(NewContext());
        var live = await store.GetAsync(item.Version.Node.Id);
        var draft = await store.GetCurrentDraftAsync(item.Version.Node.Id);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(live!.IsActive, Is.True, "still live until the page is published");
            Assert.That(draft!.IsActive, Is.False);
            Assert.That(draft.Version.State, Is.EqualTo(ContentVersionState.Draft));
        });
    }

    [Test]
    public async Task RemoveItemAsync_PageZone_NeverPublished_HardDeletes()
    {
        var zoneNodeId = await SeedPageZoneAsync(Guid.NewGuid());
        var item = Item(zoneNodeId, state: ContentVersionState.Draft);
        await SeedItemsAsync(item);

        var ok = await NewService().RemoveItemAsync(item.Version.Node.Id);

        await using var ctx = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(ctx.Set<ContentNode>().Any(n => n.Id == item.Version.Node.Id), Is.False);
        });
    }

    [Test]
    public async Task RemoveItemAsync_PageZone_DraftSaveFails_ReturnsFalse()
    {
        var zoneNodeId = await SeedPageZoneAsync(Guid.NewGuid());
        var item = Item(zoneNodeId);
        await SeedItemsAsync(item);

        var itemStore = Substitute.For<IContentStore<ContentZoneItemDTO>>();
        itemStore.GetCurrentDraftAsync(item.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(item);
        itemStore.SaveDraftAsync(Arg.Any<ContentZoneItemDTO>(), null, Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(false, "boom"));

        var ok = await NewService(itemStore: itemStore).RemoveItemAsync(item.Version.Node.Id);

        Assert.That(ok, Is.False);
        await itemStore.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetZoneTreeNodeIdsForPageAsync_WalksNestedZonesAndGuardsCycles()
    {
        var pageNodeId = Guid.NewGuid();
        Guid zoneA = Guid.NewGuid(), zoneB = Guid.NewGuid(), otherPageZone = Guid.NewGuid();
        await SeedAssignmentsAsync(
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Main", ContentZoneNodeId = zoneA, ParentPageNodeId = pageNodeId },
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Sub", ContentZoneNodeId = zoneB, ParentZoneNodeId = zoneA },
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Loop", ContentZoneNodeId = zoneA, ParentZoneNodeId = zoneB },
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Main", ContentZoneNodeId = otherPageZone, ParentPageNodeId = Guid.NewGuid() });

        var ids = await NewService().GetZoneTreeNodeIdsForPageAsync(pageNodeId);

        Assert.That(ids, Is.EqualTo(new[] { zoneA, zoneB }));
    }

    [Test]
    public async Task GetPendingItemNodeIdsAsync_ReturnsUnpublishedCurrentDraftsInTheGivenZones()
    {
        var zone = Guid.NewGuid();
        var otherZone = Guid.NewGuid();
        var live = Item(zone);
        var draft = Item(zone, state: ContentVersionState.Draft);
        var elsewhere = Item(otherZone, state: ContentVersionState.Draft);
        var deleted = Item(zone, state: ContentVersionState.Draft);
        deleted.Version.Node.IsDeleted = true;
        await SeedItemsAsync(live, draft, elsewhere, deleted);

        Assert.Multiple(async () =>
        {
            Assert.That(await NewService().GetPendingItemNodeIdsAsync([]), Is.Empty);
            Assert.That(await NewService().GetPendingItemNodeIdsAsync([zone]), Is.EqualTo(new[] { draft.Version.Node.Id }));
        });
    }

    [Test]
    public async Task GetPagesWithPendingZoneChangesAsync_FlagsPagesWithNestedDrafts()
    {
        var pendingPage = Guid.NewGuid();
        var cleanPage = Guid.NewGuid();
        var parentZone = await SeedPageZoneAsync(pendingPage);
        var childZone = Guid.NewGuid();
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Sub", ContentZoneNodeId = childZone, ParentZoneNodeId = parentZone });
        var cleanZone = await SeedPageZoneAsync(cleanPage);
        await SeedItemsAsync(Item(childZone, state: ContentVersionState.Draft), Item(cleanZone));

        var result = await NewService().GetPagesWithPendingZoneChangesAsync([pendingPage, cleanPage]);

        Assert.That(result, Is.EquivalentTo(new[] { pendingPage }));
    }

    [Test]
    public async Task GetPagesWithPendingZoneChangesAsync_NoPagesOrNoDrafts_ReturnsEmpty()
    {
        var page = Guid.NewGuid();
        var zone = await SeedPageZoneAsync(page);
        await SeedItemsAsync(Item(zone));

        Assert.Multiple(async () =>
        {
            Assert.That(await NewService().GetPagesWithPendingZoneChangesAsync([]), Is.Empty);
            Assert.That(await NewService().GetPagesWithPendingZoneChangesAsync([page]), Is.Empty);
        });
    }

    [Test]
    public async Task PublishPageZonesAsync_PublishesEveryPendingDraftUnderThePage()
    {
        var pageNodeId = Guid.NewGuid();
        var zone = await SeedPageZoneAsync(pageNodeId);
        var otherZone = await SeedPageZoneAsync(Guid.NewGuid());
        var added = Item(zone, state: ContentVersionState.Draft, component: "Added");
        var untouched = Item(otherZone, state: ContentVersionState.Draft);
        await SeedItemsAsync(added, untouched);

        var result = await NewService().PublishPageZonesAsync(pageNodeId);

        var store = ItemStore(NewContext());
        Assert.Multiple(async () =>
        {
            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.Published.Select(i => i.ComponentName), Is.EqualTo(new[] { "Added" }));
            Assert.That(await store.GetAsync(added.Version.Node.Id), Is.Not.Null);
            Assert.That(await store.GetAsync(untouched.Version.Node.Id), Is.Null, "another page's draft stays a draft");
        });
    }

    [Test]
    public async Task PublishPageZonesAsync_Failures_AreCollectedPerItem()
    {
        var pageNodeId = Guid.NewGuid();
        var zone = await SeedPageZoneAsync(pageNodeId);
        var failsWithMessage = Item(zone, state: ContentVersionState.Draft);
        var failsSilently = Item(zone, state: ContentVersionState.Draft);
        var vanishes = Item(zone, state: ContentVersionState.Draft);
        await SeedItemsAsync(failsWithMessage, failsSilently, vanishes);

        var itemStore = Substitute.For<IContentStore<ContentZoneItemDTO>>();
        itemStore.PublishAsync(failsWithMessage.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(false, "boom"));
        itemStore.PublishAsync(failsSilently.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(false));
        itemStore.PublishAsync(vanishes.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));
        itemStore.GetCurrentDraftAsync(vanishes.Version.Node.Id, Arg.Any<CancellationToken>()).Returns((ContentZoneItemDTO?)null);

        var result = await NewService(itemStore: itemStore).PublishPageZonesAsync(pageNodeId);

        Assert.Multiple(() =>
        {
            Assert.That(result.Published, Is.Empty);
            Assert.That(result.Errors, Has.Count.EqualTo(3));
            Assert.That(result.Errors, Does.Contain("boom"));
            Assert.That(result.Errors, Does.Contain($"Content zone item {vanishes.Version.Node.Id} could not be published."));
        });
    }

    [Test]
    public async Task DiscardPageZoneDraftsAsync_RevertsEditsAndDeletesNeverPublishedItems()
    {
        var pageNodeId = Guid.NewGuid();
        var zone = await SeedPageZoneAsync(pageNodeId);
        var live = Item(zone, component: "Old");
        var added = Item(zone, state: ContentVersionState.Draft);
        await SeedItemsAsync(live, added);
        await NewService().UpdateItemAsync(new ContentZoneItemDTO
        {
            ComponentName = "New",
            IsActive = true,
            Version = new ContentVersion { Node = new ContentNode { Id = live.Version.Node.Id } }
        });
        var routeService = Substitute.For<ICMSRouteService>();

        var (success, error) = await NewService(routeService: routeService).DiscardPageZoneDraftsAsync(pageNodeId);

        var ctx = NewContext();
        var current = await ItemStore(ctx).GetCurrentDraftAsync(live.Version.Node.Id);
        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(error, Is.Null);
            Assert.That(current!.ComponentName, Is.EqualTo("Old"));
            Assert.That(current.Version.State, Is.EqualTo(ContentVersionState.Published));
            Assert.That(ctx.Set<ContentNode>().Any(n => n.Id == added.Version.Node.Id), Is.False);
        });
        await routeService.Received(1).DeleteByOwningContentAsync(added.Version.Node.Id, Arg.Any<CancellationToken>());
        await routeService.DidNotReceive().DeleteByOwningContentAsync(live.Version.Node.Id, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DiscardPageZoneDraftsAsync_Failures_AreReported()
    {
        var pageNodeId = Guid.NewGuid();
        var zone = await SeedPageZoneAsync(pageNodeId);
        var a = Item(zone, state: ContentVersionState.Draft);
        var b = Item(zone, state: ContentVersionState.Draft);
        await SeedItemsAsync(a, b);

        var itemStore = Substitute.For<IContentStore<ContentZoneItemDTO>>();
        itemStore.DiscardDraftAsync(a.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(false, "boom"));
        itemStore.DiscardDraftAsync(b.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(false));

        var (success, error) = await NewService(itemStore: itemStore).DiscardPageZoneDraftsAsync(pageNodeId);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(error, Does.Contain("boom"));
            Assert.That(error, Does.Contain($"Content zone item {b.Version.Node.Id} could not be discarded."));
        });
    }
}
