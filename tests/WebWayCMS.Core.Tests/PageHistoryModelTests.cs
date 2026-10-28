using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Models.PageHistory;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class PageHistoryModelTests
{
    private IPageHistoryService _history = null!;
    private IContentStore<PageDTO> _pageStore = null!;
    private IContentStore<ContentZoneDTO> _zoneStore = null!;
    private IWidgetRegistry _registry = null!;
    private PageHistoryModel _model = null!;

    private readonly Guid _pageNodeId = Guid.NewGuid();
    private readonly Guid _mainZone = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _history = Substitute.For<IPageHistoryService>();
        _pageStore = Substitute.For<IContentStore<PageDTO>>();
        _zoneStore = Substitute.For<IContentStore<ContentZoneDTO>>();
        _registry = Substitute.For<IWidgetRegistry>();
        _model = new PageHistoryModel(_history, _pageStore, _zoneStore, _registry);

        _pageStore.GetCurrentDraftAsync(_pageNodeId, Arg.Any<CancellationToken>()).Returns(Page("Current"));
        _zoneStore.GetCurrentDraftAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ContentZoneDTO?)null);
        _zoneStore.GetCurrentDraftAsync(_mainZone, Arg.Any<CancellationToken>()).Returns(new ContentZoneDTO { Name = "Main" });
    }

    private PageDTO Page(string title, Guid? versionId = null)
        => new()
        {
            VersionId = versionId ?? Guid.NewGuid(),
            ControllerName = "GenericPage",
            Version = new ContentVersion { NodeId = _pageNodeId, Title = title }
        };

    private static ContentZoneItemDTO Item(Guid nodeId, Guid zone, int ordinal = 1, string props = "{}", string? metadata = null, bool active = true)
        => new ContentZoneItemDTO
        {
            VersionId = Guid.NewGuid(),
            ComponentName = "Text",
            ComponentPropertiesJson = props,
            MetadataJson = metadata,
            IsActive = active,
            Version = new ContentVersion { NodeId = nodeId, VersionNumber = 1 }
        }.In(zone, ordinal);

    private PageSetSnapshot Entry(DateTime created, Guid pageVersionId, params (Guid Node, Guid Version)[] items)
        => new()
        {
            Id = Guid.NewGuid(),
            PageNodeId = _pageNodeId,
            PageVersionId = pageVersionId,
            CreatedUtc = created,
            ItemCount = items.Length,
            Items = items.Select(i => new PageSetSnapshotItem { ItemNodeId = i.Node, ItemVersionId = i.Version }).ToList()
        };

    [Test]
    public void Constructor_NullArgs_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new PageHistoryModel(null!, _pageStore, _zoneStore, _registry), Throws.ArgumentNullException);
            Assert.That(() => new PageHistoryModel(_history, null!, _zoneStore, _registry), Throws.ArgumentNullException);
            Assert.That(() => new PageHistoryModel(_history, _pageStore, null!, _registry), Throws.ArgumentNullException);
            Assert.That(() => new PageHistoryModel(_history, _pageStore, _zoneStore, null!), Throws.ArgumentNullException);
        });
    }

    // ─── index ────────────────────────────────────────────────────────────────

    [Test]
    public async Task GetIndexAsync_UnknownPage_ReturnsNull()
    {
        Assert.That(await _model.GetIndexAsync(Guid.NewGuid()), Is.Null);
    }

    [Test]
    public async Task GetIndexAsync_SummarizesEachEntryAgainstTheOneBefore()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        Guid aV1 = Guid.NewGuid(), aV2 = Guid.NewGuid(), bV1 = Guid.NewGuid(), cV1 = Guid.NewGuid();
        var first = Entry(new DateTime(2026, 1, 1), Guid.NewGuid(), (a, aV1), (b, bV1));
        var second = Entry(new DateTime(2026, 2, 1), Guid.NewGuid(), (a, aV2), (c, cV1));
        _history.ListPageAsync(_pageNodeId, 0, IPageHistoryService.DefaultPageSize, Arg.Any<CancellationToken>()).Returns(([second, first], 2));

        var index = (await _model.GetIndexAsync(_pageNodeId))!;

        var newest = index.Entries[0];
        var oldest = index.Entries[1];
        Assert.Multiple(() =>
        {
            Assert.That(index.PageTitle, Is.EqualTo("Current"));
            Assert.That(newest.Id, Is.EqualTo(second.Id));
            Assert.That((newest.Added, newest.Updated, newest.Removed, newest.PageUpdated, newest.IsLatest), Is.EqualTo((1, 1, 1, true, true)));
            Assert.That((oldest.Added, oldest.Updated, oldest.Removed, oldest.PageUpdated, oldest.IsLatest), Is.EqualTo((2, 0, 0, false, false)));
            Assert.That(oldest.ItemCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task GetIndexAsync_LaterPage_DiffsTheLastRowAgainstTheExtraEntry_WithoutShowingIt()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var oldest = Entry(new DateTime(2026, 1, 1), Guid.NewGuid(), (a, Guid.NewGuid()));
        var middle = Entry(new DateTime(2026, 2, 1), oldest.PageVersionId, (a, oldest.Items[0].ItemVersionId), (b, Guid.NewGuid()));
        var newest = Entry(new DateTime(2026, 3, 1), Guid.NewGuid(), (a, Guid.NewGuid()), (b, middle.Items[1].ItemVersionId));
        _history.ListPageAsync(_pageNodeId, 1, 2, Arg.Any<CancellationToken>()).Returns(([newest, middle, oldest], 5));

        var index = (await _model.GetIndexAsync(_pageNodeId, skip: 1, take: 2))!;

        Assert.Multiple(() =>
        {
            Assert.That(index.Entries.Select(e => e.Id), Is.EqualTo(new[] { newest.Id, middle.Id }));
            Assert.That(index.Entries.Any(e => e.IsLatest), Is.False);
            Assert.That((index.Entries[1].Added, index.Entries[1].Updated, index.Entries[1].PageUpdated), Is.EqualTo((1, 0, false)));
            Assert.That((index.Skip, index.Take, index.TotalCount), Is.EqualTo((1, 2, 5)));
            Assert.That((index.HasNewer, index.HasOlder), Is.EqualTo((true, true)));
        });
    }

    [Test]
    public async Task GetIndexAsync_LastPage_HasNoOlder_AndTheOldestRowHasNoPredecessor()
    {
        var only = Entry(new DateTime(2026, 1, 1), Guid.NewGuid(), (Guid.NewGuid(), Guid.NewGuid()));
        _history.ListPageAsync(_pageNodeId, 2, 2, Arg.Any<CancellationToken>()).Returns(([only], 3));

        var index = (await _model.GetIndexAsync(_pageNodeId, skip: 2, take: 2))!;

        Assert.Multiple(() =>
        {
            Assert.That(index.Entries.Single().Added, Is.EqualTo(1));
            Assert.That((index.HasNewer, index.HasOlder), Is.EqualTo((true, false)));
        });
    }

    [TestCase(-5, 0, 0, 1)]
    [TestCase(3, 1000, 3, IPageHistoryService.MaxPageSize)]
    public async Task GetIndexAsync_OutOfRangeSkipAndTake_AreClamped(int skip, int take, int expectedSkip, int expectedTake)
    {
        _history.ListPageAsync(_pageNodeId, expectedSkip, expectedTake, Arg.Any<CancellationToken>()).Returns(([], 0));

        var index = (await _model.GetIndexAsync(_pageNodeId, skip, take))!;

        Assert.Multiple(() =>
        {
            Assert.That((index.Skip, index.Take), Is.EqualTo((expectedSkip, expectedTake)));
            Assert.That(index.Entries, Is.Empty);
        });
        await _history.Received(1).ListPageAsync(_pageNodeId, expectedSkip, expectedTake, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetIndexAsync_CarriesKindAndNote_AndTellsMovedFromUpdated()
    {
        Guid moved = Guid.NewGuid(), edited = Guid.NewGuid(), zone = Guid.NewGuid();
        var v1 = Guid.NewGuid();
        var first = Entry(new DateTime(2026, 1, 1), Guid.NewGuid());
        first.Items =
        [
            new PageSetSnapshotItem { ItemNodeId = moved, ItemVersionId = Guid.NewGuid(), ZoneNodeId = zone, Ordinal = 1 },
            new PageSetSnapshotItem { ItemNodeId = edited, ItemVersionId = v1, ZoneNodeId = zone, Ordinal = 2 }
        ];
        var second = Entry(new DateTime(2026, 1, 2), first.PageVersionId) with { Kind = PageSetSnapshotKind.Published, Note = "Published" };
        second.Items =
        [
            new PageSetSnapshotItem { ItemNodeId = moved, ItemVersionId = Guid.NewGuid(), ZoneNodeId = zone, Ordinal = 3 },
            new PageSetSnapshotItem { ItemNodeId = edited, ItemVersionId = Guid.NewGuid(), ZoneNodeId = zone, Ordinal = 2 }
        ];
        _history.ListPageAsync(_pageNodeId, 0, IPageHistoryService.DefaultPageSize, Arg.Any<CancellationToken>()).Returns(([second, first], 2));

        var newest = (await _model.GetIndexAsync(_pageNodeId))!.Entries[0];

        Assert.Multiple(() =>
        {
            Assert.That(newest.Kind, Is.EqualTo(PageSetSnapshotKind.Published));
            Assert.That(newest.Note, Is.EqualTo("Published"));
            Assert.That((newest.Moved, newest.Updated, newest.PageUpdated), Is.EqualTo((1, 1, false)));
        });
    }

    // ─── entry ────────────────────────────────────────────────────────────────

    [Test]
    public async Task GetEntryAsync_UnknownPageEntryOrForeignEntry_ReturnsNull()
    {
        var foreign = Entry(DateTime.UtcNow, Guid.NewGuid()) with { PageNodeId = Guid.NewGuid() };
        _history.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetEntryAsync(Guid.NewGuid(), foreign.Id), Is.Null);
            Assert.That(await _model.GetEntryAsync(_pageNodeId, Guid.NewGuid()), Is.Null);
            Assert.That(await _model.GetEntryAsync(_pageNodeId, foreign.Id), Is.Null);
            Assert.That(await _model.GetCompareAsync(_pageNodeId, foreign.Id), Is.Null);
        });
    }

    [Test]
    public async Task GetEntryAsync_DiffsAgainstThePreviousEntry_WithZoneNamesAndLabels()
    {
        Guid kept = Guid.NewGuid(), added = Guid.NewGuid(), dropped = Guid.NewGuid();
        var orphanZone = Guid.NewGuid();
        var previousState = new PageSetState(Page("Old"), [Item(kept, _mainZone), Item(dropped, _mainZone, 2)]);
        var entryState = new PageSetState(Page("New"),
            [Item(kept, _mainZone, props: "{\"v\":2}", metadata: "{\"AdminLabel\":\"Hero\"}"), Item(added, orphanZone)]);
        var previous = Entry(new DateTime(2026, 1, 1), previousState.Page.VersionId);
        var entry = Entry(new DateTime(2026, 2, 1), entryState.Page.VersionId);
        _history.GetAsync(entry.Id, Arg.Any<CancellationToken>()).Returns(entry);
        _history.GetPreviousAsync(entry, Arg.Any<CancellationToken>()).Returns(previous);
        _history.GetSnapshotStateAsync(entry.Id, Arg.Any<CancellationToken>()).Returns(entryState);
        _history.GetSnapshotStateAsync(previous.Id, Arg.Any<CancellationToken>()).Returns(previousState);
        _history.ListAsync(_pageNodeId, Arg.Any<CancellationToken>()).Returns([entry, previous]);
        _history.GetLatestIdAsync(_pageNodeId, Arg.Any<CancellationToken>()).Returns(entry.Id);

        var vm = (await _model.GetEntryAsync(_pageNodeId, entry.Id))!;

        Assert.Multiple(() =>
        {
            Assert.That(vm.CapturedPageTitle, Is.EqualTo("New"));
            Assert.That(vm.PageChanged, Is.True);
            Assert.That(vm.PreviousEntryId, Is.EqualTo(previous.Id));
            Assert.That(vm.Entry.IsLatest, Is.True);
            Assert.That(vm.Items.Select(r => (r.ItemNodeId, r.Change)), Is.EquivalentTo(new[]
            {
                (kept, PageSetChange.Changed), (dropped, PageSetChange.Removed), (added, PageSetChange.Added)
            }));
            Assert.That(vm.Items.Single(r => r.ItemNodeId == kept).Label, Is.EqualTo("Hero"));
            Assert.That(vm.Items.Single(r => r.ItemNodeId == dropped).Label, Is.EqualTo("Text"));
            Assert.That(vm.Items.Single(r => r.ItemNodeId == kept).ZoneName, Is.EqualTo("Main"));
            Assert.That(vm.Items.Single(r => r.ItemNodeId == added).ZoneName, Is.EqualTo(PageHistoryModel.MissingZoneName));
            Assert.That(vm.Items[0].ZoneName, Is.EqualTo(PageHistoryModel.MissingZoneName), "rows sort by zone name");
        });
    }

    [Test]
    public async Task GetEntryAsync_FirstEntry_EverythingIsAdded()
    {
        var item = Guid.NewGuid();
        var state = new PageSetState(Page("First"), [Item(item, _mainZone)]);
        var entry = Entry(DateTime.UtcNow, state.Page.VersionId);
        var newer = Entry(DateTime.UtcNow.AddDays(1), Guid.NewGuid());
        _history.GetAsync(entry.Id, Arg.Any<CancellationToken>()).Returns(entry);
        _history.GetPreviousAsync(entry, Arg.Any<CancellationToken>()).Returns((PageSetSnapshot?)null);
        _history.GetSnapshotStateAsync(entry.Id, Arg.Any<CancellationToken>()).Returns(state);
        _history.ListAsync(_pageNodeId, Arg.Any<CancellationToken>()).Returns([newer, entry]);

        var vm = (await _model.GetEntryAsync(_pageNodeId, entry.Id))!;

        Assert.Multiple(() =>
        {
            Assert.That(vm.PreviousEntryId, Is.Null);
            Assert.That(vm.PageChanged, Is.False);
            Assert.That(vm.Entry.IsLatest, Is.False);
            Assert.That(vm.Items.Single().Change, Is.EqualTo(PageSetChange.Added));
        });
    }

    [Test]
    public async Task GetEntryAsync_TheNewestEntry_IsLatest_WithoutListingTheHistory()
    {
        var state = new PageSetState(Page("Only"), []);
        var entry = Entry(DateTime.UtcNow, state.Page.VersionId);
        _history.GetAsync(entry.Id, Arg.Any<CancellationToken>()).Returns(entry);
        _history.GetSnapshotStateAsync(entry.Id, Arg.Any<CancellationToken>()).Returns(state);
        _history.GetLatestIdAsync(_pageNodeId, Arg.Any<CancellationToken>()).Returns(entry.Id);

        var vm = (await _model.GetEntryAsync(_pageNodeId, entry.Id))!;

        Assert.That(vm.Entry.IsLatest, Is.True);
        await _history.DidNotReceive().ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetEntryAsync_CapturedStateMissing_ReturnsNull()
    {
        var entry = Entry(DateTime.UtcNow, Guid.NewGuid());
        _history.GetAsync(entry.Id, Arg.Any<CancellationToken>()).Returns(entry);
        _history.GetSnapshotStateAsync(entry.Id, Arg.Any<CancellationToken>()).Returns((PageSetState?)null);

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetEntryAsync(_pageNodeId, entry.Id), Is.Null);
            Assert.That(await _model.GetCompareAsync(_pageNodeId, entry.Id), Is.Null);
        });
    }

    [Test]
    public async Task GetCompareAsync_CurrentStateMissing_ReturnsNull()
    {
        var state = new PageSetState(Page("Then"), []);
        var entry = Entry(DateTime.UtcNow, state.Page.VersionId);
        _history.GetAsync(entry.Id, Arg.Any<CancellationToken>()).Returns(entry);
        _history.GetSnapshotStateAsync(entry.Id, Arg.Any<CancellationToken>()).Returns(state);
        _history.GetCurrentStateAsync(_pageNodeId, Arg.Any<CancellationToken>()).Returns((PageSetState?)null);

        Assert.That(await _model.GetCompareAsync(_pageNodeId, entry.Id), Is.Null);
    }

    // ─── compare ──────────────────────────────────────────────────────────────

    [Test]
    public async Task GetCompareAsync_DescribesChangesSinceTheEntry_AndCarriesTheStateToken()
    {
        Guid same = Guid.NewGuid(), later = Guid.NewGuid();
        var entryState = new PageSetState(Page("Then"), [Item(same, _mainZone)]);
        var current = new PageSetState(Page("Now"), [Item(same, _mainZone), Item(later, _mainZone, 2)]);
        var entry = Entry(DateTime.UtcNow, entryState.Page.VersionId);
        _history.GetAsync(entry.Id, Arg.Any<CancellationToken>()).Returns(entry);
        _history.GetSnapshotStateAsync(entry.Id, Arg.Any<CancellationToken>()).Returns(entryState);
        _history.GetCurrentStateAsync(_pageNodeId, Arg.Any<CancellationToken>()).Returns(current);
        _history.ListAsync(_pageNodeId, Arg.Any<CancellationToken>()).Returns([entry]);

        var vm = (await _model.GetCompareAsync(_pageNodeId, entry.Id))!;

        Assert.Multiple(() =>
        {
            Assert.That(vm.PageChanged, Is.True);
            Assert.That(vm.CapturedPageTitle, Is.EqualTo("Then"));
            Assert.That(vm.PageTitle, Is.EqualTo("Current"));
            Assert.That(vm.Items.Single(r => r.ItemNodeId == later).Change, Is.EqualTo(PageSetChange.Added));
            Assert.That(vm.Items.Single(r => r.ItemNodeId == same).Change, Is.EqualTo(PageSetChange.Unchanged));
            Assert.That(vm.StateToken, Is.EqualTo(PageSetEquality.StateToken(current.Page, current.Items)));
            Assert.That(vm.HasChanges, Is.True);
        });
    }

    [Test]
    public void CompareViewModel_HasChanges()
    {
        var unchanged = new PageHistoryItemRow { Change = PageSetChange.Unchanged };
        var moved = new PageHistoryItemRow { Change = PageSetChange.Moved };

        Assert.Multiple(() =>
        {
            Assert.That(new PageHistoryCompareViewModel { Items = [unchanged] }.HasChanges, Is.False);
            Assert.That(new PageHistoryCompareViewModel { Items = [unchanged, moved] }.HasChanges, Is.True);
            Assert.That(new PageHistoryCompareViewModel { PageChanged = true }.HasChanges, Is.True);
        });
    }

    // ─── revert / delete ──────────────────────────────────────────────────────

    [Test]
    public async Task RevertAsync_UnknownOrForeignEntry_ReturnsNotFound()
    {
        var foreign = Entry(DateTime.UtcNow, Guid.NewGuid()) with { PageNodeId = Guid.NewGuid() };
        _history.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.RevertAsync(_pageNodeId, Guid.NewGuid(), "t"), Is.EqualTo((false, IPageHistoryService.NotFoundMessage)));
            Assert.That(await _model.RevertAsync(_pageNodeId, foreign.Id, "t"), Is.EqualTo((false, IPageHistoryService.NotFoundMessage)));
        });
        await _history.DidNotReceive().RevertAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<RestorableMetadata>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RevertAsync_PassesTheTokenAndTheListGrandfatheringRule()
    {
        var entry = Entry(DateTime.UtcNow, Guid.NewGuid());
        _history.GetAsync(entry.Id, Arg.Any<CancellationToken>()).Returns(entry);
        RestorableMetadata? passed = null;
        _history.RevertAsync(entry.Id, "token", Arg.Do<RestorableMetadata>(m => passed = m), Arg.Any<CancellationToken>())
            .Returns((true, (string?)null));
        _registry.GetByName("Gone").Returns((WidgetRegistrationInfo?)null);

        var result = await _model.RevertAsync(_pageNodeId, entry.Id, "token");

        const string list = "{\"DisplayMode\":\"List\"}";
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo((true, (string?)null)));
            Assert.That(WidgetMetadataJson.Parse(passed!("Gone", list, "{}")).DisplayMode, Is.EqualTo(WidgetDisplayMode.Single));
        });
    }

    [Test]
    public async Task DeleteAsync_OnlyDeletesThePagesOwnEntry()
    {
        var entry = Entry(DateTime.UtcNow, Guid.NewGuid());
        var foreign = Entry(DateTime.UtcNow, Guid.NewGuid()) with { PageNodeId = Guid.NewGuid() };
        _history.GetAsync(entry.Id, Arg.Any<CancellationToken>()).Returns(entry);
        _history.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);
        _history.DeleteAsync(entry.Id, Arg.Any<CancellationToken>()).Returns(true);

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.DeleteAsync(_pageNodeId, Guid.NewGuid()), Is.False);
            Assert.That(await _model.DeleteAsync(_pageNodeId, foreign.Id), Is.False);
            Assert.That(await _model.DeleteAsync(_pageNodeId, entry.Id), Is.True);
        });
        await _history.DidNotReceive().DeleteAsync(foreign.Id, Arg.Any<CancellationToken>());
    }
}

