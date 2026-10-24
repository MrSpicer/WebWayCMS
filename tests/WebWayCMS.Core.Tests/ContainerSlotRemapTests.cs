using Microsoft.AspNetCore.Http;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.ContentZones;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Services;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class SlotRemapPlannerTests
{
    private static IEnumerable<TestCaseData> Cases()
    {
        // Same names keep their content; the rest pair by position; leftovers go to the last new slot.
        yield return new TestCaseData(new[] { "Hero", "Column1", "Column2", "Column3" }, new[] { "Left", "Right" },
                new[] { "Hero", "Column1", "Column2", "Column3" },
                new[] { "Hero>Left", "Column1>Right", "Column2>Right", "Column3>Right" })
            .SetName("Plan_FewerSlots_LeftoversGoToTheLastSlot");
        yield return new TestCaseData(new[] { "Left", "Right" }, new[] { "Column1", "Column2", "Column3" },
                new[] { "Left", "Right" }, new[] { "Left>Column1", "Right>Column2" })
            .SetName("Plan_MoreSlots_FillsTheFirstOnes");
        yield return new TestCaseData(new[] { "Column1", "Column2", "Column3" }, new[] { "Column1", "Column2", "Column3", "Column4" },
                new[] { "Column1", "Column2", "Column3" }, Array.Empty<string>())
            .SetName("Plan_SharedNames_MoveNothing");
        yield return new TestCaseData(new[] { "Main", "Sidebar" }, new[] { "Sidebar", "Main" },
                new[] { "Main", "Sidebar" }, Array.Empty<string>())
            .SetName("Plan_SwappedOrder_KeepsNames");
        yield return new TestCaseData(new[] { "Main", "Sidebar" }, new[] { "Header", "Main", "Footer" },
                new[] { "Main", "Sidebar" }, new[] { "Sidebar>Header" })
            .SetName("Plan_MixedSharedAndNew");
        yield return new TestCaseData(new[] { "Left", "Right" }, new[] { "Column1", "Column2" },
                new[] { "Right" }, new[] { "Right>Column2" })
            .SetName("Plan_OnlySlotsWithContentMove");
        yield return new TestCaseData(new[] { "Left", "Left", "Right" }, new[] { "Main", "Main" },
                new[] { "Left", "Right" }, new[] { "Left>Main", "Right>Main" })
            .SetName("Plan_DuplicatesAreIgnored");
        yield return new TestCaseData(new[] { "Left" }, Array.Empty<string>(), new[] { "Left" }, Array.Empty<string>())
            .SetName("Plan_NewViewWithoutSlots_MovesNothing");
    }

    [TestCaseSource(nameof(Cases))]
    public void Plan(string[] oldSlots, string[] newSlots, string[] withContent, string[] expected)
    {
        var moves = SlotRemapPlanner.Plan(oldSlots, newSlots, withContent);

        Assert.That(moves.Select(m => $"{m.FromSlot}>{m.ToSlot}"), Is.EqualTo(expected));
    }
}

[TestFixture]
public class ContainerSlotRemapTests
{
    private IContentZoneService _service = null!;
    private IContentStore<ContentZoneItemDTO> _itemStore = null!;
    private IWidgetRegistry _registry = null!;
    private IWidgetSlotDiscovery _discovery = null!;
    private ContentZoneModel _model = null!;
    private Guid _layoutId;

