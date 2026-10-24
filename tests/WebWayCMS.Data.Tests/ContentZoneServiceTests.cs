using Microsoft.EntityFrameworkCore;

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
    private IPageHistoryRecorder _recorder = null!;

    [SetUp]
    public void SetUp()
    {
        _db = TestContexts.NewDb();
        _recorder = Substitute.For<IPageHistoryRecorder>();
    }

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
        return new ContentZoneService(context, zoneStore, itemStore, rc, scope, routeService, new PageZoneTree(context), _recorder);
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

    /// <summary>A nested zone's assignment: <paramref name="childZone"/> is owned by a container item.</summary>
    private static ContentZoneAssignmentDTO Nested(Guid childZone, Guid ownerItemNodeId, string slotName = "Sub")
        => new() { Id = Guid.NewGuid(), SlotName = slotName, ContentZoneNodeId = childZone, ParentItemNodeId = ownerItemNodeId };

    /// <summary>Seeds a container item in <paramref name="parentZone"/> that owns <paramref name="childZone"/>.</summary>
    private async Task<Guid> SeedNestedZoneAsync(Guid parentZone, Guid childZone, string slotName = "Sub",
        ContentVersionState ownerState = ContentVersionState.Published)
    {
        var owner = Item(parentZone, component: "Layout", state: ownerState);
        await SeedItemsAsync(owner);
        await SeedAssignmentsAsync(Nested(childZone, owner.Version.Node.Id, slotName));
        return owner.Version.Node.Id;
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
        var tree = new PageZoneTree(ctx);

        Assert.Multiple(() =>
        {
            Assert.That(() => new ContentZoneService(null!, zoneStore, itemStore, readContext, scope, routeService, tree, _recorder), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneService(ctx, null!, itemStore, readContext, scope, routeService, tree, _recorder), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneService(ctx, zoneStore, null!, readContext, scope, routeService, tree, _recorder), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneService(ctx, zoneStore, itemStore, null!, scope, routeService, tree, _recorder), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneService(ctx, zoneStore, itemStore, readContext, null!, routeService, tree, _recorder), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneService(ctx, zoneStore, itemStore, readContext, scope, null!, tree, _recorder), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneService(ctx, zoneStore, itemStore, readContext, scope, routeService, null!, _recorder), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneService(ctx, zoneStore, itemStore, readContext, scope, routeService, tree, null!), Throws.ArgumentNullException);
            Assert.That(() => new PageZoneTree(null!), Throws.ArgumentNullException);
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

    // ─── item-slot assignment ─────────────────────────────────────────────────

    [Test]
    public async Task GetByItemSlotAsync_FoundAndNotFound()
    {
        var parentItem = Guid.NewGuid();
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Sub", ContentZoneNodeId = Guid.NewGuid(), ParentItemNodeId = parentItem });

        Assert.Multiple(async () =>
        {
            Assert.That(await NewService().GetByItemSlotAsync(parentItem, "Sub"), Is.Not.Null);
            Assert.That(await NewService().GetByItemSlotAsync(parentItem, "Other"), Is.Null);
        });
    }

    [Test]
    public async Task GetOrCreateByItemSlotAsync_NoAssignment_Creates()
    {
        var parentItem = Guid.NewGuid();

        var (zone, assignment) = await NewService().GetOrCreateByItemSlotAsync(parentItem, "Sub");

        Assert.Multiple(() =>
        {
            Assert.That(zone.Name, Is.EqualTo("Sub"));
            Assert.That(assignment.ParentItemNodeId, Is.EqualTo(parentItem));
            Assert.That(assignment.ContentZoneNode, Is.SameAs(zone.Version.Node));
        });
    }

    [Test]
    public async Task GetOrCreateByItemSlotAsync_ExistingAssignmentAndZone_ReturnsExisting()
    {
        var parentItem = Guid.NewGuid();
        var zoneNodeId = Guid.NewGuid();
        await SeedZonesAsync(Zone(zoneNodeId, 0, "Sub"));
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Sub", ContentZoneNodeId = zoneNodeId, ParentItemNodeId = parentItem });

        var (zone, _) = await NewService().GetOrCreateByItemSlotAsync(parentItem, "Sub");

        Assert.That(zone.Version.Node.Id, Is.EqualTo(zoneNodeId));
    }

    [Test]
    public async Task GetOrCreateByItemSlotAsync_AssignmentButZoneDeleted_ReturnsTransientZone()
    {
        var parentItem = Guid.NewGuid();
        var zoneNodeId = Guid.NewGuid();
        await SeedZonesAsync(Zone(zoneNodeId, 0, "Sub", deleted: true));
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Sub", ContentZoneNodeId = zoneNodeId, ParentItemNodeId = parentItem });

        var (zone, assignment) = await NewService().GetOrCreateByItemSlotAsync(parentItem, "Sub");

        Assert.Multiple(() =>
        {
            Assert.That(assignment, Is.Not.Null);
            Assert.That(zone.Name, Is.EqualTo("Sub"));
        });
    }

    [Test]
    public async Task GetOrCreateByItemSlotAsync_ZoneBecomesAvailableInTransaction_ReturnsZone()
    {
        var ctx = NewContext();
        var parentItem = Guid.NewGuid();
        var zoneNodeId = Guid.NewGuid();
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Sub", ContentZoneNodeId = zoneNodeId, ParentItemNodeId = parentItem });

        var zone = Zone(zoneNodeId, 0, "Sub");
        var zoneStore = Substitute.For<IContentStore<ContentZoneDTO>>();
        zoneStore.GetAsync(zoneNodeId, Arg.Any<CancellationToken>()).Returns((ContentZoneDTO?)null, zone);

        var service = NewService(ctx, zoneStore: zoneStore);

        var (result, _) = await service.GetOrCreateByItemSlotAsync(parentItem, "Sub");

        Assert.That(result, Is.SameAs(zone));
    }

    [Test]
    public async Task GetOrCreateByItemSlotAsync_CreateFails_Throws()
    {
        var ctx = NewContext();
        var zoneStore = Substitute.For<IContentStore<ContentZoneDTO>>();
        zoneStore.SaveDraftAsync(Arg.Any<ContentZoneDTO>(), null, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ContentWriteResult>(new InvalidOperationException("boom")));

        var service = NewService(ctx, zoneStore: zoneStore);

        Assert.That(async () => await service.GetOrCreateByItemSlotAsync(Guid.NewGuid(), "Sub"),
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
        await SeedNestedZoneAsync(parentZone, zoneNodeId);

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
        await SeedNestedZoneAsync(parentWithChild, Guid.NewGuid());
        await SeedItemsAsync(Item(parentWithout));

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
        await SeedNestedZoneAsync(parentZone, childZone);
        await SeedAssignmentsAsync(
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
        await SeedNestedZoneAsync(zoneB, zoneA, "A");
        await SeedNestedZoneAsync(zoneA, zoneB, "B");

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

        // Ordinal 2 at position 1: the item moves, so the reorder writes it (and the save fails).
        var itemStore = Substitute.For<IContentStore<ContentZoneItemDTO>>();
        itemStore.GetCurrentDraftAsync(itemNodeId, Arg.Any<CancellationToken>()).Returns(Item(zoneNodeId, itemNodeId, 0, ordinal: 2));
        itemStore.SaveDraftAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(false, "boom"));

        var service = NewService(ctx, itemStore: itemStore);

        var ok = await service.ReorderItemsAsync(zoneNodeId, new List<Guid> { itemNodeId });

        Assert.That(ok, Is.False);
        await itemStore.DidNotReceive().PublishAsync(itemNodeId, Arg.Any<CancellationToken>());
        await _recorder.DidNotReceive().RecordDraftAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
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
        var ownerId = Guid.NewGuid();
        await SeedItemsAsync(Item(mainZoneId, ownerId), Item(childZoneId, Guid.NewGuid()));
        await SeedAssignmentsAsync(
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Main", ContentZoneNodeId = mainZoneId, ParentPageNodeId = pageNodeId },
            Nested(childZoneId, ownerId));

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
        var ownerId = Guid.NewGuid();
        await SeedItemsAsync(Item(parentZoneId, ownerId), Item(childZoneId, Guid.NewGuid()));
        await SeedAssignmentsAsync(
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Main", ContentZoneNodeId = parentZoneId, ParentPageNodeId = pageNodeId1 },
            Nested(childZoneId, ownerId),
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
    public async Task DeleteZoneAsync_SelfCycle_Terminates()
    {
        var zoneNodeId = Guid.NewGuid();
        await SeedZonesAsync(Zone(zoneNodeId, 0, "Loop"));
        await SeedNestedZoneAsync(zoneNodeId, zoneNodeId, "Loop");

        await NewService().DeleteZoneAsync(zoneNodeId);

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
    public async Task RemoveItemAsync_PageZone_NeverPublished_SavesInactiveDraft()
    {
        var zoneNodeId = await SeedPageZoneAsync(Guid.NewGuid());
        var item = Item(zoneNodeId, state: ContentVersionState.Draft);
        await SeedItemsAsync(item);

        var ok = await NewService().RemoveItemAsync(item.Version.Node.Id);

        var versions = await ItemStore(NewContext()).GetAllVersionsAsync(item.Version.Node.Id);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(versions, Has.Count.EqualTo(2), "removal is a new version, never a delete");
            Assert.That(versions[0].IsActive, Is.False);
            Assert.That(versions[0].Version.ChangeNote, Is.EqualTo("Removed"));
            Assert.That(versions[1].IsActive, Is.True);
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
        itemStore.SaveDraftAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
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
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Main", ContentZoneNodeId = otherPageZone, ParentPageNodeId = Guid.NewGuid() });
        await SeedNestedZoneAsync(zoneA, zoneB);
        await SeedNestedZoneAsync(zoneB, zoneA, "Loop");

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
        await SeedNestedZoneAsync(parentZone, childZone);
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
    public async Task PublishPageZoneItemsAsync_PublishesOnlyTheListedPendingItems()
    {
        var pageNodeId = Guid.NewGuid();
        var zone = await SeedPageZoneAsync(pageNodeId);
        var otherZone = await SeedPageZoneAsync(Guid.NewGuid());
        var listed = Item(zone, state: ContentVersionState.Draft, component: "Listed");
        var unlisted = Item(zone, state: ContentVersionState.Draft, component: "Unlisted");
        var otherPage = Item(otherZone, state: ContentVersionState.Draft, component: "OtherPage");
        await SeedItemsAsync(listed, unlisted, otherPage);

        var result = await NewService().PublishPageZoneItemsAsync(
            pageNodeId, [listed.Version.Node.Id, otherPage.Version.Node.Id]);

        var store = ItemStore(NewContext());
        Assert.Multiple(async () =>
        {
            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.FailedItemNodeIds, Is.Empty);
            Assert.That(result.Published.Select(i => i.ComponentName), Is.EqualTo(new[] { "Listed" }));
            Assert.That(await store.GetAsync(unlisted.Version.Node.Id), Is.Null, "an unlisted draft stays a draft");
            Assert.That(await store.GetAsync(otherPage.Version.Node.Id), Is.Null, "another page's item is not published");
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
            Assert.That(result.FailedItemNodeIds, Is.EquivalentTo(new[] { failsWithMessage.Version.Node.Id, failsSilently.Version.Node.Id, vanishes.Version.Node.Id }));
        });
    }

    // ─── moving items (drag-and-drop) ─────────────────────────────────────────

    /// <summary>A zone's items as the zone editor and preview see them (Draft read context).</summary>
    private Task<List<ContentZoneItemDTO>> DraftItemsAsync(Guid zoneNodeId)
        => NewService(readContext: new TestReadContext(ContentReadMode.Draft)).GetItemsAsync(zoneNodeId);

    private static IEnumerable<Guid> Ids(IEnumerable<ContentZoneItemDTO> items) => items.Select(i => i.Version.NodeId);

    [Test]
    public async Task MoveItemAsync_SameZone_ReordersAsDraft()
    {
        var zone = await SeedPageZoneAsync(Guid.NewGuid());
        var a = Item(zone, ordinal: 1);
        var b = Item(zone, ordinal: 2);
        var c = Item(zone, ordinal: 3);
        await SeedItemsAsync(a, b, c);

        var (success, error) = await NewService().MoveItemAsync(c.Version.Node.Id, zone, 0);

        var live = await NewService().GetItemsAsync(zone);
        var draft = await DraftItemsAsync(zone);
        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(error, Is.Null);
            Assert.That(Ids(live), Is.EqualTo(Ids([a, b, c])));
            Assert.That(Ids(draft), Is.EqualTo(Ids([c, a, b])));
        });
    }

    [Test]
    public async Task MoveItemAsync_SamePosition_WritesNothing()
    {
        var zone = await SeedPageZoneAsync(Guid.NewGuid());
        var a = Item(zone, ordinal: 1);
        var b = Item(zone, ordinal: 2);
        await SeedItemsAsync(a, b);

        var (success, _) = await NewService().MoveItemAsync(b.Version.Node.Id, zone, 1);

        await using var verify = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(verify.Set<ContentVersion>().Count(v => v.State == ContentVersionState.Draft), Is.Zero);
        });
    }

    [Test]
    public async Task MoveItemAsync_PositionIsClamped()
    {
        var zone = await SeedPageZoneAsync(Guid.NewGuid());
        var a = Item(zone, ordinal: 1);
        var b = Item(zone, ordinal: 2);
        await SeedItemsAsync(a, b);

        await NewService().MoveItemAsync(a.Version.Node.Id, zone, 99);
        var afterHigh = await DraftItemsAsync(zone);
        await NewService().MoveItemAsync(a.Version.Node.Id, zone, -5);
        var afterLow = await DraftItemsAsync(zone);

        Assert.Multiple(() =>
        {
            Assert.That(Ids(afterHigh), Is.EqualTo(Ids([b, a])));
            Assert.That(Ids(afterLow), Is.EqualTo(Ids([a, b])));
        });
    }

    [Test]
    public async Task MoveItemAsync_AcrossPageZones_IsDraftUntilPublished()
    {
        var page = Guid.NewGuid();
        var main = await SeedPageZoneAsync(page, "Main");
        var side = await SeedPageZoneAsync(page, "Side");
        var moved = Item(main, ordinal: 1);
        var stays = Item(main, ordinal: 2);
        var sideItem = Item(side, ordinal: 1);
        await SeedItemsAsync(moved, stays, sideItem);

        var (success, _) = await NewService().MoveItemAsync(moved.Version.Node.Id, side, 1);

        var liveMain = await NewService().GetItemsAsync(main);
        var liveSide = await NewService().GetItemsAsync(side);
        var draftMain = await DraftItemsAsync(main);
        var draftSide = await DraftItemsAsync(side);
        var pending = await NewService().GetPendingItemNodeIdsAsync(await NewService().GetZoneTreeNodeIdsForPageAsync(page));
        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(Ids(liveMain), Is.EqualTo(Ids([moved, stays])));
            Assert.That(Ids(liveSide), Is.EqualTo(Ids([sideItem])));
            Assert.That(Ids(draftMain), Is.EqualTo(Ids([stays])));
            Assert.That(Ids(draftSide), Is.EqualTo(Ids([sideItem, moved])));
            Assert.That(pending, Does.Contain(moved.Version.Node.Id));
        });
    }

    [Test]
    public async Task MoveItemAsync_PublishedMove_GoesLive()
    {
        var page = Guid.NewGuid();
        var main = await SeedPageZoneAsync(page, "Main");
        var side = await SeedPageZoneAsync(page, "Side");
        var moved = Item(main);
        await SeedItemsAsync(moved);

        await NewService().MoveItemAsync(moved.Version.Node.Id, side, 0);
        await NewService().PublishPageZonesAsync(page);

        Assert.Multiple(async () =>
        {
            Assert.That(await NewService().GetItemsAsync(main), Is.Empty);
            Assert.That(Ids(await NewService().GetItemsAsync(side)), Is.EqualTo(Ids([moved])));
        });
    }

    [Test]
    public async Task MoveItemAsync_IntoAndOutOfNestedZoneOfSamePage_Allowed()
    {
        var page = Guid.NewGuid();
        var main = await SeedPageZoneAsync(page);
        var column = Guid.NewGuid();
        await SeedZonesAsync(Zone(column, 0, "Left"));
        await SeedNestedZoneAsync(main, column, "Left");
        var widget = Item(main, ordinal: 5);
        await SeedItemsAsync(widget);

        var (into, _) = await NewService().MoveItemAsync(widget.Version.Node.Id, column, 0);
        var inColumn = await DraftItemsAsync(column);
        var (outOf, _) = await NewService().MoveItemAsync(widget.Version.Node.Id, main, 0);
        var inMain = await DraftItemsAsync(main);

        Assert.Multiple(() =>
        {
            Assert.That(into, Is.True);
            Assert.That(Ids(inColumn), Is.EqualTo(Ids([widget])));
            Assert.That(outOf, Is.True);
            Assert.That(Ids(inMain).First(), Is.EqualTo(widget.Version.Node.Id));
        });
    }

    [Test]
    public async Task MoveItemAsync_ContainerIntoItsOwnNestedZone_IsRejected()
    {
        var page = Guid.NewGuid();
        var main = await SeedPageZoneAsync(page);
        var column = Guid.NewGuid();
        var innerColumn = Guid.NewGuid();
        await SeedZonesAsync(Zone(column, 0, "Left"), Zone(innerColumn, 0, "Inner"));
        var container = await SeedNestedZoneAsync(main, column, "Left");
        await SeedNestedZoneAsync(column, innerColumn, "Inner");

        var direct = await NewService().MoveItemAsync(container, column, 0);
        var deep = await NewService().MoveItemAsync(container, innerColumn, 0);

        Assert.Multiple(() =>
        {
            Assert.That(direct, Is.EqualTo((false, "A widget cannot be moved into itself.")));
            Assert.That(deep, Is.EqualTo((false, "A widget cannot be moved into itself.")));
        });
    }

    [Test]
    public async Task MoveItemAsync_AcrossPages_IsRejected()
    {
        var zoneA = await SeedPageZoneAsync(Guid.NewGuid());
        var zoneB = await SeedPageZoneAsync(Guid.NewGuid());
        var item = Item(zoneA);
        await SeedItemsAsync(item);

        var result = await NewService().MoveItemAsync(item.Version.Node.Id, zoneB, 0);

        Assert.That(result, Is.EqualTo((false, "Widgets can only be moved between zones of the same page.")));
    }

    [Test]
    public async Task MoveItemAsync_BetweenGlobalZones_IsRejected()
    {
        Guid global1 = Guid.NewGuid(), global2 = Guid.NewGuid();
        await SeedZonesAsync(Zone(global1, 0, "Header"), Zone(global2, 0, "Footer"));
        var item = Item(global1);
        await SeedItemsAsync(item);

        var result = await NewService().MoveItemAsync(item.Version.Node.Id, global2, 0);

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task MoveItemAsync_WithinGlobalZone_Publishes()
    {
        var global = Guid.NewGuid();
        await SeedZonesAsync(Zone(global, 0, "Header"));
        var a = Item(global, ordinal: 1);
        var b = Item(global, ordinal: 2);
        await SeedItemsAsync(a, b);

        var (success, _) = await NewService().MoveItemAsync(b.Version.Node.Id, global, 0);

        var live = await NewService().GetItemsAsync(global);
        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(Ids(live), Is.EqualTo(Ids([b, a])));
        });
    }

    [Test]
    public async Task MoveItemAsync_MissingOrInactiveItemOrMissingZone_ReturnsErrors()
    {
        var zone = await SeedPageZoneAsync(Guid.NewGuid());
        var inactive = Item(zone, active: false);
        var item = Item(zone);
        await SeedItemsAsync(inactive, item);

        Assert.Multiple(async () =>
        {
            Assert.That(await NewService().MoveItemAsync(Guid.NewGuid(), zone, 0), Is.EqualTo((false, "Item not found.")));
            Assert.That(await NewService().MoveItemAsync(inactive.Version.Node.Id, zone, 0), Is.EqualTo((false, "Item not found.")));
            Assert.That(await NewService().MoveItemAsync(item.Version.Node.Id, Guid.NewGuid(), 0), Is.EqualTo((false, "Target zone not found.")));
        });
    }

    [Test]
    public async Task MoveItemAsync_SaveFails_ReturnsError()
    {
        var ctx = NewContext();
        var zone = Guid.NewGuid();
        await SeedZonesAsync(Zone(zone, 0, "Header"));
        var itemNodeId = Guid.NewGuid();
        var itemStore = Substitute.For<IContentStore<ContentZoneItemDTO>>();
        itemStore.GetCurrentDraftAsync(itemNodeId, Arg.Any<CancellationToken>()).Returns(Item(zone, itemNodeId, ordinal: 4));
        itemStore.SaveDraftAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(false, "boom"));

        var result = await NewService(ctx, itemStore: itemStore).MoveItemAsync(itemNodeId, zone, 0);

        Assert.That(result, Is.EqualTo((false, "Failed to move the widget.")));
    }

    [Test]
    public async Task MoveItemAsync_StaleVersionNumber_ReturnsStaleMessage()
    {
        var zone = await SeedPageZoneAsync(Guid.NewGuid());
        var item = Item(zone, ordinal: 1);
        await SeedItemsAsync(item);

        var result = await NewService().MoveItemAsync(item.Version.Node.Id, zone, 0, expectedVersionNumber: 99);

        Assert.That(result, Is.EqualTo((false, ContentStore<ContentZoneItemDTO>.StaleVersionMessage)));
    }

    [Test]
    public async Task MoveItemAsync_CrossZone_RenumbersSourceZoneOrdinals()
    {
        var page = Guid.NewGuid();
        var main = await SeedPageZoneAsync(page, "Main");
        var side = await SeedPageZoneAsync(page, "Side");
        var a = Item(main, ordinal: 1);
        var moved = Item(main, ordinal: 2);
        await SeedItemsAsync(a, moved);

        var (success, _) = await NewService().MoveItemAsync(moved.Version.Node.Id, side, 0);

        await using var verify = NewContext();
        var store = ItemStore(verify);
        var remaining = await store.GetCurrentDraftAsync(a.Version.Node.Id);
        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(remaining!.Ordinal, Is.EqualTo(1), "the source zone's gap is closed");
        });
    }

    [Test]
    public async Task MoveItemAsync_CrossZone_SourceRenumberFails_ReturnsError()
    {
        var page = Guid.NewGuid();
        var main = await SeedPageZoneAsync(page, "Main");
        var side = await SeedPageZoneAsync(page, "Side");
        var moved = Item(main, ordinal: 1);
        var stays = Item(main, ordinal: 3); // the gap the moved item leaves behind
        await SeedItemsAsync(moved, stays);

        var itemStore = Substitute.For<IContentStore<ContentZoneItemDTO>>();
        itemStore.GetCurrentDraftAsync(moved.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(moved);
        // The moved item's save (into Side) succeeds; renumbering what stayed in Main fails.
        itemStore.SaveDraftAsync(Arg.Is<ContentZoneItemDTO>(i => i.ContentZoneNodeId == side), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true));
        itemStore.SaveDraftAsync(Arg.Is<ContentZoneItemDTO>(i => i.ContentZoneNodeId != side), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(false, "boom"));

        var result = await NewService(itemStore: itemStore).MoveItemAsync(moved.Version.Node.Id, side, 0);

        Assert.That(result, Is.EqualTo((false, "Failed to move the widget.")));
    }

    [Test]
    public async Task MoveItemAsync_CrossZone_StaleSiblingInSource_ReturnsStaleMessageAndDiscardsTrackedWrites()
    {
        var page = Guid.NewGuid();
        var main = await SeedPageZoneAsync(page, "Main");
        var side = await SeedPageZoneAsync(page, "Side");
        var moved = Item(main, ordinal: 1);
        var stays = Item(main, ordinal: 3);
        await SeedItemsAsync(moved, stays);

        var ctx = NewContext();
        var itemStore = Substitute.For<IContentStore<ContentZoneItemDTO>>();
        itemStore.GetCurrentDraftAsync(moved.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(moved);
        // The target save "succeeds" and leaves a tracked row behind, like the real store would;
        // the source renumber then hits a concurrently edited sibling.
        itemStore.SaveDraftAsync(Arg.Is<ContentZoneItemDTO>(i => i.ContentZoneNodeId == side), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                ctx.Attach(new ContentNode { Id = Guid.NewGuid(), ContentTypeKey = "contentzoneitems" });
                return new ContentWriteResult(true);
            });
        itemStore.SaveDraftAsync(Arg.Is<ContentZoneItemDTO>(i => i.ContentZoneNodeId != side), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(false, ContentStore<ContentZoneItemDTO>.StaleVersionMessage));

        var result = await NewService(ctx, itemStore: itemStore).MoveItemAsync(moved.Version.Node.Id, side, 0);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo((false, ContentStore<ContentZoneItemDTO>.StaleVersionMessage)));
            Assert.That(ctx.ChangeTracker.Entries(), Is.Empty, "the rolled-back writes are not left on the shared context");
        });
    }

    [Test]
    public async Task ReorderItemsAsync_StaleSiblingPartway_ReturnsFalseAndDiscardsTrackedWrites()
    {
        var ctx = NewContext();
        var zoneNodeId = Guid.NewGuid();
        var a = Item(zoneNodeId, ordinal: 2);
        var b = Item(zoneNodeId, ordinal: 1);

        var itemStore = Substitute.For<IContentStore<ContentZoneItemDTO>>();
        itemStore.GetCurrentDraftAsync(a.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(a);
        itemStore.GetCurrentDraftAsync(b.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(b);
        itemStore.SaveDraftAsync(Arg.Is<ContentZoneItemDTO>(i => i.Version.NodeId == a.Version.Node.Id), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                ctx.Attach(new ContentNode { Id = Guid.NewGuid(), ContentTypeKey = "contentzoneitems" });
                return new ContentWriteResult(true);
            });
        itemStore.SaveDraftAsync(Arg.Is<ContentZoneItemDTO>(i => i.Version.NodeId == b.Version.Node.Id), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(false, ContentStore<ContentZoneItemDTO>.StaleVersionMessage));

        var ok = await NewService(ctx, itemStore: itemStore).ReorderItemsAsync(zoneNodeId, [a.Version.Node.Id, b.Version.Node.Id]);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(ctx.ChangeTracker.Entries(), Is.Empty);
        });
    }

    [Test]
    public async Task ReorderItemsAsync_SaveThrows_RethrowsAndDiscardsTrackedWrites()
    {
        var ctx = NewContext();
        var zoneNodeId = Guid.NewGuid();
        var a = Item(zoneNodeId, ordinal: 2);

        var itemStore = Substitute.For<IContentStore<ContentZoneItemDTO>>();
        itemStore.GetCurrentDraftAsync(a.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(a);
        itemStore.SaveDraftAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns<ContentWriteResult>(_ =>
            {
                ctx.Attach(new ContentNode { Id = Guid.NewGuid(), ContentTypeKey = "contentzoneitems" });
                throw new InvalidOperationException("boom");
            });

        var service = NewService(ctx, itemStore: itemStore);

        Assert.That(async () => await service.ReorderItemsAsync(zoneNodeId, [a.Version.Node.Id]), Throws.InvalidOperationException);
        Assert.That(ctx.ChangeTracker.Entries(), Is.Empty);
    }

    // ─── nested zones owned by items ──────────────────────────────────────────

    [Test]
    public async Task GetParentPageNodeForZoneAsync_OwnerItemMissing_ReturnsNull()
    {
        var zone = Guid.NewGuid();
        await SeedAssignmentsAsync(Nested(zone, Guid.NewGuid()));

        Assert.That(await NewService().GetParentPageNodeForZoneAsync(zone), Is.Null);
    }

    [Test]
    public async Task DeleteZoneAsync_ContainerItem_DeletesItsNestedTree()
    {
        var global = Guid.NewGuid();
        var column = Guid.NewGuid();
        var innerColumn = Guid.NewGuid();
        await SeedZonesAsync(Zone(global, 0, "Header"), Zone(column, 0, "Left"), Zone(innerColumn, 0, "Inner"));
        await SeedNestedZoneAsync(global, column, "Left");
        await SeedNestedZoneAsync(column, innerColumn, "Inner");
        await SeedItemsAsync(Item(innerColumn));

        var ok = await NewService().DeleteZoneAsync(global);

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
    public async Task DeleteZoneAsync_ContainerItem_KeepsNestedZoneReferencedElsewhere()
    {
        var global = Guid.NewGuid();
        var column = Guid.NewGuid();
        var page = Guid.NewGuid();
        await SeedZonesAsync(Zone(global, 0, "Header"), Zone(column, 0, "Left"));
        await SeedNestedZoneAsync(global, column, "Left");
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Main", ContentZoneNodeId = column, ParentPageNodeId = page });

        await NewService().DeleteZoneAsync(global);

        await using var verify = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(verify.Set<ContentZoneDTO>().Any(z => z.Version.NodeId == column), Is.True);
            Assert.That(verify.Set<ContentZoneAssignmentDTO>().Single().ParentPageNodeId, Is.EqualTo(page));
        });
    }

    [Test]
    public async Task RemoveItemAsync_PublishedContainerInPageZone_KeepsNestedZonesUntilPublish()
    {
        var main = await SeedPageZoneAsync(Guid.NewGuid());
        var column = Guid.NewGuid();
        await SeedZonesAsync(Zone(column, 0, "Left"));
        var container = await SeedNestedZoneAsync(main, column, "Left");

        await NewService().RemoveItemAsync(container);

        await using var verify = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(verify.Set<ContentZoneDTO>().Any(z => z.Version.NodeId == column), Is.True);
            Assert.That(verify.Set<ContentZoneAssignmentDTO>().Any(a => a.ParentItemNodeId == container), Is.True);
        });
    }

    [Test]
    public async Task MoveItemAsync_RemovedItemsAreRenumberedInPlace()
    {
        // B was removed (an inactive draft). Renumbering only the live items would leave B sharing an
        // ordinal with A, so a later discard of the removal would order them arbitrarily.
        var zone = await SeedPageZoneAsync(Guid.NewGuid());
        var a = Item(zone, ordinal: 1);
        var b = Item(zone, ordinal: 2, active: false, state: ContentVersionState.Draft);
        var c = Item(zone, ordinal: 3);
        await SeedItemsAsync(a, b, c);

        var (success, _) = await NewService().MoveItemAsync(c.Version.Node.Id, zone, 0);

        await using var verify = NewContext();
        var store = ItemStore(verify);
        var ordinals = new[] { c, a, b }
            .Select(i => store.GetCurrentDraftAsync(i.Version.Node.Id).Result!.Ordinal)
            .ToList();
        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(ordinals, Is.EqualTo(new[] { 1, 2, 3 }));
        });
    }

    [Test]
    public async Task RemoveItemAsync_DraftContainerHoldingMovedLiveWidget_KeepsBothForRestore()
    {
        // Removing a container is never a delete: it becomes an inactive draft and keeps its nested zone,
        // so the widget moved into it stays there (hidden) and comes back if the container is restored.
        var page = Guid.NewGuid();
        var main = await SeedPageZoneAsync(page);
        var column = Guid.NewGuid();
        await SeedZonesAsync(Zone(column, 0, "Left"));
        var container = await SeedNestedZoneAsync(main, column, "Left", ContentVersionState.Draft);
        var live = Item(main, ordinal: 1);
        await SeedItemsAsync(live);
        await NewService().MoveItemAsync(live.Version.Node.Id, column, 0);

        var ok = await NewService().RemoveItemAsync(container);

        var store = ItemStore(NewContext());
        Assert.Multiple(async () =>
        {
            Assert.That(ok, Is.True);
            Assert.That((await store.GetCurrentDraftAsync(container))!.IsActive, Is.False);
            Assert.That((await store.GetCurrentDraftAsync(live.Version.Node.Id))!.ContentZoneNodeId, Is.EqualTo(column));
            Assert.That(Ids(await NewService().GetItemsAsync(main)), Is.EqualTo(Ids([live])), "the live page is untouched");
        });
    }

    [Test]
    public async Task DeleteZoneAsync_ItemLivingElsewhere_KeepsItAndDropsItsVersionsHere()
    {
        var oldZone = Guid.NewGuid();
        var newZone = Guid.NewGuid();
        await SeedZonesAsync(Zone(oldZone, 0, "Old"), Zone(newZone, 0, "New"));
        var itemNodeId = Guid.NewGuid();
        var historical = Item(oldZone, itemNodeId, version: 1, state: ContentVersionState.Archived, currentDraft: false);
        var current = Item(newZone, itemNodeId, version: 2);
        current.Version.Node = historical.Version.Node;
        await SeedItemsAsync(historical, current);

        await NewService().DeleteZoneAsync(oldZone);

        await using var verify = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(verify.Set<ContentZoneItemDTO>().Select(i => i.VersionId), Is.EqualTo(new[] { current.VersionId }));
            Assert.That(verify.Set<ContentZoneDTO>().Any(z => z.Version.NodeId == oldZone), Is.False);
        });
    }

    // ─── metadata + concurrency on item updates ───────────────────────────────

    [Test]
    public async Task UpdateItemAsync_MissingMetadataJson_KeepsExisting()
    {
        var zoneNodeId = Guid.NewGuid();
        var itemNodeId = Guid.NewGuid();
        var seeded = Item(zoneNodeId, itemNodeId);
        seeded.MetadataJson = "{\"x\":1}";
        await SeedItemsAsync(seeded);

        var ok = await NewService().UpdateItemAsync(new ContentZoneItemDTO
        {
            ComponentName = "New",
            IsActive = true,
            Version = new ContentVersion { Node = new ContentNode { Id = itemNodeId } }
        });

        var current = await ItemStore(NewContext()).GetCurrentDraftAsync(itemNodeId);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(current!.MetadataJson, Is.EqualTo("{\"x\":1}"), "a config save must not wipe the metadata dialog's values");
        });
    }

    [Test]
    public async Task UpdateItemAsync_BlankMetadataJson_NormalizesToEmptyDocument()
    {
        var zoneNodeId = Guid.NewGuid();
        var itemNodeId = Guid.NewGuid();
        var seeded = Item(zoneNodeId, itemNodeId);
        seeded.MetadataJson = null; // e.g. a row that predates the metadata column
        await SeedItemsAsync(seeded);

        var ok = await NewService().UpdateItemAsync(new ContentZoneItemDTO
        {
            ComponentName = "New",
            IsActive = true,
            MetadataJson = "  ",
            Version = new ContentVersion { Node = new ContentNode { Id = itemNodeId } }
        });

        var current = await ItemStore(NewContext()).GetCurrentDraftAsync(itemNodeId);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(current!.MetadataJson, Is.EqualTo("{}"));
        });
    }

    [Test]
    public async Task UpdateItemAsync_StaleVersionNumber_ReturnsFalse()
    {
        var zoneNodeId = Guid.NewGuid();
        var itemNodeId = Guid.NewGuid();
        await SeedItemsAsync(Item(zoneNodeId, itemNodeId));

        var stale = await NewService().UpdateItemAsync(new ContentZoneItemDTO
        {
            ComponentName = "New",
            IsActive = true,
            Version = new ContentVersion { Node = new ContentNode { Id = itemNodeId } }
        }, expectedVersionNumber: 99);

        var fresh = await NewService().UpdateItemAsync(new ContentZoneItemDTO
        {
            ComponentName = "Newer",
            IsActive = true,
            Version = new ContentVersion { Node = new ContentNode { Id = itemNodeId } }
        }, expectedVersionNumber: 0);

        Assert.Multiple(() =>
        {
            Assert.That(stale, Is.False);
            Assert.That(fresh, Is.True);
        });
    }

    // ─── RestoreItemVersionAsync ──────────────────────────────────────────────

    /// <summary>
    /// Seeds one item node with an archived historical version (<paramref name="oldZone"/>, ordinal
    /// <paramref name="oldOrdinal"/>, component "Old") and a published current version
    /// (<paramref name="currentZone"/>, ordinal <paramref name="currentOrdinal"/>, component "New").
    /// Returns (item node id, historical version id).
    /// </summary>
    private async Task<(Guid NodeId, Guid HistoricalVersionId)> SeedItemHistoryAsync(
        Guid oldZone, int oldOrdinal, Guid currentZone, int currentOrdinal,
        bool oldActive = true, string? oldMetadata = null, bool seedCurrent = true)
    {
        var old = Item(oldZone, version: 0, ordinal: oldOrdinal, active: oldActive, component: "Old",
            state: ContentVersionState.Archived, currentDraft: !seedCurrent);
        old.MetadataJson = oldMetadata;
        if (seedCurrent)
        {
            var current = Item(currentZone, version: 1, ordinal: currentOrdinal, component: "New");
            current.Version.Node = old.Version.Node;
            current.Version.NodeId = old.Version.NodeId;
            await SeedItemsAsync(old, current);
        }
        else
        {
            old.Version.IsCurrentDraft = false;
            await SeedItemsAsync(old);
        }

        return (old.Version.NodeId, old.VersionId);
    }

    [Test]
    public async Task RestoreItemVersionAsync_SameZone_RestoresContentAtOldPositionAsDraft()
    {
        var zone = await SeedPageZoneAsync(Guid.NewGuid());
        var a = Item(zone, ordinal: 1);
        var b = Item(zone, ordinal: 2);
        await SeedItemsAsync(a, b);
        var (nodeId, versionId) = await SeedItemHistoryAsync(zone, 1, zone, 3);

        var result = await NewService().RestoreItemVersionAsync(versionId, "{\"AdminLabel\":\"x\"}", expectedVersionNumber: 1);

        var draft = await DraftItemsAsync(zone);
        var live = await NewService().GetItemsAsync(zone);
        var restored = draft.Single(i => i.Version.NodeId == nodeId);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo((true, (string?)null)));
            Assert.That(draft.Select(i => i.Version.NodeId), Is.EqualTo(new[] { nodeId, a.Version.NodeId, b.Version.NodeId }));
            Assert.That(draft.Select(i => i.Ordinal), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(restored.ComponentName, Is.EqualTo("Old"));
            Assert.That(restored.MetadataJson, Is.EqualTo("{\"AdminLabel\":\"x\"}"));
            Assert.That(restored.Version.State, Is.EqualTo(ContentVersionState.Draft));
            Assert.That(restored.Version.ChangeNote, Is.EqualTo("Restored from version 0"));
            Assert.That(live.Single(i => i.Version.NodeId == nodeId).ComponentName, Is.EqualTo("New"), "a page's widget stays a draft");
        });
    }

    [Test]
    public async Task RestoreItemVersionAsync_NoMetadataSupplied_UsesVersionMetadataOrEmptyDocument()
    {
        var zone = await SeedPageZoneAsync(Guid.NewGuid());
        var (withMeta, withMetaVersion) = await SeedItemHistoryAsync(zone, 1, zone, 1, oldMetadata: "{\"AdminNote\":\"n\"}");
        var (without, withoutVersion) = await SeedItemHistoryAsync(zone, 2, zone, 2);

        await NewService().RestoreItemVersionAsync(withMetaVersion, null, null);
        await NewService().RestoreItemVersionAsync(withoutVersion, null, null);

        var draft = await NewService(readContext: new TestReadContext(ContentReadMode.Draft)).GetItemsAsync(zone);
        Assert.Multiple(() =>
        {
            Assert.That(draft.Single(i => i.Version.NodeId == withMeta).MetadataJson, Is.EqualTo("{\"AdminNote\":\"n\"}"));
            Assert.That(draft.Single(i => i.Version.NodeId == without).MetadataJson, Is.EqualTo("{}"));
        });
    }

    [Test]
    public async Task RestoreItemVersionAsync_CrossZoneOnSamePage_MovesBackAndRenumbersSource()
    {
        var page = Guid.NewGuid();
        var main = await SeedPageZoneAsync(page, "Main");
        var side = await SeedPageZoneAsync(page, "Side");
        var mainStays = Item(main, ordinal: 2);
        var sideItem = Item(side, ordinal: 1);
        await SeedItemsAsync(mainStays, sideItem);
        var (nodeId, versionId) = await SeedItemHistoryAsync(side, 1, main, 1);

        var result = await NewService().RestoreItemVersionAsync(versionId, null, null);

        var draftSide = await DraftItemsAsync(side);
        var draftMain = await DraftItemsAsync(main);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(draftSide.Select(i => i.Version.NodeId), Is.EqualTo(new[] { nodeId, sideItem.Version.NodeId }));
            Assert.That(draftMain.Select(i => (i.Version.NodeId, i.Ordinal)), Is.EqualTo(new[] { (mainStays.Version.NodeId, 1) }));
        });
    }

    [Test]
    public async Task RestoreItemVersionAsync_OldOrdinalPastTheEnd_AppendsToZone()
    {
        var zone = await SeedPageZoneAsync(Guid.NewGuid());
        var a = Item(zone, ordinal: 1);
        await SeedItemsAsync(a);
        var (nodeId, versionId) = await SeedItemHistoryAsync(zone, 9, zone, 2);

        await NewService().RestoreItemVersionAsync(versionId, null, null);

        var draft = await DraftItemsAsync(zone);
        Assert.That(draft.Select(i => i.Version.NodeId), Is.EqualTo(new[] { a.Version.NodeId, nodeId }));
    }

    [Test]
    public async Task RestoreItemVersionAsync_GlobalZone_PublishesImmediately()
    {
        var zone = Guid.NewGuid();
        await SeedZonesAsync(Zone(zone, 0, "Footer"));
        var (nodeId, versionId) = await SeedItemHistoryAsync(zone, 1, zone, 1);

        var result = await NewService().RestoreItemVersionAsync(versionId, null, null);

        var live = await NewService().GetItemsAsync(zone);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(live.Single(i => i.Version.NodeId == nodeId).ComponentName, Is.EqualTo("Old"));
        });
    }

    [Test]
    public async Task RestoreItemVersionAsync_InactiveVersion_RestoresAsRemovedDraft()
    {
        var zone = await SeedPageZoneAsync(Guid.NewGuid());
        var (nodeId, versionId) = await SeedItemHistoryAsync(zone, 1, zone, 1, oldActive: false);

        var result = await NewService().RestoreItemVersionAsync(versionId, null, null);

        await using var verify = NewContext();
        var current = await ItemStore(verify).GetCurrentDraftAsync(nodeId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(current!.IsActive, Is.False);
        });
    }

    [Test]
    public async Task RestoreItemVersionAsync_UnknownVersion_ReturnsVersionNotFound()
    {
        var result = await NewService().RestoreItemVersionAsync(Guid.NewGuid(), null, null);

        Assert.That(result, Is.EqualTo((false, IContentZoneService.VersionNotFoundMessage)));
    }

    [Test]
    public async Task RestoreItemVersionAsync_ItemHasNoCurrentDraft_ReturnsItemNotFound()
    {
        var zone = await SeedPageZoneAsync(Guid.NewGuid());
        var (_, versionId) = await SeedItemHistoryAsync(zone, 1, zone, 1, seedCurrent: false);

        var result = await NewService().RestoreItemVersionAsync(versionId, null, null);

        Assert.That(result, Is.EqualTo((false, IContentZoneService.ItemNotFoundMessage)));
    }

    [Test]
    public async Task RestoreItemVersionAsync_StaleExpectedVersion_ReturnsStaleMessage()
    {
        var zone = await SeedPageZoneAsync(Guid.NewGuid());
        var (_, versionId) = await SeedItemHistoryAsync(zone, 1, zone, 1);

        var result = await NewService().RestoreItemVersionAsync(versionId, null, expectedVersionNumber: 7);

        Assert.That(result, Is.EqualTo((false, ContentStore<ContentZoneItemDTO>.StaleVersionMessage)));
    }

    [Test]
    public async Task RestoreItemVersionAsync_OldZoneDeleted_IsRefused()
    {
        var zone = await SeedPageZoneAsync(Guid.NewGuid());
        var (_, versionId) = await SeedItemHistoryAsync(Guid.NewGuid(), 1, zone, 1);

        var result = await NewService().RestoreItemVersionAsync(versionId, null, null);

        Assert.That(result, Is.EqualTo((false, IContentZoneService.RestoreZoneMissingMessage)));
    }

    [Test]
    public async Task RestoreItemVersionAsync_OldZoneNowOnAnotherPage_IsRefused()
    {
        var zone = await SeedPageZoneAsync(Guid.NewGuid());
        var otherPageZone = await SeedPageZoneAsync(Guid.NewGuid());
        var (_, versionId) = await SeedItemHistoryAsync(otherPageZone, 1, zone, 1);

        var result = await NewService().RestoreItemVersionAsync(versionId, null, null);

        Assert.That(result, Is.EqualTo((false, IContentZoneService.RestoreZoneMissingMessage)));
    }

    [Test]
    public async Task RestoreItemVersionAsync_GlobalItemFromAnotherGlobalZone_IsRefused()
    {
        var zone = Guid.NewGuid();
        var otherGlobal = Guid.NewGuid();
        await SeedZonesAsync(Zone(zone, 0, "Footer"), Zone(otherGlobal, 0, "Header"));
        var (_, versionId) = await SeedItemHistoryAsync(otherGlobal, 1, zone, 1);

        var result = await NewService().RestoreItemVersionAsync(versionId, null, null);

        Assert.That(result, Is.EqualTo((false, IContentZoneService.RestoreZoneMissingMessage)));
    }

    [Test]
    public async Task RestoreItemVersionAsync_OldZoneInsideTheItemItself_IsRefused()
    {
        var page = Guid.NewGuid();
        var main = await SeedPageZoneAsync(page);
        var nested = Guid.NewGuid();
        await SeedZonesAsync(Zone(nested, 0, "Left"));
        var (nodeId, versionId) = await SeedItemHistoryAsync(nested, 1, main, 1);
        await SeedAssignmentsAsync(Nested(nested, nodeId));

        var result = await NewService().RestoreItemVersionAsync(versionId, null, null);

        Assert.That(result, Is.EqualTo((false, IContentZoneService.RestoreZoneMissingMessage)));
    }

    [Test]
    public async Task RestoreItemVersionAsync_SaveFails_ReturnsErrorAndRecordsNothing()
    {
        var zone = await SeedPageZoneAsync(Guid.NewGuid());
        var historical = Item(zone, ordinal: 1, component: "Old", state: ContentVersionState.Archived, currentDraft: false);
        var current = Item(zone, historical.Version.NodeId, version: 1, ordinal: 1);
        var itemStore = Substitute.For<IContentStore<ContentZoneItemDTO>>();
        itemStore.GetVersionAsync(historical.VersionId, Arg.Any<CancellationToken>()).Returns(historical);
        itemStore.GetCurrentDraftAsync(historical.Version.NodeId, Arg.Any<CancellationToken>()).Returns(current);
        itemStore.SaveDraftAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(false, "boom"));

        var result = await NewService(itemStore: itemStore).RestoreItemVersionAsync(historical.VersionId, null, null);

        Assert.That(result, Is.EqualTo((false, "Failed to restore the widget.")));
        await _recorder.DidNotReceive().RecordDraftAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RestoreItemVersionAsync_SourceRenumberFails_ReturnsErrorAndPublishesNothing()
    {
        var page = Guid.NewGuid();
        var main = await SeedPageZoneAsync(page, "Main");
        var side = await SeedPageZoneAsync(page, "Side");
        var stays = Item(main, ordinal: 3); // the gap the restored item leaves in Main is renumbered
        await SeedItemsAsync(stays);
        var historical = Item(side, ordinal: 1, component: "Old", state: ContentVersionState.Archived, currentDraft: false);
        var current = Item(main, historical.Version.NodeId, version: 1, ordinal: 1);
        var itemStore = Substitute.For<IContentStore<ContentZoneItemDTO>>();
        itemStore.GetVersionAsync(historical.VersionId, Arg.Any<CancellationToken>()).Returns(historical);
        itemStore.GetCurrentDraftAsync(historical.Version.NodeId, Arg.Any<CancellationToken>()).Returns(current);
        itemStore.SaveDraftAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true), new ContentWriteResult(false, "boom"));

        var result = await NewService(itemStore: itemStore).RestoreItemVersionAsync(historical.VersionId, null, null);

        Assert.That(result, Is.EqualTo((false, "Failed to move the widget.")));
        await itemStore.DidNotReceive().PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RestoreItemVersionAsync_SameZoneAndPosition_MintsExactlyOneVersion()
    {
        var zone = await SeedPageZoneAsync(Guid.NewGuid(), "Main");
        var (nodeId, versionId) = await SeedItemHistoryAsync(zone, 1, zone, 1);

        await NewService().RestoreItemVersionAsync(versionId, null, null);

        var versions = await ItemStore(NewContext()).GetAllVersionsAsync(nodeId);
        Assert.Multiple(() =>
        {
            Assert.That(versions, Has.Count.EqualTo(3), "content and placement land in one new version");
            Assert.That(versions[0].ComponentName, Is.EqualTo("Old"));
            Assert.That(versions[0].Version.ChangeNote, Is.EqualTo("Restored from version 0"));
        });
    }

    // ─── history: one entry per action, version notes ─────────────────────────

    private async Task<List<string?>> NotesAsync(Guid itemNodeId)
        => (await ItemStore(NewContext()).GetAllVersionsAsync(itemNodeId)).Select(v => v.Version.ChangeNote).ToList();

    [Test]
    public async Task ItemWrites_OnAPage_RecordOneEntryEach_WithDescriptiveNotes()
    {
        var page = Guid.NewGuid();
        var main = await SeedPageZoneAsync(page, "Main");
        var side = await SeedPageZoneAsync(page, "Side");
        var service = NewService();

        var added = await service.AddItemAsync(main, new ContentZoneItemDTO { ComponentName = "Text", IsActive = true, Version = new ContentVersion { Node = new ContentNode() } });
        var id = added.Version.Node!.Id;
        var sibling = await NewService().AddItemAsync(main, new ContentZoneItemDTO { ComponentName = "Hero", IsActive = true, Version = new ContentVersion { Node = new ContentNode() } });
        await NewService().UpdateItemAsync(new ContentZoneItemDTO { ComponentName = "Text", ComponentPropertiesJson = "{\"a\":1}", IsActive = true, Version = new ContentVersion { Node = new ContentNode { Id = id } } });
        await NewService().UpdateItemAsync(new ContentZoneItemDTO { ComponentName = "Text", ComponentPropertiesJson = "{\"a\":1}", MetadataJson = "{\"AdminLabel\":\"L\"}", IsActive = true, Version = new ContentVersion { Node = new ContentNode { Id = id } } });
        await NewService().MoveItemAsync(sibling.Version.Node!.Id, main, 0);
        await NewService().MoveItemAsync(id, side, 0);
        await NewService().RemoveItemAsync(id);

        Assert.Multiple(async () =>
        {
            Assert.That(await NotesAsync(id), Is.EqualTo(new[]
            {
                "Removed", "Moved to Side (position 1)", ContentZoneService.SiblingNote, "Settings changed", "Content edited", "Added to Main"
            }));
            Assert.That((await NotesAsync(sibling.Version.Node.Id))[0], Is.EqualTo("Moved to position 1"));
            Received.InOrder(() =>
            {
                _recorder.RecordDraftAsync(page, "Added Text to Main", Arg.Any<CancellationToken>());
                _recorder.RecordDraftAsync(page, "Added Hero to Main", Arg.Any<CancellationToken>());
                _recorder.RecordDraftAsync(page, "Text: Content edited", Arg.Any<CancellationToken>());
                _recorder.RecordDraftAsync(page, "Text: Settings changed", Arg.Any<CancellationToken>());
                _recorder.RecordDraftAsync(page, "Moved Hero within Main", Arg.Any<CancellationToken>());
                _recorder.RecordDraftAsync(page, "Moved Text to Side", Arg.Any<CancellationToken>());
                _recorder.RecordDraftAsync(page, "Removed Text", Arg.Any<CancellationToken>());
            });
        });
    }

    [Test]
    public async Task UpdateItemAsync_NoteDescribesWhatChanged()
    {
        var zone = await SeedPageZoneAsync(Guid.NewGuid());
        var item = Item(zone, active: false, state: ContentVersionState.Draft);
        await SeedItemsAsync(item);
        var id = item.Version.Node.Id;

        await NewService().UpdateItemAsync(new ContentZoneItemDTO { ComponentName = item.ComponentName, IsActive = true, Version = new ContentVersion { Node = new ContentNode { Id = id } } });
        await NewService().UpdateItemAsync(new ContentZoneItemDTO { ComponentName = item.ComponentName, IsActive = true, Version = new ContentVersion { Node = new ContentNode { Id = id } } });
        await NewService().UpdateItemAsync(new ContentZoneItemDTO { ComponentName = item.ComponentName, IsActive = false, Version = new ContentVersion { Node = new ContentNode { Id = id } } });

        Assert.That((await NotesAsync(id)).Take(3), Is.EqualTo(new[] { "Removed", "Saved (no changes)", "Restored" }));
    }

    [Test]
    public async Task ReorderItemsAsync_OnlyMovedItemsMint_AndOneEntryIsRecorded()
    {
        var page = Guid.NewGuid();
        var zone = await SeedPageZoneAsync(page, "Main");
        var a = Item(zone, ordinal: 1);
        var b = Item(zone, ordinal: 2);
        var c = Item(zone, ordinal: 3);
        await SeedItemsAsync(a, b, c);

        var ok = await NewService().ReorderItemsAsync(zone, [b.Version.Node.Id, a.Version.Node.Id, c.Version.Node.Id]);

        Assert.Multiple(async () =>
        {
            Assert.That(ok, Is.True);
            Assert.That(await NotesAsync(a.Version.Node.Id), Has.Count.EqualTo(2));
            Assert.That((await NotesAsync(a.Version.Node.Id))[0], Is.EqualTo("Reordered (position 2)"));
            Assert.That(await NotesAsync(c.Version.Node.Id), Has.Count.EqualTo(1), "an item that kept its place mints nothing");
            await _recorder.Received(1).RecordDraftAsync(page, "Reordered Main", Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task ItemWrites_InAGlobalZone_RecordNothing()
    {
        var zone = Guid.NewGuid();
        await SeedZonesAsync(Zone(zone, 0, "Footer"));
        var item = Item(zone);
        await SeedItemsAsync(item);

        await NewService().UpdateItemAsync(new ContentZoneItemDTO { ComponentName = "X", IsActive = true, Version = new ContentVersion { Node = new ContentNode { Id = item.Version.Node.Id } } });

        await _recorder.DidNotReceive().RecordDraftAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ItemWrites_InAZoneSharedByTwoPages_RecordForBoth()
    {
        var pageA = Guid.NewGuid();
        var pageB = Guid.NewGuid();
        var zone = await SeedPageZoneAsync(pageA, "Shared");
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Shared", ContentZoneNodeId = zone, ParentPageNodeId = pageB });
        var item = Item(zone);
        await SeedItemsAsync(item);

        await NewService().RemoveItemAsync(item.Version.Node.Id);

        await _recorder.Received(1).RecordDraftAsync(pageA, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _recorder.Received(1).RecordDraftAsync(pageB, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RemoveItemAsync_GlobalZone_PublishesTheInactiveVersion()
    {
        var zone = Guid.NewGuid();
        await SeedZonesAsync(Zone(zone, 0, "Footer"));
        var item = Item(zone);
        await SeedItemsAsync(item);

        var ok = await NewService().RemoveItemAsync(item.Version.Node.Id);

        var store = ItemStore(NewContext());
        Assert.Multiple(async () =>
        {
            Assert.That(ok, Is.True);
            Assert.That(await NewService().GetItemsAsync(zone), Is.Empty, "gone from the site at once");
            Assert.That(await store.GetAllVersionsAsync(item.Version.Node.Id), Has.Count.EqualTo(2), "kept as history");
        });
    }

    [Test]
    public async Task RemoveItemAsync_AlreadyRemoved_WritesNothing()
    {
        var zone = await SeedPageZoneAsync(Guid.NewGuid());
        var item = Item(zone, active: false, state: ContentVersionState.Draft);
        await SeedItemsAsync(item);

        var ok = await NewService().RemoveItemAsync(item.Version.Node.Id);

        Assert.Multiple(async () =>
        {
            Assert.That(ok, Is.True);
            Assert.That(await NotesAsync(item.Version.Node.Id), Has.Count.EqualTo(1));
            await _recorder.DidNotReceive().RecordDraftAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task DeleteZoneAsync_StrayDraftOfALiveItem_ReturnsHomeAndDropsItsVersionsHere()
    {
        // The item is live in Main and its current draft was moved into the zone being deleted.
        var page = Guid.NewGuid();
        var main = await SeedPageZoneAsync(page, "Main");
        var doomed = Guid.NewGuid();
        await SeedZonesAsync(Zone(doomed, 0, "Doomed"));
        var itemNodeId = Guid.NewGuid();
        var live = Item(main, itemNodeId, version: 1, ordinal: 1, state: ContentVersionState.Published, currentDraft: false);
        var stray = Item(doomed, itemNodeId, version: 2, ordinal: 1, state: ContentVersionState.Draft);
        stray.Version.Node = live.Version.Node;
        await SeedItemsAsync(live, stray);

        await NewService().DeleteZoneAsync(doomed);

        var current = (await ItemStore(NewContext()).GetCurrentDraftAsync(itemNodeId))!;
        await using var verify = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(current.ContentZoneNodeId, Is.EqualTo(main));
            Assert.That(current.Version.ChangeNote, Is.EqualTo("Returned to its published zone"));
            Assert.That(verify.Set<ContentZoneItemDTO>().Any(i => i.ContentZoneNodeId == doomed), Is.False);
        });
    }

    [Test]
    public async Task GetPagesWithPendingZoneChangesAsync_ComparesContent()
    {
        var page = Guid.NewGuid();
        var zone = await SeedPageZoneAsync(page);
        var itemNodeId = Guid.NewGuid();
        var live = Item(zone, itemNodeId, version: 0, ordinal: 1, component: "Same", state: ContentVersionState.Published, currentDraft: false);
        var equalDraft = Item(zone, itemNodeId, version: 1, ordinal: 1, component: "Same", state: ContentVersionState.Draft);
        equalDraft.Version.Node = live.Version.Node;
        var removedNeverLive = Item(zone, ordinal: 2, active: false, state: ContentVersionState.Draft);
        await SeedItemsAsync(live, equalDraft, removedNeverLive);

        var none = await NewService().GetPagesWithPendingZoneChangesAsync([page]);
        await SeedItemsAsync(Item(zone, ordinal: 3, state: ContentVersionState.Draft));
        var some = await NewService().GetPagesWithPendingZoneChangesAsync([page]);

        Assert.Multiple(async () =>
        {
            Assert.That(none, Is.Empty, "a draft equal to what is live, or a removed item never live, is not a change");
            Assert.That(some, Is.EquivalentTo(new[] { page }));
            Assert.That(await NewService().GetPagesWithPendingZoneChangesAsync([]), Is.Empty);
        });
    }

    // ─── container view change: nested content moves with it (SlotRemapPlan) ──

    /// <summary>Seeds a published zone owned by <paramref name="owner"/> under <paramref name="slot"/>.</summary>
    private async Task<Guid> SeedOwnedZoneAsync(Guid owner, string slot)
    {
        var zone = Guid.NewGuid();
        await SeedZonesAsync(Zone(zone, 0, slot));
        await SeedAssignmentsAsync(Nested(zone, owner, slot));
        return zone;
    }

    /// <summary>A zone's current drafts (removed ones included), in order.</summary>
    private async Task<List<(Guid NodeId, int Ordinal, bool IsActive)>> DraftOrderAsync(Guid zone)
    {
        await using var ctx = NewContext();
        return (await new PageZoneTree(ctx).CurrentDraftItems()
                .Where(i => i.ContentZoneNodeId == zone)
                .OrderBy(i => i.Ordinal)
                .ToListAsync())
            .Select(i => (i.Version.NodeId, i.Ordinal, i.IsActive))
            .ToList();
    }

    private static ContentZoneItemDTO LayoutUpdate(Guid layoutNodeId, string viewName) => new()
    {
        Version = new ContentVersion { Node = new ContentNode { Id = layoutNodeId } },
        ComponentName = "Layout",
        ComponentPropertiesJson = $"{{\"ViewName\":\"{viewName}\"}}",
        IsActive = true
    };

    private static readonly SlotRemapPlan HeroToTwoColumns = new(
        [new SlotMove("Hero", "Left"), new SlotMove("Column1", "Right"), new SlotMove("Column2", "Right")],
        "Layout: layout changed; content moved");

    [Test]
    public async Task UpdateItemAsync_WithRemap_MovesNestedContentIntoTheNewZones_AsOneDraftWrite()
    {
        var page = Guid.NewGuid();
        var main = await SeedPageZoneAsync(page);
        var layout = Item(main, component: "Layout", ordinal: 1);
        await SeedItemsAsync(layout);
        var layoutId = layout.Version.NodeId;
        var hero = await SeedOwnedZoneAsync(layoutId, "Hero");
        var column1 = await SeedOwnedZoneAsync(layoutId, "Column1");
        var column2 = await SeedOwnedZoneAsync(layoutId, "Column2");
        var left = await SeedOwnedZoneAsync(layoutId, "Left"); // rendered once before: already holds a widget
        var h = Item(hero, ordinal: 1);
        var c1 = Item(column1, ordinal: 1);
        var c1Removed = Item(column1, ordinal: 2, active: false);
        var c2 = Item(column2, ordinal: 1);
        var l0 = Item(left, ordinal: 1);
        await SeedItemsAsync(h, c1, c1Removed, c2, l0);

        var ok = await NewService().UpdateItemAsync(LayoutUpdate(layoutId, "TwoColumnEqual"), HeroToTwoColumns, expectedVersionNumber: 0);

        var right = (await NewService().GetByItemSlotAsync(layoutId, "Right"))!.ContentZoneNodeId;
        Assert.Multiple(async () =>
        {
            Assert.That(ok, Is.True);
            Assert.That(await DraftOrderAsync(left), Is.EqualTo(new[] { (l0.Version.NodeId, 1, true), (h.Version.NodeId, 2, true) }));
            Assert.That(await DraftOrderAsync(right), Is.EqualTo(new[]
            {
                (c1.Version.NodeId, 1, true), (c1Removed.Version.NodeId, 2, false), (c2.Version.NodeId, 3, true)
            }), "removed widgets move too, in order, the target created on demand");
            Assert.That(await DraftOrderAsync(hero), Is.Empty);
            Assert.That(await DraftOrderAsync(column1), Is.Empty);
            Assert.That((await NotesAsync(h.Version.NodeId))[0], Is.EqualTo("Moved to Left (layout changed)"));
            Assert.That((await NotesAsync(c2.Version.NodeId))[0], Is.EqualTo("Moved to Right (layout changed)"));
            Assert.That(await NotesAsync(l0.Version.NodeId), Has.Count.EqualTo(1), "a widget already in place is not rewritten");
            Assert.That((await NotesAsync(layoutId))[0], Is.EqualTo("Content edited"));
            Assert.That((await NewService().GetItemsAsync(hero)).Select(i => i.Version.NodeId), Is.EqualTo(new[] { h.Version.NodeId }),
                "a page's moves stay drafts until the page is published");
        });
        await _recorder.Received(1).RecordDraftAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _recorder.Received(1).RecordDraftAsync(page, HeroToTwoColumns.HistoryNote, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateItemAsync_WithRemap_GlobalZone_PublishesTheContainerAndTheMoves()
    {
        var global = Guid.NewGuid();
        await SeedZonesAsync(Zone(global, 0, "Footer"));
        var layout = Item(global, component: "Layout", ordinal: 1);
        await SeedItemsAsync(layout);
        var layoutId = layout.Version.NodeId;
        var hero = await SeedOwnedZoneAsync(layoutId, "Hero");
        var h = Item(hero, ordinal: 1);
        await SeedItemsAsync(h);

        var ok = await NewService().UpdateItemAsync(LayoutUpdate(layoutId, "TwoColumnEqual"), HeroToTwoColumns, null);

        var left = (await NewService().GetByItemSlotAsync(layoutId, "Left"))!.ContentZoneNodeId;
        Assert.Multiple(async () =>
        {
            Assert.That(ok, Is.True);
            Assert.That((await NewService().GetItemsAsync(left)).Select(i => i.Version.NodeId), Is.EqualTo(new[] { h.Version.NodeId }));
            Assert.That((await NewService().GetItemsAsync(global)).Single().ComponentPropertiesJson, Does.Contain("TwoColumnEqual"));
        });
        await _recorder.DidNotReceive().RecordDraftAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateItemAsync_WithRemap_NothingToMove_IsAPlainUpdate()
    {
        var page = Guid.NewGuid();
        var main = await SeedPageZoneAsync(page);
        var layout = Item(main, component: "Layout", ordinal: 1);
        await SeedItemsAsync(layout);
        var layoutId = layout.Version.NodeId;
        var left = await SeedOwnedZoneAsync(layoutId, "Left");
        await SeedOwnedZoneAsync(layoutId, "Hero"); // empty source
        var l0 = Item(left, ordinal: 1);
        await SeedItemsAsync(l0);
        // A missing source zone, a source that is its own target, and an empty source: nothing moves.
        var plan = new SlotRemapPlan([new SlotMove("Gone", "Left"), new SlotMove("Left", "Left"), new SlotMove("Hero", "Left")], "unused");

        var ok = await NewService().UpdateItemAsync(LayoutUpdate(layoutId, "TwoColumnEqual"), plan, 0);

        Assert.Multiple(async () =>
        {
            Assert.That(ok, Is.True);
            Assert.That(await DraftOrderAsync(left), Is.EqualTo(new[] { (l0.Version.NodeId, 1, true) }));
        });
        await _recorder.Received(1).RecordDraftAsync(page, "Layout: Content edited", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateItemAsync_WithRemap_StaleContainer_MovesNothing()
    {
        var main = await SeedPageZoneAsync(Guid.NewGuid());
        var layout = Item(main, component: "Layout", ordinal: 1);
        await SeedItemsAsync(layout);
        var layoutId = layout.Version.NodeId;
        var hero = await SeedOwnedZoneAsync(layoutId, "Hero");
        var h = Item(hero, ordinal: 1);
        await SeedItemsAsync(h);

        var ok = await NewService().UpdateItemAsync(LayoutUpdate(layoutId, "TwoColumnEqual"), HeroToTwoColumns, expectedVersionNumber: 7);

        Assert.Multiple(async () =>
        {
            Assert.That(ok, Is.False);
            Assert.That(await DraftOrderAsync(hero), Is.EqualTo(new[] { (h.Version.NodeId, 1, true) }));
            Assert.That(await NotesAsync(layoutId), Has.Count.EqualTo(1));
        });
        await _recorder.DidNotReceive().RecordDraftAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateItemAsync_WithRemap_AMoveFails_ReturnsFalseAndRecordsNothing()
    {
        var ctx = NewContext();
        var main = await SeedPageZoneAsync(Guid.NewGuid());
        var layout = Item(main, component: "Layout", ordinal: 1);
        await SeedItemsAsync(layout);
        var layoutId = layout.Version.NodeId;
        var hero = await SeedOwnedZoneAsync(layoutId, "Hero");
        await SeedItemsAsync(Item(hero, ordinal: 1));
        var itemStore = Substitute.For<IContentStore<ContentZoneItemDTO>>();
        itemStore.GetCurrentDraftAsync(layoutId, Arg.Any<CancellationToken>()).Returns(layout);
        itemStore.SaveDraftAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true), new ContentWriteResult(false, "boom"));

        var ok = await NewService(ctx, itemStore: itemStore).UpdateItemAsync(LayoutUpdate(layoutId, "TwoColumnEqual"), HeroToTwoColumns, 0);

        Assert.That(ok, Is.False);
        await _recorder.DidNotReceive().RecordDraftAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateItemAsync_WithRemap_StaleContainer_CreatesNoZones()
    {
        var main = await SeedPageZoneAsync(Guid.NewGuid());
        var layout = Item(main, component: "Layout", ordinal: 1);
        await SeedItemsAsync(layout);
        var layoutId = layout.Version.NodeId;
        var hero = await SeedOwnedZoneAsync(layoutId, "Hero");
        await SeedItemsAsync(Item(hero, ordinal: 1));

        var ok = await NewService().UpdateItemAsync(LayoutUpdate(layoutId, "TwoColumnEqual"), HeroToTwoColumns, expectedVersionNumber: 7);

        Assert.Multiple(async () =>
        {
            Assert.That(ok, Is.False);
            Assert.That(await NewService().GetByItemSlotAsync(layoutId, "Left"), Is.Null);
            Assert.That(await NewService().GetByItemSlotAsync(layoutId, "Right"), Is.Null);
        });
    }

    [Test]
    public async Task ReorderItemsAsync_RemovedItemKeepsItsPlace_SoNoOrdinalIsShared()
    {
        var page = Guid.NewGuid();
        var zone = await SeedPageZoneAsync(page);
        var a = Item(zone, ordinal: 1);
        var b = Item(zone, ordinal: 2, active: false);
        var c = Item(zone, ordinal: 3);
        await SeedItemsAsync(a, b, c);

        // The editor sends only the active items.
        var ok = await NewService().ReorderItemsAsync(zone, [c.Version.NodeId, a.Version.NodeId]);

        Assert.Multiple(async () =>
        {
            Assert.That(ok, Is.True);
            Assert.That(await DraftOrderAsync(zone), Is.EqualTo(new[]
            {
                (c.Version.NodeId, 1, true), (b.Version.NodeId, 2, false), (a.Version.NodeId, 3, true)
            }));
            Assert.That(await NotesAsync(b.Version.NodeId), Has.Count.EqualTo(1), "a removed item already in place is not rewritten");
        });
    }

    [Test]
    public async Task ReorderItemsAsync_RepeatedId_ReturnsFalse()
    {
        var zone = await SeedPageZoneAsync(Guid.NewGuid());
        var a = Item(zone, ordinal: 1);
        var b = Item(zone, ordinal: 2);
        await SeedItemsAsync(a, b);

        // The repeat's second save is stale, so the whole reorder fails (and rolls back on a real database).
        var ok = await NewService().ReorderItemsAsync(zone, [b.Version.NodeId, a.Version.NodeId, b.Version.NodeId]);

        Assert.That(ok, Is.False);
    }

    [Test]
    public async Task DeleteZoneAsync_ReturningAStrayDraftHomeFails_KeepsTheZoneAndItsItem()
    {
        var page = Guid.NewGuid();
        var main = await SeedPageZoneAsync(page, "Main");
        var doomed = Guid.NewGuid();
        await SeedZonesAsync(Zone(doomed, 0, "Doomed"));
        var itemNodeId = Guid.NewGuid();
        var live = Item(main, itemNodeId, version: 1, ordinal: 1, state: ContentVersionState.Published, currentDraft: false);
        var stray = Item(doomed, itemNodeId, version: 2, ordinal: 1, state: ContentVersionState.Draft);
        stray.Version.Node = live.Version.Node;
        await SeedItemsAsync(live, stray);
        var itemStore = Substitute.For<IContentStore<ContentZoneItemDTO>>();
        itemStore.RestoreAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(false, ContentStore<ContentZoneItemDTO>.StaleVersionMessage));

        var ok = await NewService(itemStore: itemStore).DeleteZoneAsync(doomed);

        Assert.Multiple(async () =>
        {
            Assert.That(ok, Is.False);
            Assert.That(await NewContext().Set<ContentZoneDTO>().AnyAsync(z => z.Version.NodeId == doomed), Is.True);
            Assert.That((await ItemStore(NewContext()).GetCurrentDraftAsync(itemNodeId))!.ContentZoneNodeId, Is.EqualTo(doomed));
        });
        await itemStore.DidNotReceive().DeleteVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteZoneAsync_DroppingAStrayVersionFails_KeepsTheZone()
    {
        // The item's current draft already went home; only an old version still sits in the zone.
        var main = await SeedPageZoneAsync(Guid.NewGuid(), "Main");
        var doomed = Guid.NewGuid();
        await SeedZonesAsync(Zone(doomed, 0, "Doomed"));
        var itemNodeId = Guid.NewGuid();
        var live = Item(main, itemNodeId, version: 1, ordinal: 1, state: ContentVersionState.Published, currentDraft: false);
        var old = Item(doomed, itemNodeId, version: 2, ordinal: 1, state: ContentVersionState.Draft, currentDraft: false);
        var current = Item(main, itemNodeId, version: 3, ordinal: 1, state: ContentVersionState.Draft);
        old.Version.Node = live.Version.Node;
        current.Version.Node = live.Version.Node;
        await SeedItemsAsync(live, old, current);
        var itemStore = Substitute.For<IContentStore<ContentZoneItemDTO>>();
        itemStore.DeleteVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        var ok = await NewService(itemStore: itemStore).DeleteZoneAsync(doomed);

        Assert.Multiple(async () =>
        {
            Assert.That(ok, Is.False);
            Assert.That(await NewContext().Set<ContentZoneDTO>().AnyAsync(z => z.Version.NodeId == doomed), Is.True);
        });
        await itemStore.DidNotReceive().RestoreAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void RemapOverloads_NullPlan_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => NewService().UpdateItemAsync(new ContentZoneItemDTO(), null!, null), Throws.ArgumentNullException);
            Assert.That(() => NewService().RestoreItemVersionAsync(Guid.NewGuid(), null, null, null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public async Task MergeUpdate_KeepsPlacementAndUnsuppliedMetadata()
    {
        var existing = Item(Guid.NewGuid(), ordinal: 3);
        existing.MetadataJson = "{\"AdminLabel\":\"a\"}";

        var merged = ContentZoneService.MergeUpdate(existing, new ContentZoneItemDTO { ComponentName = "X", ComponentPropertiesJson = "{}", IsActive = false });

        Assert.Multiple(() =>
        {
            Assert.That(merged.ComponentName, Is.EqualTo("X"));
            Assert.That(merged.Ordinal, Is.EqualTo(3));
            Assert.That(merged.MetadataJson, Is.EqualTo("{\"AdminLabel\":\"a\"}"));
            Assert.That(merged.IsActive, Is.False);
        });
    }

    /// <summary>A Layout with an old (view "Hero") version 0 and a current version 1, in <paramref name="zone"/>.</summary>
    private async Task<(Guid NodeId, Guid HistoricalVersionId)> SeedLayoutHistoryAsync(Guid zone)
    {
        var old = Item(zone, version: 0, ordinal: 1, component: "Layout", state: ContentVersionState.Archived, currentDraft: false);
        old.ComponentPropertiesJson = "{\"ViewName\":\"TwoColumnEqual\"}";
        var current = Item(zone, version: 1, ordinal: 1, component: "Layout");
        current.ComponentPropertiesJson = "{\"ViewName\":\"HeroWithColumns\"}";
        current.Version.Node = old.Version.Node;
        current.Version.NodeId = old.Version.NodeId;
        await SeedItemsAsync(old, current);
        return (old.Version.NodeId, old.VersionId);
    }

    [Test]
    public async Task RestoreItemVersionAsync_WithRemap_MovesNestedContent_InOneEntry()
    {
        var page = Guid.NewGuid();
        var main = await SeedPageZoneAsync(page);
        var (layoutId, versionId) = await SeedLayoutHistoryAsync(main);
        var hero = await SeedOwnedZoneAsync(layoutId, "Hero");
        var h = Item(hero, ordinal: 1);
        await SeedItemsAsync(h);

        var result = await NewService().RestoreItemVersionAsync(versionId, null, 1, HeroToTwoColumns);

        var left = (await NewService().GetByItemSlotAsync(layoutId, "Left"))!.ContentZoneNodeId;
        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo((true, (string?)null)));
            Assert.That(await DraftOrderAsync(left), Is.EqualTo(new[] { (h.Version.NodeId, 1, true) }));
            Assert.That((await NotesAsync(layoutId))[0], Is.EqualTo("Restored from version 0"));
        });
        await _recorder.Received(1).RecordDraftAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _recorder.Received(1).RecordDraftAsync(page, "Restored Layout from version 0; " + HeroToTwoColumns.HistoryNote, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RestoreItemVersionAsync_WithRemap_NothingToMove_RecordsThePlainNote()
    {
        var page = Guid.NewGuid();
        var main = await SeedPageZoneAsync(page);
        var (layoutId, versionId) = await SeedLayoutHistoryAsync(main);
        await SeedOwnedZoneAsync(layoutId, "Hero"); // empty

        var result = await NewService().RestoreItemVersionAsync(versionId, null, null, HeroToTwoColumns);

        Assert.That(result.Success, Is.True);
        await _recorder.Received(1).RecordDraftAsync(page, "Restored Layout from version 0", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RestoreItemVersionAsync_WithRemap_PlacementFails_MovesNothing()
    {
        var main = await SeedPageZoneAsync(Guid.NewGuid());
        var historical = Item(main, ordinal: 1, component: "Layout", state: ContentVersionState.Archived, currentDraft: false);
        var current = Item(main, historical.Version.NodeId, version: 1, ordinal: 1, component: "Layout");
        var hero = await SeedOwnedZoneAsync(historical.Version.NodeId, "Hero");
        await SeedItemsAsync(Item(hero, ordinal: 1));
        var itemStore = Substitute.For<IContentStore<ContentZoneItemDTO>>();
        itemStore.GetVersionAsync(historical.VersionId, Arg.Any<CancellationToken>()).Returns(historical);
        itemStore.GetCurrentDraftAsync(historical.Version.NodeId, Arg.Any<CancellationToken>()).Returns(current);
        itemStore.SaveDraftAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(false, "boom"));

        var result = await NewService(itemStore: itemStore).RestoreItemVersionAsync(historical.VersionId, null, null, HeroToTwoColumns);

        Assert.That(result, Is.EqualTo((false, "Failed to restore the widget.")));
        await itemStore.Received(1).SaveDraftAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    // ─── container queries ────────────────────────────────────────────────────

    [Test]
    public async Task GetOwnedSlotsWithItemsAsync_ListsSlotsHoldingAnyCurrentDraft()
    {
        var owner = Guid.NewGuid();
        var active = await SeedOwnedZoneAsync(owner, "Active");
        var removedOnly = await SeedOwnedZoneAsync(owner, "RemovedOnly");
        await SeedOwnedZoneAsync(owner, "Empty");
        var foreign = await SeedOwnedZoneAsync(Guid.NewGuid(), "Foreign");
        await SeedItemsAsync(Item(active), Item(removedOnly, active: false), Item(foreign));

        var slots = await NewService().GetOwnedSlotsWithItemsAsync(owner);

        Assert.That(slots, Is.EquivalentTo(new[] { "Active", "RemovedOnly" }));
    }

    [Test]
    public async Task GetDescendantItemNodeIdsAsync_WalksNestedZones_AnyVersion_WithACycleGuard()
    {
        var page = await SeedPageZoneAsync(Guid.NewGuid());
        var container = Item(page, component: "Layout");
        await SeedItemsAsync(container);
        var outer = await SeedOwnedZoneAsync(container.Version.NodeId, "Left");
        var inner = Item(outer, component: "Layout");
        var movedOut = Item(outer, version: 0, state: ContentVersionState.Archived, currentDraft: false);
        var movedOutNow = Item(page, movedOut.Version.NodeId, version: 1);
        movedOutNow.Version.Node = movedOut.Version.Node;
        var deleted = Item(outer);
        deleted.Version.Node.IsDeleted = true;
        await SeedItemsAsync(inner, movedOut, movedOutNow, deleted);
        var innerZone = await SeedOwnedZoneAsync(inner.Version.NodeId, "Main");
        var leaf = Item(innerZone);
        // An old version of the container inside its own nested zone must not loop the walk.
        var cyclic = Item(innerZone, container.Version.NodeId, version: 5, state: ContentVersionState.Archived, currentDraft: false);
        cyclic.Version.Node = null!;
        await SeedItemsAsync(leaf, cyclic);

        var ids = await NewService().GetDescendantItemNodeIdsAsync(container.Version.NodeId);

        Assert.That(ids, Is.EquivalentTo(new[] { inner.Version.NodeId, movedOut.Version.NodeId, leaf.Version.NodeId }));
    }

    [Test]
    public async Task GetItemOwnedZonesForPageAsync_ListsActiveOwnersZonesWithActiveWidgets()
    {
        var pageNodeId = Guid.NewGuid();
        var main = await SeedPageZoneAsync(pageNodeId);
        var layout = Item(main, component: "Layout");
        var removedLayout = Item(main, component: "Layout", active: false);
        await SeedItemsAsync(layout, removedLayout);
        var shown = await SeedOwnedZoneAsync(layout.Version.NodeId, "Column3");
        var onlyRemoved = await SeedOwnedZoneAsync(layout.Version.NodeId, "Column4");
        var underRemoved = await SeedOwnedZoneAsync(removedLayout.Version.NodeId, "Left");
        await SeedItemsAsync(Item(shown), Item(onlyRemoved, active: false), Item(underRemoved));

        var zones = await NewService(readContext: new TestReadContext(ContentReadMode.Draft)).GetItemOwnedZonesForPageAsync(pageNodeId);

        Assert.Multiple(async () =>
        {
            Assert.That(zones.Select(z => (z.ZoneNodeId, z.SlotName, z.Owner.Version.NodeId)),
                Is.EqualTo(new[] { (shown, "Column3", layout.Version.NodeId) }));
            Assert.That(await NewService().GetItemOwnedZonesForPageAsync(Guid.NewGuid()), Is.Empty);
        });
    }
}