[TestFixture]
public class PageSetComparerTests
{
    private static readonly Guid Zone = Guid.NewGuid();

    private static ContentZoneItemDTO Item(Guid node, Guid? zone = null, int ordinal = 1, string props = "{}", bool active = true)
        => new ContentZoneItemDTO
        {
            VersionId = Guid.NewGuid(),
            ComponentName = "Text",
            ComponentPropertiesJson = props,
            IsActive = active,
            Version = new ContentVersion { NodeId = node }
        }.In(zone ?? Zone, ordinal);

    private static PageDTO Page(string title) => new() { ControllerName = "GenericPage", Version = new ContentVersion { Title = title } };

    [Test]
    public void Compare_ClassifiesEveryItem()
    {
        Guid same = Guid.NewGuid(), edited = Guid.NewGuid(), rezoned = Guid.NewGuid(), reordered = Guid.NewGuid(),
            added = Guid.NewGuid(), removed = Guid.NewGuid(), deactivated = Guid.NewGuid();
        var from = new PageSetState(Page("T"),
            [Item(same), Item(edited), Item(rezoned), Item(reordered), Item(removed), Item(deactivated)]);
        var to = new PageSetState(Page("T"),
            [Item(same), Item(edited, props: "{\"x\":1}"), Item(rezoned, zone: Guid.NewGuid()), Item(reordered, ordinal: 5),
             Item(added), Item(deactivated, active: false)]);

        var diff = PageSetComparer.Compare(from, to);

        var changes = diff.Items.ToDictionary(d => d.Item.Version.NodeId, d => d.Change);
        Assert.Multiple(() =>
        {
            Assert.That(diff.PageChanged, Is.False);
            Assert.That(changes[same], Is.EqualTo(PageSetChange.Unchanged));
            Assert.That(changes[edited], Is.EqualTo(PageSetChange.Changed));
            Assert.That(changes[rezoned], Is.EqualTo(PageSetChange.Moved));
            Assert.That(changes[reordered], Is.EqualTo(PageSetChange.Moved));
            Assert.That(changes[added], Is.EqualTo(PageSetChange.Added));
            Assert.That(changes[removed], Is.EqualTo(PageSetChange.Removed));
            Assert.That(changes[deactivated], Is.EqualTo(PageSetChange.Removed), "an inactive item is not on the page");
        });
    }

    [Test]
    public void Compare_NoBaseline_EverythingAddedAndNoPageChange()
    {
        var diff = PageSetComparer.Compare(null, new PageSetState(Page("T"), [Item(Guid.NewGuid())]));

        Assert.Multiple(() =>
        {
            Assert.That(diff.PageChanged, Is.False);
            Assert.That(diff.Items.Single().Change, Is.EqualTo(PageSetChange.Added));
        });
    }

    [Test]
    public void Compare_PageFieldsDiffer_ReportsAPageChange()
    {
        var diff = PageSetComparer.Compare(new PageSetState(Page("A"), []), new PageSetState(Page("B"), []));

        Assert.That(diff.PageChanged, Is.True);
    }
}
