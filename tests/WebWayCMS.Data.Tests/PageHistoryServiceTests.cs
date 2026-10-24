using Microsoft.EntityFrameworkCore;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class PageHistoryServiceTests
{
    private string _db = null!;

    [SetUp]
    public void SetUp() => _db = TestContexts.NewDb();

    private CmsDbContext NewContext() => TestContexts.Cms(_db);

    private static readonly RestorableMetadata KeepMetadata = (_, historical, _) => historical;

    /// <summary>One request's worth of services sharing a fresh context, like a DI scope.</summary>
    private sealed record Graph(
        CmsDbContext Context,
        ChangeSetScope Scope,
        ContentStore<PageDTO> Pages,
        ContentStore<ContentZoneItemDTO> Items,
        ContentZoneService Zones,
        PageHistoryService History,
        PageZoneTree Tree,
        PageHistoryRecorder Recorder);

    /// <summary>A context whose save fails whenever it would write a page-history snapshot.</summary>
    private sealed class SnapshotRejectingContext : CmsDbContext
    {
        public SnapshotRejectingContext(string db)
            : base(new DbContextOptionsBuilder<CmsDbContext>()
                .UseInMemoryDatabase(db)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options)
        {
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
            => ChangeTracker.Entries<PageSetSnapshot>().Any(e => e.State == EntityState.Added)
                ? throw new DbUpdateException("rejected", (Exception?)null)
                : base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private Graph NewGraph(
        IContentStore<PageDTO>? pageStore = null,
        IContentStore<ContentZoneItemDTO>? historyItemStore = null,
        ContentReadMode mode = ContentReadMode.Published,
        CmsDbContext? context = null,
        bool recordZoneWrites = false)
    {
        var ctx = context ?? NewContext();
        var user = new DefaultContentUserContext();
        var scope = new ChangeSetScope(ctx, user);
        var rc = new TestReadContext(mode);
        var pages = new ContentStore<PageDTO>(ctx, rc, scope, user, "pages");
        var zoneStore = new ContentStore<ContentZoneDTO>(ctx, rc, scope, user, "contentzones");
        var items = new ContentStore<ContentZoneItemDTO>(ctx, rc, scope, user, "contentzoneitems");
        var tree = new PageZoneTree(ctx);
        var recorder = new PageHistoryRecorder(ctx, tree, scope, user);
        // Zone writes record their own entries only when a test asks for it, so the other tests see just
        // the entries they record explicitly.
        var zones = new ContentZoneService(ctx, zoneStore, items, rc, scope, Substitute.For<ICMSRouteService>(), tree,
            recordZoneWrites ? recorder : Substitute.For<IPageHistoryRecorder>());
        var history = new PageHistoryService(ctx, pageStore ?? pages, historyItemStore ?? items, tree, recorder, scope, new CmsTransaction(ctx));
        return new Graph(ctx, scope, pages, items, zones, history, tree, recorder);
    }

    private async Task<Guid> CreatePageAsync(string title = "Home", bool publish = true)
    {
        var g = NewGraph();
        var page = new PageDTO
        {
            ControllerName = "GenericPage",
            Version = new ContentVersion { Title = title, Node = new ContentNode() }
        };
        await g.Pages.SaveDraftAsync(page, null);
        if (publish)
            await g.Pages.PublishAsync(page.Version.Node.Id);
        return page.Version.Node.Id;
    }

    private async Task<Guid> ZoneAsync(Guid pageNodeId, string slot = "Main")
        => (await NewGraph().Zones.GetOrCreateByPageSlotAsync(pageNodeId, slot)).Zone.Version.Node.Id;

    private async Task<Guid> NestedZoneAsync(Guid ownerItemNodeId, string slot = "Left")
        => (await NewGraph().Zones.GetOrCreateByItemSlotAsync(ownerItemNodeId, slot)).Zone.Version.Node.Id;

    private async Task<Guid> AddItemAsync(Guid zoneNodeId, string component, string props = "{}")
    {
        var item = new ContentZoneItemDTO
        {
            ComponentName = component,
            ComponentPropertiesJson = props,
            IsActive = true,
            Version = new ContentVersion { Title = component, Node = new ContentNode() }
        };
        await NewGraph().Zones.AddItemAsync(zoneNodeId, item);
        return item.Version.Node.Id;
    }

    private async Task PublishZonesAsync(Guid pageNodeId) => await NewGraph().Zones.PublishPageZonesAsync(pageNodeId);

    private async Task<PageSetSnapshot> CaptureAsync(Guid pageNodeId)
        => (await NewGraph().History.CaptureAsync(pageNodeId))!;

    private async Task EditItemAsync(Guid itemNodeId, string props)
    {
        var g = NewGraph();
        var item = (await g.Items.GetCurrentDraftAsync(itemNodeId))!;
        await g.Zones.UpdateItemAsync(item with { ComponentPropertiesJson = props });
    }

    private async Task<ContentZoneItemDTO> DraftAsync(Guid itemNodeId)
        => (await NewGraph().Items.GetCurrentDraftAsync(itemNodeId))!;

    private async Task<string> TokenAsync(Guid pageNodeId) => (await NewGraph().History.GetDraftStateTokenAsync(pageNodeId))!;

    [Test]
    public void Constructor_NullArgs_Throws()
    {
        var g = NewGraph();
        var user = new DefaultContentUserContext();
        var tx = new CmsTransaction(g.Context);
        Assert.Multiple(() =>
        {
            Assert.That(() => new PageHistoryService(null!, g.Pages, g.Items, g.Tree, g.Recorder, g.Scope, tx), Throws.ArgumentNullException);
            Assert.That(() => new PageHistoryService(g.Context, null!, g.Items, g.Tree, g.Recorder, g.Scope, tx), Throws.ArgumentNullException);
            Assert.That(() => new PageHistoryService(g.Context, g.Pages, null!, g.Tree, g.Recorder, g.Scope, tx), Throws.ArgumentNullException);
            Assert.That(() => new PageHistoryService(g.Context, g.Pages, g.Items, null!, g.Recorder, g.Scope, tx), Throws.ArgumentNullException);
            Assert.That(() => new PageHistoryService(g.Context, g.Pages, g.Items, g.Tree, null!, g.Scope, tx), Throws.ArgumentNullException);
            Assert.That(() => new PageHistoryService(g.Context, g.Pages, g.Items, g.Tree, g.Recorder, null!, tx), Throws.ArgumentNullException);
            Assert.That(() => new PageHistoryService(g.Context, g.Pages, g.Items, g.Tree, g.Recorder, g.Scope, null!), Throws.ArgumentNullException);
            Assert.That(() => new PageHistoryRecorder(null!, g.Tree, g.Scope, user), Throws.ArgumentNullException);
            Assert.That(() => new PageHistoryRecorder(g.Context, null!, g.Scope, user), Throws.ArgumentNullException);
            Assert.That(() => new PageHistoryRecorder(g.Context, g.Tree, null!, user), Throws.ArgumentNullException);
            Assert.That(() => new PageHistoryRecorder(g.Context, g.Tree, g.Scope, null!), Throws.ArgumentNullException);
        });
    }

    // ─── CaptureAsync ─────────────────────────────────────────────────────────

    [Test]
    public async Task CaptureAsync_PageNeverPublished_RecordsNothing()
    {
        var page = await CreatePageAsync(publish: false);

        var snapshot = await NewGraph().History.CaptureAsync(page);

        await using var ctx = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(snapshot, Is.Null);
            Assert.That(ctx.Set<PageSetSnapshot>().Any(), Is.False);
        });
    }

    [Test]
    public async Task CaptureAsync_RecordsThePageAndEveryActiveLiveItemIncludingNested()
    {
        var page = await CreatePageAsync();
        var main = await ZoneAsync(page);
        var layout = await AddItemAsync(main, "Layout");
        var nested = await NestedZoneAsync(layout);
        var inner = await AddItemAsync(nested, "Text");
        var removed = await AddItemAsync(main, "Gone");
        await PublishZonesAsync(page);
        await NewGraph().Zones.RemoveItemAsync(removed);
        await PublishZonesAsync(page);
        var draftOnly = await AddItemAsync(main, "Pending");

        var g = NewGraph();
        PageSetSnapshot? snapshot;
        Guid changeSet;
        using (g.Scope.Begin(ChangeSetKind.Publish, page, null))
        {
            changeSet = g.Scope.Current;
            snapshot = await g.History.CaptureAsync(page);
        }

        var pageVersion = (await NewGraph().Pages.GetAsync(page))!.VersionId;
        Assert.Multiple(() =>
        {
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot!.PageNodeId, Is.EqualTo(page));
            Assert.That(snapshot.PageVersionId, Is.EqualTo(pageVersion));
            Assert.That(snapshot.ChangeSetId, Is.EqualTo(changeSet));
            Assert.That(snapshot.ItemCount, Is.EqualTo(2));
            Assert.That(snapshot.Items.Select(i => i.ItemNodeId), Is.EquivalentTo(new[] { layout, inner }));
            Assert.That(snapshot.Items.Select(i => i.ItemNodeId), Has.None.EqualTo(removed).And.None.EqualTo(draftOnly));
            Assert.That(snapshot.Items.Single(i => i.ItemNodeId == inner).ZoneNodeId, Is.EqualTo(nested));
        });
    }

    [Test]
    public async Task CaptureAsync_LiveSetUnchanged_SkipsTheDuplicate_ButRecordsAChange()
    {
        var page = await CreatePageAsync();
        var main = await ZoneAsync(page);
        var item = await AddItemAsync(main, "Text");
        await PublishZonesAsync(page);
        await CaptureAsync(page);

        var duplicate = await NewGraph().History.CaptureAsync(page);
        await EditItemAsync(item, "{\"Body\":\"new\"}");
        await PublishZonesAsync(page);
        var changed = await NewGraph().History.CaptureAsync(page);

        Assert.Multiple(async () =>
        {
            Assert.That(duplicate, Is.Null);
            Assert.That(changed, Is.Not.Null);
            Assert.That(await NewGraph().History.ListAsync(page), Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task CaptureAsync_WriteFails_ReturnsNullAndLeavesNothingTracked()
    {
        var page = await CreatePageAsync();
        var main = await ZoneAsync(page);
        await AddItemAsync(main, "Text");
        await PublishZonesAsync(page);
        var ctx = new SnapshotRejectingContext(_db);

        var snapshot = await NewGraph(context: ctx).History.CaptureAsync(page);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot, Is.Null);
            Assert.That(ctx.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged), Is.Empty);
        });
    }

    [Test]
    public async Task RecordDraftAsync_NoteOverTheColumnLimit_IsTruncatedWithoutSplittingASurrogatePair()
    {
        var page = await CreatePageAsync();
        // 499 BMP chars + one surrogate pair = 501 chars; the cut lands inside the pair.
        var longNote = new string('a', 499) + "\U0001F600"; // 😀

        var g = NewGraph();
        await g.Recorder.RecordDraftAsync(page, longNote);

        var snapshot = (await NewGraph().History.ListAsync(page)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Note!.Length, Is.EqualTo(499), "the orphaned high surrogate is dropped with the cut");
            Assert.That(snapshot.Note, Is.EqualTo(longNote[..499]));
        });
    }

    [Test]
    public async Task CaptureAsync_SameItemsButNewPageVersion_RecordsAnEntry()
    {
        var page = await CreatePageAsync();
        await CaptureAsync(page);
        var g = NewGraph();
        var draft = (await g.Pages.GetCurrentDraftAsync(page))!;
        await g.Pages.SaveDraftAsync(draft with { Version = draft.Version with { Title = "Renamed" } }, null);
        await NewGraph().Pages.PublishAsync(page);

        var snapshot = await NewGraph().History.CaptureAsync(page);

        Assert.That(snapshot, Is.Not.Null);
    }

    // ─── reads ────────────────────────────────────────────────────────────────

    [Test]
    public async Task ListGetAndPrevious_OrderByCaptureTime()
    {
        var page = await CreatePageAsync();
        var main = await ZoneAsync(page);
        var item = await AddItemAsync(main, "Text");
        await PublishZonesAsync(page);
        var first = await CaptureAsync(page);
        await EditItemAsync(item, "{\"Body\":\"2\"}");
        await PublishZonesAsync(page);
        var second = await CaptureAsync(page);

        var history = NewGraph().History;
        var list = await history.ListAsync(page);
        var fetched = await history.GetAsync(second.Id);
        var previous = await history.GetPreviousAsync(fetched!);
        var beforeFirst = await history.GetPreviousAsync(first);
        Assert.Multiple(async () =>
        {
            Assert.That(list.Select(s => s.Id), Is.EqualTo(new[] { second.Id, first.Id }));
            Assert.That(fetched!.Items, Has.Count.EqualTo(1));
            Assert.That(previous!.Id, Is.EqualTo(first.Id));
            Assert.That(beforeFirst, Is.Null);
            Assert.That(await history.GetAsync(Guid.NewGuid()), Is.Null);
        });
    }

    [Test]
    public async Task GetLatestIdAsync_IsTheFirstListedEntry_OrNull()
    {
        var page = await CreatePageAsync();
        var none = await NewGraph().History.GetLatestIdAsync(page);
        await CaptureAsync(page);
        await RenamePageAsync(page, "Renamed");
        await NewGraph().History.RecordDraftAsync(page, "Renamed");

        var history = NewGraph().History;
        Assert.Multiple(async () =>
        {
            Assert.That(none, Is.Null);
            Assert.That(await history.GetLatestIdAsync(page), Is.EqualTo((await history.ListAsync(page))[0].Id));
        });
    }

    [Test]
    public async Task GetPreviousAsync_TiedTimestamps_FollowsTheListOrder()
    {
        var page = await CreatePageAsync();
        var pageVersion = (await NewGraph().Pages.GetCurrentDraftAsync(page))!.VersionId;
        var tie = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        await using (var ctx = NewContext())
        {
            PageSetSnapshot Snapshot(DateTime at) => new()
            {
                Id = Guid.NewGuid(), PageNodeId = page, PageVersionId = pageVersion, Kind = PageSetSnapshotKind.Draft,
                Note = "n", StateHash = Guid.NewGuid().ToString("N"), CreatedUtc = at
            };
            ctx.Set<PageSetSnapshot>().AddRange(Snapshot(tie.AddMinutes(-1)), Snapshot(tie), Snapshot(tie), Snapshot(tie));
            await ctx.SaveChangesAsync();
        }

        var history = NewGraph().History;
        var list = await history.ListAsync(page);
        var previous = new List<Guid?>();
        foreach (var entry in list)
            previous.Add((await history.GetPreviousAsync(entry))?.Id);

        Assert.That(previous, Is.EqualTo(list.Skip(1).Select(e => (Guid?)e.Id).Append(null)),
            "each entry's previous is the next one listed, ties included");
    }

    [Test]
    public void GetPreviousAsync_Null_Throws()
    {
        Assert.That(() => NewGraph().History.GetPreviousAsync(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task GetSnapshotStateAsync_ReturnsTheCapturedVersions()
    {
        var page = await CreatePageAsync("Captured");
        var main = await ZoneAsync(page);
        var item = await AddItemAsync(main, "Text", "{\"Body\":\"old\"}");
        await PublishZonesAsync(page);
        var snapshot = await CaptureAsync(page);
        await EditItemAsync(item, "{\"Body\":\"new\"}");

        var state = await NewGraph().History.GetSnapshotStateAsync(snapshot.Id);
        var missing = await NewGraph().History.GetSnapshotStateAsync(Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(state!.Page.Version.Title, Is.EqualTo("Captured"));
            Assert.That(state.Items.Single().ComponentPropertiesJson, Is.EqualTo("{\"Body\":\"old\"}"));
            Assert.That(missing, Is.Null);
        });
    }

    [Test]
    public async Task GetCurrentStateAsync_IncludesRemovedDrafts_AndNullForUnknownPage()
    {
        var page = await CreatePageAsync();
        var main = await ZoneAsync(page);
        var kept = await AddItemAsync(main, "Kept");
        var removed = await AddItemAsync(main, "Removed");
        await PublishZonesAsync(page);
        await NewGraph().Zones.RemoveItemAsync(removed);

        var state = await NewGraph().History.GetCurrentStateAsync(page);
        var unknown = await NewGraph().History.GetCurrentStateAsync(Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(state!.Items.Select(i => i.Version.NodeId), Is.EquivalentTo(new[] { kept, removed }));
            Assert.That(state.Items.Single(i => i.Version.NodeId == removed).IsActive, Is.False);
            Assert.That(unknown, Is.Null);
        });
    }

    [Test]
    public async Task GetDraftStateTokenAsync_ChangesOnAnInPlaceDraftEdit()
    {
        var page = await CreatePageAsync();
        var main = await ZoneAsync(page);
        var item = await AddItemAsync(main, "Text");
        var before = await TokenAsync(page);
        await EditItemAsync(item, "{\"Body\":\"edited\"}");
        var after = await TokenAsync(page);

        Assert.Multiple(async () =>
        {
            Assert.That(after, Is.Not.EqualTo(before));
            Assert.That(await TokenAsync(page), Is.EqualTo(after), "stable while nothing changes");
            Assert.That(await NewGraph().History.GetDraftStateTokenAsync(Guid.NewGuid()), Is.Null);
        });
    }

    [Test]
    public async Task GetSnapshotsReferencingVersionAsync_FindsPageAndItemReferences()
    {
        var page = await CreatePageAsync();
        var main = await ZoneAsync(page);
        var item = await AddItemAsync(main, "Text");
        await PublishZonesAsync(page);
        var snapshot = await CaptureAsync(page);

        var history = NewGraph().History;
        var viaPage = await history.GetSnapshotsReferencingVersionAsync(snapshot.PageVersionId);
        var viaItem = await history.GetSnapshotsReferencingVersionAsync(snapshot.Items.Single().ItemVersionId);
        var none = await history.GetSnapshotsReferencingVersionAsync(Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(viaPage.Select(s => s.Id), Is.EqualTo(new[] { snapshot.Id }));
            Assert.That(viaItem.Select(s => s.Id), Is.EqualTo(new[] { snapshot.Id }));
            Assert.That(none, Is.Empty);
        });
    }

    [Test]
    public async Task GetReadSnapshotAsync_GovernsTheWholeTree_SoALaterItemIsHidden()
    {
        var page = await CreatePageAsync();
        var main = await ZoneAsync(page);
        var item = await AddItemAsync(main, "Text");
        await PublishZonesAsync(page);
        var snapshot = await CaptureAsync(page);
        var later = await AddItemAsync(main, "Later");
        await PublishZonesAsync(page);

        var read = await NewGraph().History.GetReadSnapshotAsync(snapshot.Id);
        var missing = await NewGraph().History.GetReadSnapshotAsync(Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(read!.GovernedNodeIds, Is.EquivalentTo(new[] { page, item, later }));
            Assert.That(read.VersionIds, Is.EquivalentTo(new[] { snapshot.PageVersionId, snapshot.Items.Single().ItemVersionId }));
            Assert.That(missing, Is.Null);
        });
    }

    [Test]
    public async Task DeleteAsync_RemovesOnlyTheRecord()
    {
        var page = await CreatePageAsync();
        var main = await ZoneAsync(page);
        await AddItemAsync(main, "Text");
        await PublishZonesAsync(page);
        var snapshot = await CaptureAsync(page);

        var deleted = await NewGraph().History.DeleteAsync(snapshot.Id);
        var again = await NewGraph().History.DeleteAsync(snapshot.Id);

        await using var ctx = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.True);
            Assert.That(again, Is.False);
            Assert.That(ctx.Set<PageSetSnapshot>().Any(), Is.False);
            Assert.That(ctx.Set<PageSetSnapshotItem>().Any(), Is.False);
            Assert.That(ctx.Set<ContentVersion>().Any(v => v.Id == snapshot.PageVersionId), Is.True);
        });
    }

    // ─── RevertAsync ──────────────────────────────────────────────────────────

    [Test]
    public void RevertAsync_NullMetadataDelegate_Throws()
    {
        Assert.That(() => NewGraph().History.RevertAsync(Guid.NewGuid(), "t", null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task RevertAsync_UnknownSnapshot_ReturnsNotFound()
    {
        var result = await NewGraph().History.RevertAsync(Guid.NewGuid(), "t", KeepMetadata);

        Assert.That(result, Is.EqualTo((false, IPageHistoryService.NotFoundMessage)));
    }

    [Test]
    public async Task RevertAsync_PageNoLongerExists_ReturnsNotFound()
    {
        await using (var ctx = NewContext())
        {
            ctx.Set<PageSetSnapshot>().Add(new PageSetSnapshot { Id = Guid.NewGuid(), PageNodeId = Guid.NewGuid(), PageVersionId = Guid.NewGuid() });
            await ctx.SaveChangesAsync();
        }

        var snapshotId = NewContext().Set<PageSetSnapshot>().Single().Id;
        var result = await NewGraph().History.RevertAsync(snapshotId, "t", KeepMetadata);

        Assert.That(result, Is.EqualTo((false, IPageHistoryService.NotFoundMessage)));
    }

    [Test]
    public async Task RevertAsync_StaleToken_IsRefused()
    {
        var page = await CreatePageAsync();
        var snapshot = await CaptureAsync(page);

        var result = await NewGraph().History.RevertAsync(snapshot.Id, "stale", KeepMetadata);

        Assert.That(result, Is.EqualTo((false, IPageHistoryService.StaleStateMessage)));
    }

    [Test]
    public async Task RevertAsync_RestoresPageAndItemsAsDrafts_AndRemovesLaterItems()
    {
        var page = await CreatePageAsync("Original");
        var main = await ZoneAsync(page, "Main");
        var side = await ZoneAsync(page, "Side");
        var a = await AddItemAsync(main, "A", "{\"v\":1}");
        var b = await AddItemAsync(main, "B");
        await PublishZonesAsync(page);
        var snapshot = await CaptureAsync(page);

        // Later: the page is renamed, A edited and moved to Side, B removed, C added.
        var g = NewGraph();
        var pageDraft = (await g.Pages.GetCurrentDraftAsync(page))!;
        await g.Pages.SaveDraftAsync(pageDraft with { Version = pageDraft.Version with { Title = "Renamed" } }, null);
        await EditItemAsync(a, "{\"v\":2}");
        await NewGraph().Zones.MoveItemAsync(a, side, 0);
        await NewGraph().Zones.RemoveItemAsync(b);
        var c = await AddItemAsync(main, "C");

        var result = await NewGraph().History.RevertAsync(snapshot.Id, await TokenAsync(page), KeepMetadata);

        var revertedPage = (await NewGraph().Pages.GetCurrentDraftAsync(page))!;
        var livePage = (await NewGraph().Pages.GetAsync(page))!;
        var aDraft = await DraftAsync(a);
        var bDraft = await DraftAsync(b);
        var cDraft = await DraftAsync(c);
        await using var ctx = NewContext();
        var restoreSets = ctx.Set<ChangeSet>().Where(s => s.Kind == ChangeSetKind.Restore).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo((true, (string?)null)));
            Assert.That(revertedPage.Version.Title, Is.EqualTo("Original"));
            Assert.That(revertedPage.Version.ChangeNote, Does.StartWith("Reverted to page history from "));
            Assert.That(livePage.Version.Title, Is.EqualTo("Original"));
            Assert.That(aDraft.ComponentPropertiesJson, Is.EqualTo("{\"v\":1}"));
            Assert.That(aDraft.ContentZoneNodeId, Is.EqualTo(main));
            Assert.That(aDraft.Version.State, Is.EqualTo(ContentVersionState.Draft), "a revert never publishes");
            Assert.That(bDraft.IsActive, Is.True);
            Assert.That(cDraft.IsActive, Is.False, "an item added after the entry is removed, not deleted");
            Assert.That(restoreSets, Has.Count.EqualTo(1));
            Assert.That(restoreSets.Single().RootNodeId, Is.EqualTo(page));
        });
    }

    [Test]
    public async Task RevertAsync_NothingChanged_WritesNothing()
    {
        var page = await CreatePageAsync();
        var main = await ZoneAsync(page);
        await AddItemAsync(main, "A");
        await PublishZonesAsync(page);
        var snapshot = await CaptureAsync(page);
        int versionsBefore;
        await using (var ctx = NewContext())
            versionsBefore = ctx.Set<ContentVersion>().Count();

        var result = await NewGraph().History.RevertAsync(snapshot.Id, await TokenAsync(page), KeepMetadata);

        await using var verify = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(verify.Set<ContentVersion>().Count(), Is.EqualTo(versionsBefore));
        });
    }

    [Test]
    public async Task RevertAsync_LaterItemOnACapturedOrdinal_MovesToTheEndOfItsZone()
    {
        var page = await CreatePageAsync();
        var main = await ZoneAsync(page);
        var a = await AddItemAsync(main, "A");
        await PublishZonesAsync(page);
        var snapshot = await CaptureAsync(page);
        var c = await AddItemAsync(main, "C");
        await NewGraph().Zones.MoveItemAsync(c, main, 0); // C takes ordinal 1, A shifts to 2

        var result = await NewGraph().History.RevertAsync(snapshot.Id, await TokenAsync(page), KeepMetadata);

        var aDraft = await DraftAsync(a);
        var cDraft = await DraftAsync(c);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(aDraft.Ordinal, Is.EqualTo(1));
            Assert.That(cDraft.IsActive, Is.False);
            Assert.That(cDraft.Ordinal, Is.EqualTo(3), "past the zone's highest ordinal");
        });
    }

    [Test]
    public async Task RevertAsync_UsesTheNormalizedMetadata()
    {
        var page = await CreatePageAsync();
        var main = await ZoneAsync(page);
        var a = await AddItemAsync(main, "A");
        await PublishZonesAsync(page);
        var snapshot = await CaptureAsync(page);
        await EditItemAsync(a, "{\"v\":2}");

        var result = await NewGraph().History.RevertAsync(
            snapshot.Id, await TokenAsync(page), (component, _, _) => component == "A" ? "{\"DisplayMode\":\"Single\"}" : null);

        Assert.Multiple(async () =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That((await DraftAsync(a)).MetadataJson, Is.EqualTo("{\"DisplayMode\":\"Single\"}"));
        });
    }

    [Test]
    public async Task RevertAsync_NullNormalizedMetadata_StoresEmptyDocument()
    {
        var page = await CreatePageAsync();
        var main = await ZoneAsync(page);
        var a = await AddItemAsync(main, "A");
        await PublishZonesAsync(page);
        var snapshot = await CaptureAsync(page);
        await EditItemAsync(a, "{\"v\":2}");

        await NewGraph().History.RevertAsync(snapshot.Id, await TokenAsync(page), (_, _, _) => null);

        Assert.That((await DraftAsync(a)).MetadataJson, Is.EqualTo("{}"));
    }

    [Test]
    public async Task RevertAsync_CapturedItemNoLongerInTheTree_IsRefusedBeforeWriting()
    {
        var page = await CreatePageAsync("Original");
        var main = await ZoneAsync(page);
        await AddItemAsync(main, "A");
        await PublishZonesAsync(page);
        var snapshot = await CaptureAsync(page);

        // A captured version that sits in a global zone: its node is not in this page's tree.
        var global = (await NewGraph().Zones.GetOrCreateByNameAsync("Footer")).Version.Node.Id;
        var stray = await AddItemAsync(global, "Stray");
        await AddCapturedItemAsync(snapshot.Id, (await DraftAsync(stray)).VersionId, stray);
        await RenamePageAsync(page, "Renamed");

        var result = await NewGraph().History.RevertAsync(snapshot.Id, await TokenAsync(page), KeepMetadata);

        Assert.Multiple(async () =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.StartWith("A zone or widget in this history entry no longer exists"));
            Assert.That((await NewGraph().Pages.GetCurrentDraftAsync(page))!.Version.Title, Is.EqualTo("Renamed"));
        });
    }

    [Test]
    public async Task RevertAsync_CapturedZoneNoLongerInTheTree_IsRefused()
    {
        var page = await CreatePageAsync();
        var main = await ZoneAsync(page);
        var a = await AddItemAsync(main, "A");
        await PublishZonesAsync(page);
        var snapshot = await CaptureAsync(page);

        // Point A's captured version at a zone outside the page (its node is still in the tree).
        await using (var ctx = NewContext())
        {
            var captured = ctx.Set<ContentZoneItemDTO>().Single(i => i.VersionId == snapshot.Items.Single().ItemVersionId);
            captured.ContentZoneNodeId = Guid.NewGuid();
            await ctx.SaveChangesAsync();
        }

        var result = await NewGraph().History.RevertAsync(snapshot.Id, await TokenAsync(page), KeepMetadata);

        Assert.That(result.Success, Is.False);
        Assert.That(a, Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public async Task RevertAsync_PageSaveFails_ReturnsTheStoreError()
    {
        var page = await CreatePageAsync("Original");
        var snapshot = await CaptureAsync(page);
        await RenamePageAsync(page, "Renamed");

        var real = NewGraph().Pages;
        var pages = Substitute.For<IContentStore<PageDTO>>();
        pages.GetCurrentDraftAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ci => real.GetCurrentDraftAsync(ci.Arg<Guid>()));
        pages.GetVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ci => real.GetVersionAsync(ci.Arg<Guid>()));
        pages.SaveDraftAsync(Arg.Any<PageDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(false, "boom"));

        var result = await NewGraph(pageStore: pages).History.RevertAsync(snapshot.Id, await TokenAsync(page), KeepMetadata);

        Assert.That(result, Is.EqualTo((false, "boom")));
    }

    [Test]
    public async Task RevertAsync_ItemSaveFails_ReturnsTheStoreError()
    {
        var page = await CreatePageAsync();
        var main = await ZoneAsync(page);
        var a = await AddItemAsync(main, "A");
        await PublishZonesAsync(page);
        var snapshot = await CaptureAsync(page);
        await EditItemAsync(a, "{\"v\":2}");

        var items = Substitute.For<IContentStore<ContentZoneItemDTO>>();
        items.SaveDraftAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(false, "boom"));

        var result = await NewGraph(historyItemStore: items).History.RevertAsync(snapshot.Id, await TokenAsync(page), KeepMetadata);

        Assert.That(result, Is.EqualTo((false, "boom")));
    }

    // ─── recording on every change ────────────────────────────────────────────

    [Test]
    public async Task RecordDraftAsync_RecordsTheDrafts_AndSkipsAnIdenticalState()
    {
        var page = await CreatePageAsync(publish: false);
        var main = await ZoneAsync(page);
        var item = await AddItemAsync(main, "Text");

        var first = await NewGraph().History.RecordDraftAsync(page, "Added Text to Main");
        var repeat = await NewGraph().History.RecordDraftAsync(page, "again");
        await EditItemAsync(item, "{\"v\":2}");
        var changed = await NewGraph().History.RecordDraftAsync(page, "Text: Content edited");
        var unknown = await NewGraph().History.RecordDraftAsync(Guid.NewGuid(), "x");

        Assert.Multiple(() =>
        {
            Assert.That(first!.Kind, Is.EqualTo(PageSetSnapshotKind.Draft));
            Assert.That(first.Note, Is.EqualTo("Added Text to Main"));
            Assert.That(first.Items.Single().ItemNodeId, Is.EqualTo(item));
            Assert.That(first.StateHash, Has.Length.EqualTo(64));
            Assert.That(repeat, Is.Null, "nothing changed since the last entry");
            Assert.That(changed, Is.Not.Null);
            Assert.That(unknown, Is.Null);
        });
    }

    [Test]
    public async Task Recording_DraftAndPublishedKinds_SkipOnlyWhatTheyRepeat()
    {
        var page = await CreatePageAsync(publish: false);
        var main = await ZoneAsync(page);
        await AddItemAsync(main, "Text");
        await NewGraph().History.RecordDraftAsync(page, "Added Text to Main");
        await NewGraph().Pages.PublishAsync(page);
        await PublishZonesAsync(page);

        // The same state as the draft entry, but going live is still a new (Published) entry.
        var published = await NewGraph().History.CaptureAsync(page);
        var draftAfterPublish = await NewGraph().History.RecordDraftAsync(page, "Saved");
        var publishedAgain = await NewGraph().History.CaptureAsync(page);

        Assert.Multiple(() =>
        {
            Assert.That(published!.Kind, Is.EqualTo(PageSetSnapshotKind.Published));
            Assert.That(published.Note, Is.EqualTo("Published"));
            Assert.That(draftAfterPublish, Is.Null, "the drafts equal what was just published");
            Assert.That(publishedAgain, Is.Null);
        });
    }

    [Test]
    public async Task RecordDraftAsync_RemovedContainer_HidesItsNestedItems()
    {
        var page = await CreatePageAsync(publish: false);
        var main = await ZoneAsync(page);
        var layout = await AddItemAsync(main, "Layout");
        var nested = await NestedZoneAsync(layout);
        await AddItemAsync(nested, "Inner");
        await NewGraph().Zones.RemoveItemAsync(layout);

        var entry = await NewGraph().History.RecordDraftAsync(page, "Removed Layout");

        Assert.That(entry!.Items, Is.Empty);
    }

    [Test]
    public async Task ZoneWrites_RecordAnEntryPerAction()
    {
        var page = await CreatePageAsync(publish: false);
        var main = await ZoneAsync(page, "Main");
        var g = NewGraph(recordZoneWrites: true);
        var item = new ContentZoneItemDTO { ComponentName = "Text", IsActive = true, Version = new ContentVersion { Node = new ContentNode() } };
        await g.Zones.AddItemAsync(main, item);
        await NewGraph(recordZoneWrites: true).Zones.RemoveItemAsync(item.Version.Node.Id);

        var notes = (await NewGraph().History.ListAsync(page)).Select(e => e.Note);

        Assert.That(notes, Is.EqualTo(new[] { "Removed Text", "Added Text to Main" }));
    }

    [Test]
    public async Task RevertAsync_IsItselfRecorded()
    {
        var page = await CreatePageAsync("Original");
        var snapshot = await CaptureAsync(page);
        // A page save records its own entry (PageModel does this), so the revert's result differs from it.
        await RenamePageAsync(page, "Renamed");
        await NewGraph().History.RecordDraftAsync(page, "Page edited");

        await NewGraph().History.RevertAsync(snapshot.Id, await TokenAsync(page), KeepMetadata);

        var latest = (await NewGraph().History.ListAsync(page))[0];
        Assert.Multiple(() =>
        {
            Assert.That(latest.Kind, Is.EqualTo(PageSetSnapshotKind.Draft));
            Assert.That(latest.Note, Does.StartWith("Reverted to page history from "));
        });
    }

    // ─── discard (return to what is live) ─────────────────────────────────────

    [Test]
    public async Task DiscardUnpublishedChangesAsync_PageNotLive_IsRefused()
    {
        var page = await CreatePageAsync(publish: false);

        var result = await NewGraph().History.DiscardUnpublishedChangesAsync(page);

        Assert.That(result, Is.EqualTo((false, IPageHistoryService.NotPublishedMessage)));
    }

    [Test]
    public async Task DiscardUnpublishedChangesAsync_ReturnsTheWholePageToWhatIsLive_AsNewVersions()
    {
        var page = await CreatePageAsync("Live title");
        var main = await ZoneAsync(page);
        var edited = await AddItemAsync(main, "Edited", "{\"v\":1}");
        var removed = await AddItemAsync(main, "Removed");
        await PublishZonesAsync(page);
        await EditItemAsync(edited, "{\"v\":2}");
        await NewGraph().Zones.RemoveItemAsync(removed);
        var added = await AddItemAsync(main, "Added");
        await RenamePageAsync(page, "Draft title");
        int versionsBefore;
        await using (var ctx = NewContext())
            versionsBefore = ctx.Set<ContentVersion>().Count();
        var pendingBefore = await NewGraph().History.GetPagesWithUnpublishedChangesAsync([page]);

        var result = await NewGraph().History.DiscardUnpublishedChangesAsync(page);

        await using var verify = NewContext();
        var pendingAfter = await NewGraph().History.GetPagesWithUnpublishedChangesAsync([page]);
        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo((true, (string?)null)));
            Assert.That((await DraftAsync(edited)).ComponentPropertiesJson, Is.EqualTo("{\"v\":1}"));
            Assert.That((await DraftAsync(edited)).Version.ChangeNote, Is.EqualTo("Discarded unpublished changes"));
            Assert.That((await DraftAsync(removed)).IsActive, Is.True);
            Assert.That((await DraftAsync(added)).IsActive, Is.False, "never live, so taken off the page — not deleted");
            Assert.That((await NewGraph().Pages.GetCurrentDraftAsync(page))!.Version.Title, Is.EqualTo("Live title"));
            Assert.That(verify.Set<ContentVersion>().Count(), Is.GreaterThan(versionsBefore), "nothing is deleted");
            Assert.That((await NewGraph().History.ListAsync(page))[0].Note, Is.EqualTo("Discarded unpublished changes"));
            Assert.That(pendingBefore, Is.EquivalentTo(new[] { page }));
            Assert.That(pendingAfter, Is.Empty, "drafts equal to what is live are not pending");
        });
    }

    [Test]
    public async Task DiscardUnpublishedChangesAsync_LiveWidgetMovedIntoADraftContainer_GoesHome()
    {
        var page = await CreatePageAsync();
        var main = await ZoneAsync(page);
        var live = await AddItemAsync(main, "Live");
        await PublishZonesAsync(page);
        var layout = await AddItemAsync(main, "Layout");
        var column = await NestedZoneAsync(layout);
        await NewGraph().Zones.MoveItemAsync(live, column, 0);

        var result = await NewGraph().History.DiscardUnpublishedChangesAsync(page);

        Assert.Multiple(async () =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That((await DraftAsync(live)).ContentZoneNodeId, Is.EqualTo(main));
            Assert.That((await DraftAsync(live)).IsActive, Is.True);
            Assert.That((await DraftAsync(layout)).IsActive, Is.False);
        });
    }

    [Test]
    public async Task GetPagesWithUnpublishedChangesAsync_ComparesThePageByContent()
    {
        var neverLive = await CreatePageAsync(publish: false);
        var upToDate = await CreatePageAsync("Same");
        var renamed = await CreatePageAsync("Before");
        await RenamePageAsync(renamed, "After");
        var renamedBack = await CreatePageAsync("Back");
        await RenamePageAsync(renamedBack, "Away");
        await RenamePageAsync(renamedBack, "Back");

        var history = NewGraph().History;
        var pending = await history.GetPagesWithUnpublishedChangesAsync([neverLive, upToDate, renamed, renamedBack, Guid.NewGuid()]);

        Assert.Multiple(async () =>
        {
            Assert.That(pending, Is.EquivalentTo(new[] { neverLive, renamed }));
            Assert.That(await history.GetPagesWithUnpublishedChangesAsync([]), Is.Empty);
        });
    }

    [Test]
    public async Task PageZoneTree_GetPagesForZoneAsync_FollowsNestedZonesAndIgnoresGlobalOnes()
    {
        var page = await CreatePageAsync();
        var main = await ZoneAsync(page);
        var layout = await AddItemAsync(main, "Layout");
        var nested = await NestedZoneAsync(layout);
        var global = (await NewGraph().Zones.GetOrCreateByNameAsync("Footer")).Version.Node.Id;

        var tree = NewGraph().Tree;

        Assert.Multiple(async () =>
        {
            Assert.That(await tree.GetPagesForZoneAsync(nested), Is.EqualTo(new[] { page }));
            Assert.That(await tree.GetPagesForZoneAsync(main), Is.EqualTo(new[] { page }));
            Assert.That(await tree.GetPagesForZoneAsync(global), Is.Empty);
        });
    }

    private async Task RenamePageAsync(Guid page, string title)
    {
        var g = NewGraph();
        var draft = (await g.Pages.GetCurrentDraftAsync(page))!;
        await g.Pages.SaveDraftAsync(draft with { Version = draft.Version with { Title = title } }, null);
    }

    private async Task AddCapturedItemAsync(Guid snapshotId, Guid itemVersionId, Guid itemNodeId)
    {
        await using var ctx = NewContext();
        ctx.Set<PageSetSnapshotItem>().Add(new PageSetSnapshotItem { SnapshotId = snapshotId, ItemVersionId = itemVersionId, ItemNodeId = itemNodeId });
        await ctx.SaveChangesAsync();
    }
}
