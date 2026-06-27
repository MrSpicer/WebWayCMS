using Microsoft.EntityFrameworkCore;

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

    private ContentZoneContext NewContext() => TestContexts.ContentZone(_db);

    private ContentZoneService NewService() => new(NewContext());

    private static ContentZoneDTO Zone(Guid master, int version = 0, string name = "Zone",
        bool published = true, bool deleted = false)
    {
        var id = Guid.NewGuid();
        return new ContentZoneDTO
        {
            ContentId = id,
            Name = name,
            ContentMeta = new ContentDTO
            {
                Id = id,
                MasterId = master,
                Version = version,
                Title = name,
                IsPublished = published,
                IsDeleted = deleted
            }
        };
    }

    private static ContentZoneItemDTO Item(Guid zoneId, Guid master, int version = 0, int ordinal = 0,
        bool active = true, string component = "ContentBlock")
    {
        var id = Guid.NewGuid();
        return new ContentZoneItemDTO
        {
            ContentId = id,
            ContentZoneId = zoneId,
            Ordinal = ordinal,
            IsActive = active,
            ComponentName = component,
            ContentMeta = new ContentDTO
            {
                Id = id,
                MasterId = master,
                Version = version
            }
        };
    }

    private async Task SeedZonesAsync(params ContentZoneDTO[] zones)
    {
        await using var ctx = NewContext();
        ctx.ContentZones.AddRange(zones);
        await ctx.SaveChangesAsync();
    }

    private async Task SeedItemsAsync(params ContentZoneItemDTO[] items)
    {
        await using var ctx = NewContext();
        ctx.ContentZoneItems.AddRange(items);
        await ctx.SaveChangesAsync();
    }

    private async Task SeedAssignmentsAsync(params ContentZoneAssignmentDTO[] assignments)
    {
        await using var ctx = NewContext();
        ctx.ContentZoneAssignments.AddRange(assignments);
        await ctx.SaveChangesAsync();
    }

    [Test]
    public void Constructor_NullContext_Throws()
    {
        Assert.That(() => new ContentZoneService(null!), Throws.ArgumentNullException);
    }

    // --- GetByName ---

    [Test]
    public async Task GetByNameAsync_Whitespace_ReturnsNull()
    {
        Assert.That(await NewService().GetByNameAsync(" "), Is.Null);
    }

    [Test]
    public async Task GetByNameAsync_ReturnsPublishedLatestWithActiveItemsOrdered()
    {
        var m = Guid.NewGuid();
        var zone = Zone(m, 0, "Main");
        await SeedZonesAsync(zone);
        await SeedItemsAsync(
            Item(zone.ContentId, Guid.NewGuid(), ordinal: 2),
            Item(zone.ContentId, Guid.NewGuid(), ordinal: 1),
            Item(zone.ContentId, Guid.NewGuid(), ordinal: 3, active: false));

        var result = await NewService().GetByNameAsync("Main");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Items.Select(i => i.Ordinal), Is.EqualTo(new[] { 1, 2 }));
        });
    }

    [Test]
    public async Task GetByIdAsync_FoundAndNotFound()
    {
        var zone = Zone(Guid.NewGuid());
        await SeedZonesAsync(zone);

        Assert.Multiple(async () =>
        {
            Assert.That(await NewService().GetByIdAsync(zone.ContentId), Is.Not.Null);
            Assert.That(await NewService().GetByIdAsync(Guid.NewGuid()), Is.Null);
        });
    }

    [Test]
    public async Task GetAllAsync_ReturnsLatestNonDeletedOrderedByName()
    {
        var m1 = Guid.NewGuid();
        await SeedZonesAsync(
            Zone(m1, 0, "B"),
            Zone(m1, 1, "B"),
            Zone(Guid.NewGuid(), 0, "A"),
            Zone(Guid.NewGuid(), 0, "Z", deleted: true));

        var all = await NewService().GetAllAsync();

        Assert.That(all.Select(z => z.Name), Is.EqualTo(new[] { "A", "B" }));
    }

    // --- Create / Update / Delete zone ---

    [Test]
    public void CreateAsync_Null_Throws()
    {
        Assert.That(async () => await NewService().CreateAsync(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task CreateAsync_EmptyId_InitializesIdMasterAndDates()
    {
        var created = await NewService().CreateAsync(new ContentZoneDTO { Name = "N", ContentMeta = new ContentDTO { Title = "N" } });

        Assert.Multiple(() =>
        {
            Assert.That(created.ContentMeta.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(created.ContentId, Is.EqualTo(created.ContentMeta.Id));
            Assert.That(created.ContentMeta.MasterId, Is.EqualTo(created.ContentMeta.Id));
            Assert.That(created.ContentMeta.PublicationDate, Is.Not.EqualTo(default(DateTime)));
        });
    }

    [Test]
    public async Task CreateAsync_PresetIdAndPublicationDate_AreKept()
    {
        var id = Guid.NewGuid();
        var when = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var created = await NewService().CreateAsync(new ContentZoneDTO { Name = "N", ContentMeta = new ContentDTO { Id = id, Title = "N", PublicationDate = when } });

        Assert.Multiple(() =>
        {
            Assert.That(created.ContentMeta.Id, Is.EqualTo(id));
            Assert.That(created.ContentMeta.PublicationDate, Is.EqualTo(when));
        });
    }

    [Test]
    public void UpdateAsync_Null_Throws()
    {
        Assert.That(async () => await NewService().UpdateAsync(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task UpdateAsync_NonExistent_ReturnsFalse()
    {
        Assert.That(await NewService().UpdateAsync(Zone(Guid.NewGuid())), Is.False);
    }

    [Test]
    public async Task UpdateAsync_Existing_AddsNewVersion()
    {
        var m = Guid.NewGuid();
        var zone = Zone(m, 0, "Old");
        await SeedZonesAsync(zone);

        var ok = await NewService().UpdateAsync(new ContentZoneDTO { ContentId = zone.ContentMeta.Id, Name = "New", ContentMeta = new ContentDTO { Id = zone.ContentMeta.Id, MasterId = m, Title = "New" } });

        await using var verify = NewContext();
        var versions = await verify.ContentZones.Where(z => z.ContentMeta.MasterId == m).OrderBy(z => z.ContentMeta.Version).ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(versions, Has.Count.EqualTo(2));
            Assert.That(versions[1].Name, Is.EqualTo("New"));
        });
    }

    [Test]
    public async Task DeleteAsync_NotFoundAndFound()
    {
        var zone = Zone(Guid.NewGuid());
        await SeedZonesAsync(zone);

        Assert.Multiple(async () =>
        {
            Assert.That(await NewService().DeleteAsync(Guid.NewGuid()), Is.False);
            Assert.That(await NewService().DeleteAsync(zone.ContentId), Is.True);
        });
    }

    // --- Items ---

    [Test]
    public void AddItemAsync_Null_Throws()
    {
        Assert.That(async () => await NewService().AddItemAsync(Guid.NewGuid(), null!), Throws.ArgumentNullException);
    }

    [Test]
    public void AddItemAsync_ZoneNotFound_Throws()
    {
        Assert.That(async () => await NewService().AddItemAsync(Guid.NewGuid(), new ContentZoneItemDTO { ComponentName = "X" }),
            Throws.InvalidOperationException);
    }

    [Test]
    public async Task AddItemAsync_AutoAssignsIdAndOrdinal()
    {
        var zone = Zone(Guid.NewGuid());
        await SeedZonesAsync(zone);
        await SeedItemsAsync(Item(zone.ContentId, Guid.NewGuid(), ordinal: 5));

        var item = await NewService().AddItemAsync(zone.ContentId, new ContentZoneItemDTO { ComponentName = "ContentBlock" });

        Assert.Multiple(() =>
        {
            Assert.That(item.ContentMeta.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(item.ContentZoneId, Is.EqualTo(zone.ContentId));
            Assert.That(item.Ordinal, Is.EqualTo(6)); // max + 1
            Assert.That(item.ContentMeta.IsPublished, Is.True);
        });
    }

    [Test]
    public async Task AddItemAsync_PresetOrdinal_IsKept()
    {
        var zone = Zone(Guid.NewGuid());
        await SeedZonesAsync(zone);

        var item = await NewService().AddItemAsync(zone.ContentId, new ContentZoneItemDTO { ComponentName = "X", Ordinal = 9 });

        Assert.That(item.Ordinal, Is.EqualTo(9));
    }

    [Test]
    public void UpdateItemAsync_Null_Throws()
    {
        Assert.That(async () => await NewService().UpdateItemAsync(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task UpdateItemAsync_NonExistent_ReturnsFalse()
    {
        Assert.That(await NewService().UpdateItemAsync(Item(Guid.NewGuid(), Guid.NewGuid())), Is.False);
    }

    [Test]
    public async Task UpdateItemAsync_Existing_AddsNewVersion()
    {
        var zoneId = Guid.NewGuid();
        var m = Guid.NewGuid();
        var existing = Item(zoneId, m, 0, ordinal: 1, component: "Old");
        await SeedItemsAsync(existing);

        var ok = await NewService().UpdateItemAsync(new ContentZoneItemDTO { ContentId = existing.ContentMeta.Id, ComponentName = "New", IsActive = true });

        await using var verify = NewContext();
        var versions = await verify.ContentZoneItems.Where(i => i.ContentMeta.MasterId == m).OrderBy(i => i.ContentMeta.Version).ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(versions, Has.Count.EqualTo(2));
            Assert.That(versions[1].ComponentName, Is.EqualTo("New"));
            Assert.That(versions[1].Ordinal, Is.EqualTo(1), "ordinal preserved from existing");
        });
    }

    [Test]
    public async Task RemoveItemAsync_NotFoundAndFound()
    {
        var item = Item(Guid.NewGuid(), Guid.NewGuid());
        await SeedItemsAsync(item);

        Assert.Multiple(async () =>
        {
            Assert.That(await NewService().RemoveItemAsync(Guid.NewGuid()), Is.False);
            Assert.That(await NewService().RemoveItemAsync(item.ContentMeta.Id), Is.True);
        });
    }

    [Test]
    public async Task GetItemByIdAsync_FoundAndNotFound()
    {
        var item = Item(Guid.NewGuid(), Guid.NewGuid());
        await SeedItemsAsync(item);

        Assert.Multiple(async () =>
        {
            Assert.That(await NewService().GetItemByIdAsync(item.ContentMeta.Id), Is.Not.Null);
            Assert.That(await NewService().GetItemByIdAsync(Guid.NewGuid()), Is.Null);
        });
    }

    [Test]
    public async Task ReorderItemsAsync_AssignsSequentialOrdinals_IgnoringUnknownIds()
    {
        var zoneId = Guid.NewGuid();
        var a = Item(zoneId, Guid.NewGuid(), ordinal: 1);
        var b = Item(zoneId, Guid.NewGuid(), ordinal: 2);
        await SeedItemsAsync(a, b);

        var ok = await NewService().ReorderItemsAsync(zoneId, new List<Guid> { b.ContentMeta.Id, a.ContentMeta.Id, Guid.NewGuid() });

        await using var verify = NewContext();
        var items = await verify.ContentZoneItems.Where(i => i.ContentZoneId == zoneId).ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(items.Single(i => i.ContentId == b.ContentMeta.Id).Ordinal, Is.EqualTo(1));
            Assert.That(items.Single(i => i.ContentId == a.ContentMeta.Id).Ordinal, Is.EqualTo(2));
        });
    }

    // --- Page slot get/create ---

    [Test]
    public async Task GetByPageSlotAsync_FoundAndNotFound()
    {
        var pageMaster = Guid.NewGuid();
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Main", ContentZoneId = Guid.NewGuid(), ParentPageMasterId = pageMaster });

        Assert.Multiple(async () =>
        {
            Assert.That(await NewService().GetByPageSlotAsync(pageMaster, "Main"), Is.Not.Null);
            Assert.That(await NewService().GetByPageSlotAsync(pageMaster, "Other"), Is.Null);
        });
    }

    [Test]
    public async Task GetOrCreateByPageSlotAsync_NoAssignment_CreatesZoneAndAssignment()
    {
        var pageMaster = Guid.NewGuid();

        var (zone, assignment) = await NewService().GetOrCreateByPageSlotAsync(pageMaster, "Main");

        Assert.Multiple(() =>
        {
            Assert.That(zone.Name, Is.EqualTo("Main"));
            Assert.That(assignment.ParentPageMasterId, Is.EqualTo(pageMaster));
            Assert.That(assignment.ContentZoneId, Is.EqualTo(zone.ContentMeta.MasterId));
            Assert.That(assignment.ContentZone, Is.SameAs(zone));
        });
    }

    [Test]
    public async Task GetOrCreateByPageSlotAsync_ExistingAssignmentAndZone_ReturnsExisting()
    {
        var pageMaster = Guid.NewGuid();
        var zoneMaster = Guid.NewGuid();
        await SeedZonesAsync(Zone(zoneMaster, 0, "Main"));
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Main", ContentZoneId = zoneMaster, ParentPageMasterId = pageMaster });

        var (zone, assignment) = await NewService().GetOrCreateByPageSlotAsync(pageMaster, "Main");

        Assert.Multiple(() =>
        {
            Assert.That(zone.ContentMeta.MasterId, Is.EqualTo(zoneMaster));
            Assert.That(assignment.ParentPageMasterId, Is.EqualTo(pageMaster));
        });
    }

    [Test]
    public async Task GetOrCreateByPageSlotAsync_AssignmentButZoneDeleted_RechecksInTransaction()
    {
        // Assignment exists, but its zone is soft-deleted so the first lookup yields no latest zone.
        var pageMaster = Guid.NewGuid();
        var zoneMaster = Guid.NewGuid();
        await SeedZonesAsync(Zone(zoneMaster, 0, "Main", deleted: true));
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Main", ContentZoneId = zoneMaster, ParentPageMasterId = pageMaster });

        var (zone, assignment) = await NewService().GetOrCreateByPageSlotAsync(pageMaster, "Main");

        // Transaction re-check finds the assignment again; the (deleted) latest zone is null and returned.
        Assert.Multiple(() =>
        {
            Assert.That(assignment, Is.Not.Null);
            Assert.That(zone, Is.Null);
        });
    }

    // --- Zone slot get/create ---

    [Test]
    public async Task GetByZoneSlotAsync_FoundAndNotFound()
    {
        var parentZone = Guid.NewGuid();
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Sub", ContentZoneId = Guid.NewGuid(), ParentZoneId = parentZone });

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
            Assert.That(assignment.ParentZoneId, Is.EqualTo(parentZone));
            Assert.That(assignment.ContentZone, Is.SameAs(zone));
        });
    }

    [Test]
    public async Task GetOrCreateByZoneSlotAsync_ExistingAssignmentAndZone_ReturnsExisting()
    {
        var parentZone = Guid.NewGuid();
        var zoneMaster = Guid.NewGuid();
        await SeedZonesAsync(Zone(zoneMaster, 0, "Sub"));
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Sub", ContentZoneId = zoneMaster, ParentZoneId = parentZone });

        var (zone, _) = await NewService().GetOrCreateByZoneSlotAsync(parentZone, "Sub");

        Assert.That(zone.ContentMeta.MasterId, Is.EqualTo(zoneMaster));
    }

    [Test]
    public async Task GetOrCreateByZoneSlotAsync_AssignmentButZoneDeleted_RechecksInTransaction()
    {
        var parentZone = Guid.NewGuid();
        var zoneMaster = Guid.NewGuid();
        await SeedZonesAsync(Zone(zoneMaster, 0, "Sub", deleted: true));
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Sub", ContentZoneId = zoneMaster, ParentZoneId = parentZone });

        var (zone, assignment) = await NewService().GetOrCreateByZoneSlotAsync(parentZone, "Sub");

        Assert.Multiple(() =>
        {
            Assert.That(assignment, Is.Not.Null);
            Assert.That(zone, Is.Null);
        });
    }

    // --- GetOrCreateByName ---

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
            Assert.That(zone.ContentMeta.IsPublished, Is.True);
        });
    }

    // --- Queries by page / parent / counts ---

    [Test]
    public async Task GetAllAssignmentsForPageAsync_ReturnsPageAssignments()
    {
        var pageMaster = Guid.NewGuid();
        await SeedAssignmentsAsync(
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "A", ContentZoneId = Guid.NewGuid(), ParentPageMasterId = pageMaster },
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "B", ContentZoneId = Guid.NewGuid(), ParentPageMasterId = Guid.NewGuid() });

        var result = await NewService().GetAllAssignmentsForPageAsync(pageMaster);

        Assert.That(result.Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task GetAllByPageAsync_ReturnsAssignedZones()
    {
        var pageMaster = Guid.NewGuid();
        var zoneMaster = Guid.NewGuid();
        await SeedZonesAsync(Zone(zoneMaster, 0, "Main"));
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Main", ContentZoneId = zoneMaster, ParentPageMasterId = pageMaster });

        var zones = await NewService().GetAllByPageAsync(pageMaster);

        Assert.That(zones.Select(z => z.ContentMeta.MasterId), Is.EqualTo(new[] { zoneMaster }));
    }

    [Test]
    public async Task GetAllByParentZoneAsync_ReturnsAssignedZones()
    {
        var parentZone = Guid.NewGuid();
        var zoneMaster = Guid.NewGuid();
        await SeedZonesAsync(Zone(zoneMaster, 0, "Sub"));
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Sub", ContentZoneId = zoneMaster, ParentZoneId = parentZone });

        var zones = await NewService().GetAllByParentZoneAsync(parentZone);

        Assert.That(zones.Select(z => z.ContentMeta.MasterId), Is.EqualTo(new[] { zoneMaster }));
    }

    [Test]
    public async Task GetZoneIdsWithChildrenAsync_EmptyInput_ReturnsEmpty()
    {
        Assert.That(await NewService().GetZoneIdsWithChildrenAsync(Array.Empty<Guid>()), Is.Empty);
    }

    [Test]
    public async Task GetZoneIdsWithChildrenAsync_ReturnsParentsWithChildren()
    {
        var parentWithChild = Guid.NewGuid();
        var parentWithout = Guid.NewGuid();
        await SeedAssignmentsAsync(new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "Sub", ContentZoneId = Guid.NewGuid(), ParentZoneId = parentWithChild });

        var result = await NewService().GetZoneIdsWithChildrenAsync(new[] { parentWithChild, parentWithout });

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain(parentWithChild));
            Assert.That(result, Does.Not.Contain(parentWithout));
        });
    }

    [Test]
    public async Task GetAllVersionsAsync_OrderedDesc()
    {
        var m = Guid.NewGuid();
        await SeedZonesAsync(Zone(m, 0), Zone(m, 1), Zone(m, 2));

        var versions = await NewService().GetAllVersionsAsync(m);

        Assert.That(versions.Select(v => v.ContentMeta.Version), Is.EqualTo(new[] { 2, 1, 0 }));
    }

    [Test]
    public async Task GetAllItemVersionsAsync_OrderedDesc()
    {
        var zoneId = Guid.NewGuid();
        var m = Guid.NewGuid();
        await SeedItemsAsync(Item(zoneId, m, 0), Item(zoneId, m, 1));

        var versions = await NewService().GetAllItemVersionsAsync(m);

        Assert.That(versions.Select(v => v.ContentMeta.Version), Is.EqualTo(new[] { 1, 0 }));
    }

    [Test]
    public async Task GetAssignmentCountsByMasterIdAsync_EmptyInput_ReturnsEmpty()
    {
        Assert.That(await NewService().GetAssignmentCountsByMasterIdAsync(Array.Empty<Guid>()), Is.Empty);
    }

    [Test]
    public async Task GetAssignmentCountsByMasterIdAsync_CountsPerZone()
    {
        var zoneMaster = Guid.NewGuid();
        await SeedAssignmentsAsync(
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "A", ContentZoneId = zoneMaster, ParentPageMasterId = Guid.NewGuid() },
            new ContentZoneAssignmentDTO { Id = Guid.NewGuid(), SlotName = "B", ContentZoneId = zoneMaster, ParentPageMasterId = Guid.NewGuid() });

        var counts = await NewService().GetAssignmentCountsByMasterIdAsync(new[] { zoneMaster });

        Assert.That(counts[zoneMaster], Is.EqualTo(2));
    }
}