    [SetUp]
    public void SetUp()
    {
        _service = Substitute.For<IContentZoneService>();
        _itemStore = Substitute.For<IContentStore<ContentZoneItemDTO>>();
        _registry = Substitute.For<IWidgetRegistry>();
        _registry.GetByName("Layout").Returns(new WidgetRegistrationInfo { Name = "Layout", ConfigurationTypeName = typeof(SampleWidgetConfig).FullName });
        _discovery = Substitute.For<IWidgetSlotDiscovery>();
        _model = new ContentZoneModel(_service, Substitute.For<IContentStore<ContentZoneDTO>>(), _itemStore, _registry,
            Substitute.For<IViewDiscoveryService>(), Substitute.For<ICMSRouteService>(), Substitute.For<IRouteRegistrationService>(),
            Substitute.For<IPageHistoryService>(), _discovery, Substitute.For<IChangeSetScope>());

        _layoutId = Guid.NewGuid();
        _service.GetOwnedSlotsWithItemsAsync(_layoutId, Arg.Any<CancellationToken>()).Returns(new List<string> { "Hero", "Column1" });
        _service.GetDescendantItemNodeIdsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new List<Guid>());
        _service.UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>(), Arg.Any<int?>()).Returns(true);
        _service.UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<SlotRemapPlan>(), Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(true);
        DiscoverSlots("HeroWithColumns", "Hero", "Column1", "Column2", "Column3");
        DiscoverSlots("TwoColumnEqual", "Left", "Right");
    }

    private void DiscoverSlots(string viewName, params string[] slots)
        => _discovery.DiscoverSlotsAsync(_layoutId, "Layout",
                Arg.Is<object>(o => o is SampleWidgetConfig && ((SampleWidgetConfig)o).ViewName == viewName), Arg.Any<CancellationToken>())
            .Returns(slots);

    private ContentZoneItemDTO Layout(string viewName, string? metadata = "{}", bool active = true, int version = 2) => new()
    {
        VersionId = Guid.NewGuid(),
        ComponentName = "Layout",
        ComponentPropertiesJson = $"{{\"ViewName\":\"{viewName}\"}}",
        MetadataJson = metadata,
        IsActive = active,
        Version = new ContentVersion { NodeId = _layoutId, Node = new ContentNode { Id = _layoutId }, VersionNumber = version }
    };

    private void Existing(ContentZoneItemDTO item) => _itemStore.GetCurrentDraftAsync(_layoutId, Arg.Any<CancellationToken>()).Returns(item);

    [Test]
    public async Task UpdateItemAsync_ViewChangeStrandingContent_SavesWithThePlan()
    {
        Existing(Layout("HeroWithColumns", "{\"AdminLabel\":\"Promo\"}"));
        var update = Layout("TwoColumnEqual", metadata: null);

        var ok = await _model.UpdateItemAsync(update, default, 2);

        Assert.That(ok, Is.True);
        await _service.Received(1).UpdateItemAsync(update,
            Arg.Is<SlotRemapPlan>(p => p.Moves.SequenceEqual(new[] { new SlotMove("Hero", "Left"), new SlotMove("Column1", "Right") })
                && p.HistoryNote == "Promo: layout changed; content moved (Hero → Left, Column1 → Right)"),
            2, Arg.Any<CancellationToken>());
        // Both views render with the item's id and metadata attached, as the zone would invoke them.
        await _discovery.Received(2).DiscoverSlotsAsync(_layoutId, "Layout",
            Arg.Is<object>(o => ((SampleWidgetConfig)o).ZoneItemNodeId == _layoutId && ((SampleWidgetConfig)o).Metadata!.AdminLabel == "Promo"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateItemAsync_NoAdminLabel_NotesTheComponentName()
    {
        Existing(Layout("HeroWithColumns"));

        await _model.UpdateItemAsync(Layout("TwoColumnEqual"));

        await _service.Received(1).UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(),
            Arg.Is<SlotRemapPlan>(p => p.HistoryNote.StartsWith("Layout: layout changed")), null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateItemAsync_NothingToRemap_IsAPlainSave()
    {
        // No node id; an unknown item; a removal; an unchanged save; no zone holding widgets; failed discovery
        // (old or new); no moves.
        var noNode = Layout("TwoColumnEqual");
        noNode.Version.Node = null!;
        await _model.UpdateItemAsync(noNode);

        await _model.UpdateItemAsync(Layout("TwoColumnEqual"));

        Existing(Layout("HeroWithColumns"));
        await _model.UpdateItemAsync(Layout("TwoColumnEqual", active: false));
        await _model.UpdateItemAsync(Layout("HeroWithColumns"));

        _service.GetOwnedSlotsWithItemsAsync(_layoutId, Arg.Any<CancellationToken>()).Returns(new List<string>(), new List<string> { "Hero" });
        await _model.UpdateItemAsync(Layout("TwoColumnEqual"));

        DiscoverSlots("Unknown", null!);
        _service.GetOwnedSlotsWithItemsAsync(_layoutId, Arg.Any<CancellationToken>()).Returns(new List<string> { "Hero" });
        await _model.UpdateItemAsync(Layout("Unknown"));
        Existing(Layout("Unknown"));
        await _model.UpdateItemAsync(Layout("TwoColumnEqual"));

        Existing(Layout("HeroWithColumns"));
        DiscoverSlots("Same", "Hero", "Column1");
        await _model.UpdateItemAsync(Layout("Same"));

        await _service.Received(8).UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>(), Arg.Any<int?>());
        await _service.DidNotReceive().UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<SlotRemapPlan>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
        Assert.That(() => _model.UpdateItemAsync(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task UpdateItemAsync_WithoutWidgetsInItsZones_DoesNotRender()
    {
        _service.GetOwnedSlotsWithItemsAsync(_layoutId, Arg.Any<CancellationToken>()).Returns(new List<string>());
        Existing(Layout("HeroWithColumns"));

        await _model.UpdateItemAsync(Layout("TwoColumnEqual"));

        await _discovery.DidNotReceive().DiscoverSlotsAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RestoreItemVersionAsync_ToAnotherView_RestoresWithThePlan()
    {
        var historical = Layout("TwoColumnEqual", "{\"AdminLabel\":\"Old\"}", version: 0);
        _itemStore.GetVersionAsync(historical.VersionId, Arg.Any<CancellationToken>()).Returns(historical);
        Existing(Layout("HeroWithColumns"));
        _service.RestoreItemVersionAsync(historical.VersionId, historical.MetadataJson, 2, Arg.Any<SlotRemapPlan>(), Arg.Any<CancellationToken>())
            .Returns((true, (string?)null));

        var result = await _model.RestoreItemVersionAsync(historical.VersionId, null, 2);

        Assert.That(result.Success, Is.True);
        // The remap is planned against — and the service saves — the same document the restore stores
        // (the version's own metadata when the caller supplies none), never the raw null.
        await _service.Received(1).RestoreItemVersionAsync(historical.VersionId, historical.MetadataJson, 2,
            Arg.Is<SlotRemapPlan>(p => p.HistoryNote.StartsWith("Old: layout changed") && p.Moves.Count == 2), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RestoreItemVersionAsync_SuppliedOrMissingMetadata_AndNothingToRemap_IsAPlainRestore()
    {
        var missing = Guid.NewGuid();
        var orphan = Layout("TwoColumnEqual", version: 0);
        _itemStore.GetVersionAsync(orphan.VersionId, Arg.Any<CancellationToken>()).Returns(orphan);
        _itemStore.GetCurrentDraftAsync(_layoutId, Arg.Any<CancellationToken>()).Returns((ContentZoneItemDTO?)null, Layout("TwoColumnEqual"), Layout("TwoColumnEqual"));
        var sameView = Layout("TwoColumnEqual", metadata: null, version: 0);
        _itemStore.GetVersionAsync(sameView.VersionId, Arg.Any<CancellationToken>()).Returns(sameView);

        await _model.RestoreItemVersionAsync(missing, null, null);
        await _model.RestoreItemVersionAsync(orphan.VersionId, null, null);
        await _model.RestoreItemVersionAsync(sameView.VersionId, "{}", null);
        await _model.RestoreItemVersionAsync(sameView.VersionId, null, null);

        await _service.Received(4).RestoreItemVersionAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
        await _service.DidNotReceive().RestoreItemVersionAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<SlotRemapPlan>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetUnplacedCandidatesAsync_LabelsByWidgetAndSlot()
    {
        var pageId = Guid.NewGuid();
        var zone = Guid.NewGuid();
        var labelled = Layout("HeroWithColumns", "{\"AdminLabel\":\"Promo\"}");
        _service.GetItemOwnedZonesForPageAsync(pageId, Arg.Any<CancellationToken>())
            .Returns(new List<ItemOwnedZone> { new(zone, "Column3", labelled), new(zone, "Left", Layout("TwoColumnEqual")) });

        var candidates = await _model.GetUnplacedCandidatesAsync(pageId);

        Assert.That(candidates, Is.EqualTo(new[]
        {
            new UnplacedZoneCandidate(zone, _layoutId, "Promo › Column3"),
            new UnplacedZoneCandidate(zone, _layoutId, "Layout › Left")
        }));
    }

    [Test]
    public void Constructor_NullSlotDiscovery_Throws()
    {
        Assert.That(() => new ContentZoneModel(_service, Substitute.For<IContentStore<ContentZoneDTO>>(), _itemStore, _registry,
            Substitute.For<IViewDiscoveryService>(), Substitute.For<ICMSRouteService>(), Substitute.For<IRouteRegistrationService>(),
            Substitute.For<IPageHistoryService>(), null!, Substitute.For<IChangeSetScope>()), Throws.ArgumentNullException);
    }

    // --- combined history ---

    private static ContentZoneItemDTO Version(Guid nodeId, int number, DateTime createdUtc, bool current, string? metadata = null) => new()
    {
        VersionId = Guid.NewGuid(),
        ComponentName = "Block",
        MetadataJson = metadata,
        Version = new ContentVersion
        {
            NodeId = nodeId,
            Node = new ContentNode { Id = nodeId },
            VersionNumber = number,
            CreatedUtc = createdUtc,
            IsCurrentDraft = current
        }
    };

    [Test]
    public async Task ChildHandler_ContainerHistory_MergesNestedWidgetsNewestFirst()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var nested = Guid.NewGuid();
        var gone = Guid.NewGuid();
        var own0 = Layout("A", version: 0);
        own0.Version.CreatedUtc = t;
        var own1 = Layout("B", "{\"AdminLabel\":\"Promo\"}", version: 1);
        own1.Version.CreatedUtc = t.AddMinutes(3);
        own1.Version.IsCurrentDraft = true;
        _itemStore.GetAllVersionsAsync(_layoutId, Arg.Any<CancellationToken>()).Returns(new List<ContentZoneItemDTO> { own1, own0 });
        _itemStore.GetAllVersionsAsync(nested, Arg.Any<CancellationToken>()).Returns(new List<ContentZoneItemDTO>
        {
            Version(nested, 2, t.AddMinutes(3), current: true, "{\"AdminLabel\":\"Hero text\"}"),
            Version(nested, 0, t.AddMinutes(1), current: false)
        });
        _itemStore.GetAllVersionsAsync(gone, Arg.Any<CancellationToken>()).Returns(new List<ContentZoneItemDTO>());
        _service.GetDescendantItemNodeIdsAsync(_layoutId, Arg.Any<CancellationToken>()).Returns(new List<Guid> { nested, gone });

        var vm = (await _model.ChildHandler!.GetChildVersionHistoryViewModelAsync("k", _layoutId))!;

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsCombined, Is.True);
            Assert.That(vm.ItemTitle, Is.EqualTo("Promo"));
            Assert.That(vm.CurrentVersionNumber, Is.EqualTo(1));
            Assert.That(vm.Versions.Select(v => (v.NodeId, v.Version)), Is.EqualTo(new[]
            {
                (nested, 2), (_layoutId, 1), (nested, 0), (_layoutId, 0)
            }), "newest first; a tie goes to the higher version number");
            Assert.That(vm.Versions.Select(v => v.WidgetLabel), Is.EqualTo(new[]
            {
                "Hero text", "Promo (this container)", "Hero text", "Promo (this container)"
            }));
            Assert.That(vm.Versions.Select(v => v.IsLatest), Is.EqualTo(new[] { true, true, false, false }));
            Assert.That(vm.Versions.Select(v => v.NodeCurrentVersionNumber), Is.EqualTo(new int?[] { 2, 1, 2, 1 }));
        });
    }

    [Test]
    public async Task ChildHandler_PlainWidgetHistory_IsNotCombinedAndKeepsItsOrder()
    {
        var own0 = Layout("A", version: 0);
        var own1 = Layout("B", version: 1);
        _itemStore.GetAllVersionsAsync(_layoutId, Arg.Any<CancellationToken>()).Returns(new List<ContentZoneItemDTO> { own0, own1 });

        var vm = (await _model.ChildHandler!.GetChildVersionHistoryViewModelAsync("k", _layoutId))!;

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsCombined, Is.False);
            Assert.That(vm.Versions.Select(v => v.Version), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(vm.Versions.Select(v => v.NodeCurrentVersionNumber), Is.All.Null);
        });
    }
}

[TestFixture]
public class ZoneEditRenderLogTests
{
    [Test]
    public void For_CreatesOncePerRequest()
    {
        var context = new DefaultHttpContext();

        var log = ZoneEditRenderLog.For(context);

        Assert.Multiple(() =>
        {
            Assert.That(ZoneEditRenderLog.For(context), Is.SameAs(log));
            Assert.That(context.Items[ZoneEditRenderLog.ItemsKey], Is.SameAs(log));
            Assert.That(ZoneEditRenderLog.For(new DefaultHttpContext()), Is.Not.SameAs(log));
            Assert.That(() => ZoneEditRenderLog.For(null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void NextUnplaced_PrefersRenderedOwners_ThenTheRest_SkippingRenderedAndListed()
    {
        var log = new ZoneEditRenderLog();
        var renderedOwner = Guid.NewGuid();
        var hiddenOwner = Guid.NewGuid();
        var renderedZone = Guid.NewGuid();
        log.ItemRendered(renderedOwner);
        log.ZoneRendered(renderedZone);
        var shown = new UnplacedZoneCandidate(renderedZone, renderedOwner, "shown");
        var stranded = new UnplacedZoneCandidate(Guid.NewGuid(), renderedOwner, "stranded");
        var underHidden = new UnplacedZoneCandidate(Guid.NewGuid(), hiddenOwner, "under hidden");
        var all = new[] { shown, stranded, underHidden };
        var listed = new HashSet<Guid>();

        var first = log.NextUnplaced(all, listed);
        listed.Add(stranded.ZoneNodeId);
        var second = log.NextUnplaced(all, listed);
        listed.Add(underHidden.ZoneNodeId);
        var third = log.NextUnplaced(all, listed);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(new[] { stranded }));
            Assert.That(second, Is.EqualTo(new[] { underHidden }));
            Assert.That(third, Is.Empty);
            Assert.That(log.WasRendered(renderedZone), Is.True, "the zone the editor rendered is skipped on a re-check");
            Assert.That(log.WasRendered(stranded.ZoneNodeId), Is.False);
            Assert.That(() => log.NextUnplaced(null!, listed), Throws.ArgumentNullException);
            Assert.That(() => log.NextUnplaced(all, null!), Throws.ArgumentNullException);
        });
    }
}
