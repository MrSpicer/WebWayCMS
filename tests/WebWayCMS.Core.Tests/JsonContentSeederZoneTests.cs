using System.Text.Json;

using Microsoft.Extensions.Options;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Models.Page;
using WebWayCMS.Models.Shared;
using WebWayCMS.Services.ContentSeeding;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class JsonContentSeederZoneTests
{
    private static readonly Guid PageSeedId = Guid.Parse("22222222-0000-4000-8000-000000000001");
    private static readonly Guid ItemSeedId = Guid.Parse("44444444-0000-4000-8000-000000000001");
    private static readonly Guid ItemSeedId2 = Guid.Parse("44444444-0000-4000-8000-000000000002");
    private static readonly Guid ItemSeedId3 = Guid.Parse("44444444-0000-4000-8000-000000000003");
    private static readonly Guid PageNodeId = Guid.NewGuid();
    private static readonly Guid ZoneNodeId = Guid.NewGuid();
    private static readonly Guid ItemNodeId = Guid.NewGuid();

    private IAdminHandlerRegistry _registry = null!;
    private IContentSeedRecordService _records = null!;
    private IContentZoneService _zones = null!;
    private IAdminCrudHandler _child = null!;
    private IAdminCrudHandler _pages = null!;
    private IPageModel _pageModel = null!;
    private Dictionary<Guid, ContentSeedRecordDTO> _ledger = null!;
    private List<ContentSeedRecordDTO> _upserts = null!;
    private List<ContentZoneItemUpsertViewModel> _saved = null!;

    [SetUp]
    public void SetUp()
    {
        _registry = Substitute.For<IAdminHandlerRegistry>();
        _records = Substitute.For<IContentSeedRecordService>();
        _zones = Substitute.For<IContentZoneService>();
        _child = Substitute.For<IAdminCrudHandler>();
        _pages = Substitute.For<IAdminCrudHandler>();
        _pageModel = Substitute.For<IPageModel>();

        _ledger = new Dictionary<Guid, ContentSeedRecordDTO>();
        _upserts = new List<ContentSeedRecordDTO>();
        _saved = new List<ContentZoneItemUpsertViewModel>();

        _records.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => _ledger.GetValueOrDefault(c.Arg<Guid>()));
        _records.UpsertAsync(Arg.Any<ContentSeedRecordDTO>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(c =>
            {
                var r = c.Arg<ContentSeedRecordDTO>();
                _ledger[r.SeedId] = r;
                _upserts.Add(r);
            });

        _registry.GetHandler(Arg.Any<string>()).Returns((IAdminCrudHandler?)null);
        _registry.GetHandler("contentzoneitems").Returns(_child);
        _registry.GetHandler("pages").Returns(_pages);
        _child.ContentType.Returns("contentzoneitems");
        _pages.ContentType.Returns("pages");
        _pages.PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new AdminSaveResult(true));

        // Every page exists and is live unless a test says otherwise.
        _pageModel.GetVersionHistoryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(History(published: true));
        _pageModel.PublishZoneItemsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new PageZonePublishResult([], []));

        _child.CreateEmptyUpsertViewModel().Returns(_ => new ContentZoneItemUpsertViewModel());
        _child.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new AdminSaveResult(true, NodeId: ItemNodeId))
            .AndDoes(c => _saved.Add((ContentZoneItemUpsertViewModel)c.ArgAt<object>(0)));

        _zones.GetOrCreateByPageSlotAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Zone(ZoneNodeId), new OwnedZone()));
        _zones.GetOrCreateByItemSlotAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Zone(ZoneNodeId), new OwnedZone()));
        _zones.GetOrCreateByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Zone(ZoneNodeId));
        _zones.GetItemByNodeIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => Item(c.Arg<Guid>(), ZoneNodeId));
        _zones.GetParentPageNodeForZoneAsync(ZoneNodeId, Arg.Any<CancellationToken>()).Returns((Guid?)PageNodeId);

        // The page itself is already seeded.
        _ledger[PageSeedId] = new ContentSeedRecordDTO { SeedId = PageSeedId, NodeId = PageNodeId, ContentHash = "page" };
    }

    private static ContentZoneDTO Zone(Guid nodeId) =>
        new() { Version = new ContentVersion { Node = new ContentNode { Id = nodeId } } };

    private static ContentZoneItemDTO Item(Guid nodeId, Guid zoneNodeId) =>
        new ContentZoneItemDTO { Version = new ContentVersion { Node = new ContentNode { Id = nodeId } } }.In(zoneNodeId);

    private static VersionHistoryViewModel History(bool published, bool deleted = false) =>
        new() { Versions = [new VersionItemViewModel { IsPublished = published, IsDeleted = deleted }] };

    private static string PageZone(string slot = "Main") =>
        "{\"page\":\"@seed:" + PageSeedId + "\",\"slot\":\"" + slot + "\"}";

    private static string ZoneItem(
        Guid? id = null,
        string? zone = null,
        string fields = "{\"componentName\":\"FaqCount\",\"isActive\":true}",
        string? publish = null,
        string contentType = "contentzoneitems") =>
        "{\"id\":\"" + (id ?? ItemSeedId) + "\",\"contentType\":\"" + contentType + "\"" +
        (publish == null ? string.Empty : ",\"publish\":" + publish) +
        ",\"zone\":" + (zone ?? PageZone()) +
        ",\"fields\":" + fields + "}";

    private static string Document(params string[] items) => "{\"items\":[" + string.Join(",", items) + "]}";

    private static IContentSeedSourceProvider Provider(string json)
    {
        var provider = Substitute.For<IContentSeedSourceProvider>();
        provider.GetSources().Returns(new[] { new ContentSeedSource("zones.json", json) });
        return provider;
    }

    private JsonContentSeeder Seeder(string json, bool withZoneService = true, bool withPageModel = true) =>
        new(new[] { Provider(json) }, _registry, _records, Options.Create(new ContentSeedOptions()),
            zoneService: withZoneService ? _zones : null,
            pageModel: withPageModel ? _pageModel : null);

    private void SaveReturns(params Guid[] nodeIds)
    {
        var results = nodeIds.Select(id => new AdminSaveResult(true, NodeId: id)).ToArray();
        _child.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(results[0], results.Skip(1).ToArray())
            .AndDoes(c => _saved.Add((ContentZoneItemUpsertViewModel)c.ArgAt<object>(0)));
    }

    private void AssertNothingSaved()
    {
        _ = _child.DidNotReceive().SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
        Assert.That(_upserts.Where(r => r.SeedId != PageSeedId), Is.Empty);
    }

    [Test]
    public async Task Seed_PageSlotItem_LivePage_PublishesOnlyTheItem()
    {
        await Seeder(Document(ZoneItem(fields: "{\"componentName\":\"FaqCount\",\"componentPropertiesJson\":\"{}\",\"isActive\":true,\"contentZoneNodeId\":\"" + Guid.NewGuid() + "\"}"))).SeedAsync();

        await _zones.Received(1).GetOrCreateByPageSlotAsync(PageNodeId, "Main", Arg.Any<CancellationToken>());
        await _child.Received(1).SaveUpsertAsync(Arg.Is<object>(m => ((ContentZoneItemUpsertViewModel)m).ContentZoneNodeId == ZoneNodeId), Arg.Any<CancellationToken>());
        await _pageModel.Received(1).PublishZoneItemsAsync(
            PageNodeId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { ItemNodeId })), Arg.Any<CancellationToken>());
        // The page itself is never (re)published, so its other drafts stay pending.
        await _pages.DidNotReceive().PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _pageModel.DidNotReceive().PublishPageAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _child.DidNotReceive().MoveRelatedAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
        Assert.Multiple(() =>
        {
            Assert.That(_saved.Single().ComponentName, Is.EqualTo("FaqCount"));
            Assert.That(_saved.Single().IsActive, Is.True);
            // Placement comes from the locator, never from the fields.
            Assert.That(_saved.Single().ContentZoneNodeId, Is.EqualTo(ZoneNodeId));
            // Recorded first with an empty hash (draft), then with the real hash once the item published.
            Assert.That(_upserts.Select(r => r.ContentHash.Length == 0), Is.EqualTo(new[] { true, false }));
            Assert.That(_upserts.All(r => r.NodeId == ItemNodeId && r.SeedId == ItemSeedId), Is.True);
        });
    }

    [Test]
    public async Task Seed_SeveralItemsOnOnePage_PublishesThemInOneCall()
    {
        var second = Guid.NewGuid();
        SaveReturns(ItemNodeId, second);

        await Seeder(Document(ZoneItem(), ZoneItem(id: ItemSeedId2))).SeedAsync();

        await _pageModel.Received(1).PublishZoneItemsAsync(
            PageNodeId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { ItemNodeId, second })), Arg.Any<CancellationToken>());
        // The page's existence is looked up once (cached), and its publish state once more at the end.
        await _pageModel.Received(2).GetVersionHistoryAsync(PageNodeId, Arg.Any<CancellationToken>());
        Assert.That(_ledger[ItemSeedId2].ContentHash, Is.Not.Empty);
    }

    [Test]
    public async Task Seed_ZoneItemsApplyAfterTopLevelItems()
    {
        var pageSeed = Guid.Parse("22222222-0000-4000-8000-000000000009");
        var pageNode = Guid.NewGuid();
        _ledger.Remove(PageSeedId);
        _pages.SupportsVersionHistory.Returns(true);
        _pages.CreateEmptyUpsertViewModel().Returns(new object());
        _pages.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(new AdminSaveResult(true, NodeId: pageNode));
        var page = "{\"id\":\"" + pageSeed + "\",\"contentType\":\"pages\",\"publish\":false,\"fields\":{\"title\":\"P\"}}";
        var zone = "{\"page\":\"@seed:" + pageSeed + "\",\"slot\":\"Main\"}";

        // The zone item comes first in the file but still resolves in the first pass.
        await Seeder(Document(ZoneItem(zone: zone), page)).SeedAsync();

        Received.InOrder(() =>
        {
            _pages.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
            _zones.GetOrCreateByPageSlotAsync(pageNode, "Main", Arg.Any<CancellationToken>());
        });
        await _zones.Received(1).GetOrCreateByPageSlotAsync(pageNode, "Main", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Seed_TopLevelItemReferencingAZoneItem_ResolvesInTheSameRun()
    {
        var blocks = Substitute.For<IAdminCrudHandler>();
        var blockNode = Guid.NewGuid();
        _registry.GetHandler("blocks").Returns(blocks);
        blocks.SupportsVersionHistory.Returns(true);
        blocks.CreateEmptyUpsertViewModel().Returns(new object());
        blocks.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(new AdminSaveResult(true, NodeId: blockNode));
        var blockSeed = Guid.Parse("33333333-0000-4000-8000-000000000001");
        var block = "{\"id\":\"" + blockSeed + "\",\"contentType\":\"blocks\",\"publish\":false,\"fields\":{\"title\":\"@seed:" + ItemSeedId + "\"}}";

        await Seeder(Document(block, ZoneItem())).SeedAsync();

        await blocks.Received(1).SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
        Assert.That(_ledger[blockSeed].NodeId, Is.EqualTo(blockNode));
    }

    [Test]
    public async Task Seed_UnpublishedPage_ItemsStayDraftsAndRecordTheirHash()
    {
        _pageModel.GetVersionHistoryAsync(PageNodeId, Arg.Any<CancellationToken>()).Returns(History(published: false));

        await Seeder(Document(ZoneItem())).SeedAsync();

        await _pageModel.DidNotReceive().PublishZoneItemsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        await _pageModel.DidNotReceive().PublishPageAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _pages.DidNotReceive().PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        Assert.That(_ledger[ItemSeedId].ContentHash, Is.Not.Empty);
    }

    [Test]
    public async Task Seed_PageGoneByPublishTime_ItemsStayDrafts()
    {
        _pageModel.GetVersionHistoryAsync(PageNodeId, Arg.Any<CancellationToken>())
            .Returns(History(published: true), (VersionHistoryViewModel?)null);

        await Seeder(Document(ZoneItem())).SeedAsync();

        await _pageModel.DidNotReceive().PublishZoneItemsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        Assert.That(_ledger[ItemSeedId].ContentHash, Is.Not.Empty);
    }

    [Test]
    public async Task Seed_ItemPublishFails_KeepsEmptyHashRecord()
    {
        _pageModel.PublishZoneItemsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new PageZonePublishResult([], ["boom"]) { FailedItemNodeIds = [ItemNodeId] });

        await Seeder(Document(ZoneItem())).SeedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_ledger[ItemSeedId].NodeId, Is.EqualTo(ItemNodeId));
            Assert.That(_ledger[ItemSeedId].ContentHash, Is.Empty);
        });
    }

    [Test]
    public async Task Seed_RouteFailureOnPublishedItem_RecordsHashSoItIsNotRetried()
    {
        _pageModel.PublishZoneItemsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new PageZonePublishResult([], ["route collision"]));

        await Seeder(Document(ZoneItem())).SeedAsync();

        Assert.That(_ledger[ItemSeedId].ContentHash, Is.Not.Empty);
    }

    [Test]
    public async Task Seed_PublishFalse_RecordsHashWithoutPublishing()
    {
        // A publish:false item next to a publish:true one on the same page is not published with it.
        var second = Guid.NewGuid();
        SaveReturns(ItemNodeId, second);

        await Seeder(Document(ZoneItem(), ZoneItem(id: ItemSeedId2, publish: "false"))).SeedAsync();

        await _pageModel.Received(1).PublishZoneItemsAsync(
            PageNodeId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { ItemNodeId })), Arg.Any<CancellationToken>());
        Assert.That(_upserts.Single(r => r.SeedId == ItemSeedId2).ContentHash, Is.Not.Empty);
    }

    [Test]
    public async Task Seed_GlobalZone_UsesNameAndRecordsImmediately()
    {
        await Seeder(Document(ZoneItem(zone: "{\"name\":\"SiteFooter\"}"))).SeedAsync();

        await _zones.Received(1).GetOrCreateByNameAsync("SiteFooter", Arg.Any<CancellationToken>());
        await _pageModel.DidNotReceive().PublishZoneItemsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        Assert.That(_upserts.Single(r => r.SeedId == ItemSeedId).ContentHash, Is.Not.Empty);
    }

    [Test]
    public async Task Seed_GlobalZonePublishFalse_IsSkipped()
    {
        // Global-zone writes go live immediately, so an unpublished item cannot be honoured.
        await Seeder(Document(ZoneItem(zone: "{\"name\":\"SiteFooter\"}", publish: "false"))).SeedAsync();

        AssertNothingSaved();
    }

    [Test]
    public async Task Seed_NestedItemSlot_ResolvesOwnerItemSeededEarlierInTheRun()
    {
        var layoutNode = Guid.NewGuid();
        var nestedNode = Guid.NewGuid();
        var nestedZone = Guid.NewGuid();
        SaveReturns(layoutNode, nestedNode);
        _zones.GetOrCreateByItemSlotAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Zone(nestedZone), new OwnedZone()));
        var nested = ZoneItem(id: ItemSeedId2, zone: "{\"item\":\"@seed:" + ItemSeedId + "\",\"slot\":\"Sidebar\"}");

        // Listed before its owner: deferred once, then resolved against the owner's draft record.
        await Seeder(Document(nested, ZoneItem())).SeedAsync();

        await _zones.Received(1).GetOrCreateByItemSlotAsync(layoutNode, "Sidebar", Arg.Any<CancellationToken>());
        // The nested zone belongs to the owner's page, so both items publish with it.
        await _pageModel.Received(1).PublishZoneItemsAsync(
            PageNodeId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { layoutNode, nestedNode })), Arg.Any<CancellationToken>());
        Assert.That(_ledger[ItemSeedId2].NodeId, Is.EqualTo(nestedNode));
    }

    [Test]
    public async Task Seed_SeveralNestedItems_LookUpTheOwnersPageOnce()
    {
        var owner = Guid.NewGuid();
        var nestedZone = "{\"item\":\"" + owner + "\",\"slot\":\"Left\"}";

        await Seeder(Document(ZoneItem(zone: nestedZone), ZoneItem(id: ItemSeedId2, zone: nestedZone))).SeedAsync();

        await _zones.Received(1).GetParentPageNodeForZoneAsync(ZoneNodeId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Seed_NestedOwnerNotAZoneItem_IsSkipped()
    {
        var owner = Guid.NewGuid();
        _zones.GetItemByNodeIdAsync(owner, Arg.Any<CancellationToken>()).Returns((ContentZoneItemDTO?)null);

        await Seeder(Document(ZoneItem(zone: "{\"item\":\"" + owner + "\",\"slot\":\"Left\"}"))).SeedAsync();

        await _zones.DidNotReceive().GetOrCreateByItemSlotAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        AssertNothingSaved();
    }

    [Test]
    public async Task Seed_DeferredZoneItem_KeepsFileOrderWithinItsZone()
    {
        // A defers (its config references C, a later item in another zone); B shares A's zone, so it waits
        // for A instead of jumping ahead of it.
        var a = ZoneItem(fields: "{\"componentName\":\"A\",\"componentPropertiesJson\":\"@seed:" + ItemSeedId3 + "\",\"isActive\":true}");
        var b = ZoneItem(id: ItemSeedId2, fields: "{\"componentName\":\"B\",\"isActive\":true}");
        var c = ZoneItem(id: ItemSeedId3, zone: PageZone("Side"), fields: "{\"componentName\":\"C\",\"isActive\":true}");

        await Seeder(Document(a, b, c)).SeedAsync();

        Assert.That(_saved.Select(v => v.ComponentName), Is.EqualTo(new[] { "C", "A", "B" }));
    }

    [Test]
    public async Task Seed_RawGuidOwner_IsUsedAsIs()
    {
        var owner = Guid.NewGuid();

        await Seeder(Document(ZoneItem(zone: "{\"page\":\"" + owner + "\",\"slot\":\"Main\"}"))).SeedAsync();

        await _zones.Received(1).GetOrCreateByPageSlotAsync(owner, "Main", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Seed_PageOwnerNotAPage_IsSkipped()
    {
        var owner = Guid.NewGuid();
        _pageModel.GetVersionHistoryAsync(owner, Arg.Any<CancellationToken>()).Returns((VersionHistoryViewModel?)null);

        await Seeder(Document(ZoneItem(zone: "{\"page\":\"" + owner + "\",\"slot\":\"Main\"}"))).SeedAsync();

        await _zones.DidNotReceive().GetOrCreateByPageSlotAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        AssertNothingSaved();
    }

    [Test]
    public async Task Seed_PageOwnerDeleted_IsSkipped()
    {
        _pageModel.GetVersionHistoryAsync(PageNodeId, Arg.Any<CancellationToken>()).Returns(History(published: true, deleted: true));

        await Seeder(Document(ZoneItem())).SeedAsync();

        AssertNothingSaved();
    }

    [Test]
    public async Task Seed_UnresolvedLocatorReference_IsNotSavedOrRecorded()
    {
        _ledger.Remove(PageSeedId);

        await Seeder(Document(ZoneItem())).SeedAsync();

        AssertNothingSaved();
    }

    [TestCase("{}")]
    [TestCase("{\"slot\":\"Main\"}")]
    [TestCase("{\"page\":\"x\",\"name\":\"Footer\",\"slot\":\"Main\"}")]
    [TestCase("{\"page\":\"x\",\"item\":\"y\",\"slot\":\"Main\"}")]
    [TestCase("{\"page\":\"x\"}")]
    [TestCase("{\"item\":\"y\",\"slot\":\" \"}")]
    public async Task Seed_InvalidLocator_IsSkipped(string zone)
    {
        await Seeder(Document(ZoneItem(zone: zone))).SeedAsync();

        AssertNothingSaved();
    }

    [Test]
    public async Task Seed_OwnerNotAGuid_IsSkipped()
    {
        await Seeder(Document(ZoneItem(zone: "{\"page\":\"home\",\"slot\":\"Main\"}"))).SeedAsync();

        AssertNothingSaved();
    }

    [Test]
    public async Task Seed_ZoneLocatorOnAnotherContentType_IsSkipped()
    {
        await Seeder(Document(ZoneItem(contentType: "pages"))).SeedAsync();

        AssertNothingSaved();
        await _pages.DidNotReceive().SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Seed_LegacyContentZonesType_AppliesAsAZoneItem()
    {
        await Seeder(Document(ZoneItem(contentType: "contentzones"))).SeedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_saved.Single().ContentZoneNodeId, Is.EqualTo(ZoneNodeId));
            Assert.That(_ledger.ContainsKey(ItemSeedId), Is.True);
        });
    }

    [Test]
    public async Task Seed_NoZoneService_IsSkipped()
    {
        await Seeder(Document(ZoneItem()), withZoneService: false).SeedAsync();

        AssertNothingSaved();
    }

    [Test]
    public async Task Seed_NoPageModel_IsSkipped()
    {
        await Seeder(Document(ZoneItem()), withPageModel: false).SeedAsync();

        AssertNothingSaved();
    }

    [Test]
    public async Task Seed_NonObjectFields_IsSkipped()
    {
        await Seeder(Document(ZoneItem(fields: "[]"))).SeedAsync();

        AssertNothingSaved();
    }

    [Test]
    public async Task Seed_SaveFails_IsNotRecorded()
    {
        _child.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new AdminSaveResult(false, "bad"));

        await Seeder(Document(ZoneItem())).SeedAsync();

        Assert.That(_ledger.ContainsKey(ItemSeedId), Is.False);
        await _pageModel.DidNotReceive().PublishZoneItemsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Seed_SaveReturnsNoNodeId_IsNotRecorded()
    {
        _child.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new AdminSaveResult(true), new AdminSaveResult(true, NodeId: Guid.Empty));

        await Seeder(Document(ZoneItem(), ZoneItem(id: ItemSeedId2))).SeedAsync();

        Assert.That(_ledger.Keys, Is.EquivalentTo(new[] { PageSeedId }));
    }

    [Test]
    public async Task Seed_ChangedExistingItem_MergesOverStoredItem()
    {
        _ledger[ItemSeedId] = new ContentSeedRecordDTO { SeedId = ItemSeedId, NodeId = ItemNodeId, ContentHash = "old" };
        _child.GetUpsertViewModelAsync(ItemNodeId, Arg.Any<Microsoft.AspNetCore.Http.IQueryCollection>(), Arg.Any<CancellationToken>())
            .Returns(new ContentZoneItemUpsertViewModel { NodeId = ItemNodeId, ContentZoneNodeId = ZoneNodeId, ExpectedVersionNumber = 4, ComponentPropertiesJson = "{\"a\":1}" });

        await Seeder(Document(ZoneItem())).SeedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_saved.Single().NodeId, Is.EqualTo(ItemNodeId));
            // The stored version travels with the save, so a concurrent edit is never overwritten.
            Assert.That(_saved.Single().ExpectedVersionNumber, Is.EqualTo(4));
            Assert.That(_saved.Single().ComponentPropertiesJson, Is.EqualTo("{\"a\":1}"));
            Assert.That(_saved.Single().ComponentName, Is.EqualTo("FaqCount"));
        });
    }

    [Test]
    public async Task Seed_ExistingItemDeletedInAdmin_IsRecreated()
    {
        _ledger[ItemSeedId] = new ContentSeedRecordDTO { SeedId = ItemSeedId, NodeId = ItemNodeId, ContentHash = "old" };
        _zones.GetItemByNodeIdAsync(ItemNodeId, Arg.Any<CancellationToken>()).Returns((ContentZoneItemDTO?)null);

        await Seeder(Document(ZoneItem())).SeedAsync();

        _child.Received(1).CreateEmptyUpsertViewModel();
        await _child.DidNotReceive().MoveRelatedAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
        Assert.That(_saved.Single().NodeId, Is.Null);
    }

    [Test]
    public async Task Seed_ItemStoredInAnotherZoneOfThePage_IsMovedBeforeItIsSaved()
    {
        var otherZone = Guid.NewGuid();
        _ledger[ItemSeedId] = new ContentSeedRecordDTO { SeedId = ItemSeedId, NodeId = ItemNodeId, ContentHash = "old" };
        _zones.GetItemByNodeIdAsync(ItemNodeId, Arg.Any<CancellationToken>()).Returns(Item(ItemNodeId, otherZone));
        _zones.GetParentPageNodeForZoneAsync(otherZone, Arg.Any<CancellationToken>()).Returns((Guid?)PageNodeId);
        _child.MoveRelatedAsync(ZoneRelations.Zone, ItemNodeId, ZoneNodeId, int.MaxValue, null, Arg.Any<CancellationToken>())
            .Returns((true, (string?)null));

        await Seeder(Document(ZoneItem())).SeedAsync();

        Received.InOrder(() =>
        {
            _child.MoveRelatedAsync(ZoneRelations.Zone, ItemNodeId, ZoneNodeId, int.MaxValue, null, Arg.Any<CancellationToken>());
            _child.GetUpsertViewModelAsync(ItemNodeId, Arg.Any<Microsoft.AspNetCore.Http.IQueryCollection>(), Arg.Any<CancellationToken>());
            _child.SaveUpsertAsync(Arg.Is<object>(m => ((ContentZoneItemUpsertViewModel)m).ContentZoneNodeId == ZoneNodeId), Arg.Any<CancellationToken>());
        });
        Assert.That(_ledger[ItemSeedId].ContentHash, Is.Not.Empty);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Seed_ItemStoredOnAnotherPageOrGlobalZone_IsSkippedWithoutSaving(bool storedOnAnotherPage)
    {
        var otherZone = Guid.NewGuid();
        _ledger[ItemSeedId] = new ContentSeedRecordDTO { SeedId = ItemSeedId, NodeId = ItemNodeId, ContentHash = "old" };
        _zones.GetItemByNodeIdAsync(ItemNodeId, Arg.Any<CancellationToken>()).Returns(Item(ItemNodeId, otherZone));
        _zones.GetParentPageNodeForZoneAsync(otherZone, Arg.Any<CancellationToken>())
            .Returns(storedOnAnotherPage ? Guid.NewGuid() : (Guid?)null);

        await Seeder(Document(ZoneItem())).SeedAsync();

        await _child.DidNotReceive().MoveRelatedAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
        AssertNothingSaved();
        Assert.That(_ledger[ItemSeedId].ContentHash, Is.EqualTo("old"));
    }

    [Test]
    public async Task Seed_MoveFails_IsSkippedWithoutSaving()
    {
        var otherZone = Guid.NewGuid();
        _ledger[ItemSeedId] = new ContentSeedRecordDTO { SeedId = ItemSeedId, NodeId = ItemNodeId, ContentHash = "old" };
        _zones.GetItemByNodeIdAsync(ItemNodeId, Arg.Any<CancellationToken>()).Returns(Item(ItemNodeId, otherZone));
        _zones.GetParentPageNodeForZoneAsync(otherZone, Arg.Any<CancellationToken>()).Returns((Guid?)PageNodeId);
        _child.MoveRelatedAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns((false, "A widget cannot be moved into itself."));

        await Seeder(Document(ZoneItem())).SeedAsync();

        AssertNothingSaved();
        await _pageModel.DidNotReceive().PublishZoneItemsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void ContentSeedItem_WithoutZone_SerializesWithoutZoneKey()
    {
        // Keeps the content hash of every pre-existing (top-level) item unchanged.
        var json = JsonSerializer.Serialize(new ContentSeedItem { Id = ItemSeedId, ContentType = "pages", Fields = JsonDocument.Parse("{}").RootElement });

        Assert.That(json, Does.Not.Contain("Zone"));
    }
}
