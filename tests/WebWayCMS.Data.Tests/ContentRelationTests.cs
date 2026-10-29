using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class ContentVersionRelationsTests
{
    private static ContentRelation Row(string name, Guid target, int ordinal = 0, string qualifier = "", RelationKind kind = RelationKind.Reference)
        => new() { Id = Guid.NewGuid(), Name = name, TargetNodeId = target, Ordinal = ordinal, Qualifier = qualifier, Kind = kind };

    [Test]
    public void TargetOf_And_TargetsOf_ReadTheStoredRowsByOrdinal()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var version = new ContentVersion { Relations = [Row("Many", b, 1), Row("Many", a, 0), Row("One", a)] };

        Assert.Multiple(() =>
        {
            Assert.That(version.TargetOf("One"), Is.EqualTo(a));
            Assert.That(version.TargetOf("Missing"), Is.Null);
            Assert.That(version.TargetsOf("Many"), Is.EqualTo(new[] { a, b }));
            Assert.That(version.TargetsOf("Missing"), Is.Empty);
        });
    }

    [Test]
    public void SetRelation_WritesOneTargetOrNone_OverTheStoredRows()
    {
        var stored = Guid.NewGuid();
        var target = Guid.NewGuid();
        var version = new ContentVersion { Relations = [Row("One", stored), Row("Other", stored)] };

        version.SetRelation("One", target, RelationKind.Containment, "slot");
        var written = version.EffectiveRelations().Single(r => r.Name == "One");
        version.SetRelation("Other", Guid.Empty, RelationKind.Reference);
        var afterEmpty = version.TargetOf("Other");
        version.SetRelation("Other", null, RelationKind.Reference);

        Assert.Multiple(() =>
        {
            Assert.That(written.TargetNodeId, Is.EqualTo(target));
            Assert.That(written.Kind, Is.EqualTo(RelationKind.Containment));
            Assert.That(written.Qualifier, Is.EqualTo("slot"));
            Assert.That(afterEmpty, Is.Null, "an empty id clears the relation");
            Assert.That(version.TargetOf("Other"), Is.Null);
            Assert.That(version.Relations, Has.Count.EqualTo(2), "the stored rows are untouched");
        });
    }

    [Test]
    public void SetRelations_SkipsEmptyIds_AndNumbersTheRest()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var version = new ContentVersion();

        version.SetRelations("Many", [a, Guid.Empty, b], RelationKind.Reference);

        Assert.Multiple(() =>
        {
            Assert.That(version.TargetsOf("Many"), Is.EqualTo(new[] { a, b }));
            Assert.That(version.RelationWrites["Many"].Select(w => w.Ordinal), Is.EqualTo(new[] { 0, 1 }));
        });
    }

    [Test]
    public void Writes_AreCopyOnWrite_SoAWithCopyNeverSharesThem()
    {
        var original = new ContentVersion();
        var copy = original with { };

        copy.SetRelation("One", Guid.NewGuid(), RelationKind.Reference);

        Assert.Multiple(() =>
        {
            Assert.That(original.RelationWrites, Is.Empty);
            Assert.That(copy.RelationWrites, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void ReplaceAllRelationsFrom_CopiesTheSourceAndClearsNamesItLacks()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var version = new ContentVersion { Relations = [Row("Kept", mine), Row("Dropped", mine)] };
        version.SetRelation("Pending", mine, RelationKind.Reference);

        version.ReplaceAllRelationsFrom([Row("Kept", theirs, 2, "q", RelationKind.Owned)]);

        var kept = version.EffectiveRelations().Single();
        Assert.Multiple(() =>
        {
            Assert.That(kept.Name, Is.EqualTo("Kept"));
            Assert.That(kept.TargetNodeId, Is.EqualTo(theirs));
            Assert.That(kept.Ordinal, Is.EqualTo(2));
            Assert.That(kept.Qualifier, Is.EqualTo("q"));
            Assert.That(kept.Kind, Is.EqualTo(RelationKind.Owned));
            Assert.That(version.TargetOf("Dropped"), Is.Null);
            Assert.That(version.TargetOf("Pending"), Is.Null);
        });
    }

    [Test]
    public void Overlay_KeepsUnwrittenBaselineNames_UnderTheNewVersion()
    {
        var target = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var baseline = Row("Kept", target);
        var writes = new Dictionary<string, IReadOnlyList<RelationWrite>> { ["New"] = [new RelationWrite(target, 0, "", RelationKind.Reference)] };

        var rows = ContentVersionRelations.Overlay([baseline], writes, owner);

        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(r => r.Name), Is.EquivalentTo(new[] { "Kept", "New" }));
            Assert.That(rows.Select(r => r.SourceVersionId), Is.All.EqualTo(owner));
            Assert.That(rows.Single(r => r.Name == "Kept").Id, Is.Not.EqualTo(baseline.Id), "rows are copied, never re-parented");
        });
    }

    [Test]
    public void RelationFingerprint_IsOrderIndependent_AndEmptyWithoutRelations()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var first = new ContentVersion { Relations = [Row("X", a), Row("Y", b)] };
        var second = new ContentVersion { Relations = [Row("Y", b), Row("X", a)] };

        Assert.Multiple(() =>
        {
            Assert.That(first.RelationFingerprint(), Is.EqualTo(second.RelationFingerprint()));
            Assert.That(new ContentVersion().RelationFingerprint(), Is.Empty);
        });
    }
}

[TestFixture]
public class ContentStoreRelationTests
{
    private const string ListRelation = "ArticleListId";
    private string _db = null!;

    [SetUp]
    public void SetUp() => _db = TestContexts.NewDb();

    private CmsDbContext NewContext() => TestContexts.Cms(_db);

    private ContentStore<ArticleDTO> Articles(ContentReadMode mode = ContentReadMode.Published, CmsDbContext? ctx = null)
        => TestStore.Create<ArticleDTO>(ctx ?? NewContext(), "articles", mode);

    private ContentStore<ArticleListDTO> Lists(ContentReadMode mode = ContentReadMode.Published)
        => TestStore.Create<ArticleListDTO>(NewContext(), "articlelists", mode);

    private async Task<Guid> ListAsync(bool publish = true, Guid? parent = null)
    {
        var list = new ArticleListDTO { Version = new ContentVersion { Title = "L", Node = new ContentNode() } };
        if (parent.HasValue)
            list.Version.SetRelation("Parent", parent, RelationKind.Containment);
        await Lists().SaveDraftAsync(list, null);
        if (publish)
            await Lists().PublishAsync(list.Version.Node.Id);
        return list.Version.Node.Id;
    }

    private async Task<Guid> ArticleAsync(Guid? list, string title = "A", bool publish = true, Action<ContentVersion>? relate = null)
    {
        var article = new ArticleDTO { Body = "b", Version = new ContentVersion { Title = title, Node = new ContentNode() } };
        if (list.HasValue)
            article.Version.SetRelation(ListRelation, list, RelationKind.Containment);
        relate?.Invoke(article.Version);
        await Articles().SaveDraftAsync(article, null);
        if (publish)
            await Articles().PublishAsync(article.Version.Node.Id);
        return article.Version.Node.Id;
    }

    // ─── storage and versioning ───────────────────────────────────────────────

    [Test]
    public async Task SaveDraft_Create_PersistsTheWrittenRelations()
    {
        var list = await ListAsync();
        var article = await ArticleAsync(list);

        var draft = (await Articles().GetCurrentDraftAsync(article))!;

        Assert.Multiple(() =>
        {
            Assert.That(draft.Version.TargetOf(ListRelation), Is.EqualTo(list));
            Assert.That(draft.Version.Relations.Single().Kind, Is.EqualTo(RelationKind.Containment));
            Assert.That(draft.Version.Relations.Single().SourceVersionId, Is.EqualTo(draft.VersionId));
        });
    }

    [Test]
    public async Task SaveDraft_Edit_CarriesUnwrittenRelationsForward_AndReplacesWrittenOnes()
    {
        var listA = await ListAsync();
        var listB = await ListAsync();
        var tag = Guid.NewGuid();
        var article = await ArticleAsync(listA, relate: v => v.SetRelation("Tag", tag, RelationKind.Reference));

        // A fresh DTO that writes nothing (as a mapper-built save of a type without relations would).
        var store = Articles();
        var draft = (await store.GetCurrentDraftAsync(article))!;
        await store.SaveDraftAsync(new ArticleDTO { Body = "edited", Version = new ContentVersion { Title = "A", Node = draft.Version.Node } }, null);
        var carried = (await Articles().GetCurrentDraftAsync(article))!;

        var moved = carried with { Version = carried.Version with { } };
        moved.Version.SetRelation(ListRelation, listB, RelationKind.Containment);
        await Articles().SaveDraftAsync(moved, null);
        var afterMove = (await Articles().GetCurrentDraftAsync(article))!;

        Assert.Multiple(() =>
        {
            Assert.That(carried.Body, Is.EqualTo("edited"));
            Assert.That(carried.Version.TargetOf(ListRelation), Is.EqualTo(listA));
            Assert.That(carried.Version.TargetOf("Tag"), Is.EqualTo(tag));
            Assert.That(afterMove.Version.TargetOf(ListRelation), Is.EqualTo(listB));
            Assert.That(afterMove.Version.TargetOf("Tag"), Is.EqualTo(tag));
        });
    }

    [Test]
    public async Task Restore_BringsBackTheHistoricalRelations()
    {
        var listA = await ListAsync();
        var listB = await ListAsync();
        var article = await ArticleAsync(listA);
        var v0 = (await Articles().GetCurrentDraftAsync(article))!.VersionId;
        var draft = (await Articles().GetCurrentDraftAsync(article))!;
        draft.Version.SetRelation(ListRelation, listB, RelationKind.Containment);
        await Articles().SaveDraftAsync(draft, null);

        var result = await Articles().RestoreAsync(v0);
        var restored = (await Articles().GetCurrentDraftAsync(article))!;
        await using var ctx = NewContext();

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(restored.Version.TargetOf(ListRelation), Is.EqualTo(listA));
            Assert.That(ctx.Set<ContentRelation>().Count(r => r.SourceVersionId == v0), Is.EqualTo(1), "the historical rows stay on their version");
        });
    }

    [Test]
    public async Task HardDelete_RemovesTheRelationRows_OfEveryVersion()
    {
        var list = await ListAsync();
        var article = await ArticleAsync(list);
        var draft = (await Articles().GetCurrentDraftAsync(article))!;
        await Articles().SaveDraftAsync(draft, null);

        var deleted = await Articles().DeleteAsync(article, softDelete: false);
        await using var ctx = NewContext();

        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.True);
            Assert.That(ctx.Set<ContentRelation>().Count(), Is.Zero);
        });
    }

    [Test]
    public async Task DeleteVersion_RemovesThatVersionsRelationRows()
    {
        var list = await ListAsync();
        var article = await ArticleAsync(list);
        var draft = (await Articles().GetCurrentDraftAsync(article))!;
        var v0 = draft.VersionId;
        await Articles().SaveDraftAsync(draft, null);

        var deleted = await Articles().DeleteVersionAsync(v0);
        await using var ctx = NewContext();

        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.True);
            Assert.That(ctx.Set<ContentRelation>().Count(r => r.SourceVersionId == v0), Is.Zero);
            Assert.That(ctx.Set<ContentRelation>().Count(), Is.EqualTo(1));
        });
    }

    // ─── relation queries ─────────────────────────────────────────────────────

    [Test]
    public async Task FindRelated_OrdersByTheRelationsOrdinal_ThenNewestFirst()
    {
        var target = await ListAsync();
        var other = Guid.NewGuid();
        var second = await ArticleAsync(null, "second", relate: v => v.SetRelations("Tags", [other, target], RelationKind.Reference));
        var olderFirst = await ArticleAsync(null, "older first", relate: v => v.SetRelations("Tags", [target], RelationKind.Reference));
        var newerFirst = await ArticleAsync(null, "newer first", relate: v => v.SetRelations("Tags", [target], RelationKind.Reference));
        await ArticleAsync(null, "unrelated", relate: v => v.SetRelations("Tags", [other], RelationKind.Reference));
        await ArticleAsync(null, "draft only", publish: false, relate: v => v.SetRelations("Tags", [target], RelationKind.Reference));

        var live = await Articles().FindRelatedAsync("Tags", target);
        var drafts = await Articles().FindCurrentDraftsRelatedAsync("Tags", target);

        Assert.Multiple(() =>
        {
            Assert.That(live.Select(a => a.Version.NodeId), Is.EqualTo(new[] { newerFirst, olderFirst, second }));
            Assert.That(drafts.Select(a => a.Version.Title), Is.EqualTo(new[] { "draft only", "newer first", "older first", "second" }));
        });
    }

    [Test]
    public async Task CountCurrentDraftsByRelation_GroupsByTarget()
    {
        var a = await ListAsync();
        var b = await ListAsync();
        await ArticleAsync(a);
        await ArticleAsync(a, publish: false);
        await ArticleAsync(b);
        await ArticleAsync(null);

        var counts = await Articles().CountCurrentDraftsByRelationAsync(ListRelation);

        Assert.That(counts, Is.EquivalentTo(new Dictionary<Guid, int> { [a] = 2, [b] = 1 }));
    }

    // ─── containment visibility ───────────────────────────────────────────────

    [Test]
    public async Task Published_ChildOfAnUnpublishedParent_IsHidden_ButShownInDraft()
    {
        var hiddenList = await ListAsync(publish: false);
        var visibleList = await ListAsync();
        var hidden = await ArticleAsync(hiddenList, "hidden");
        var visible = await ArticleAsync(visibleList, "visible");
        var store = Articles();
        var draftStore = Articles(ContentReadMode.Draft);

        Assert.Multiple(async () =>
        {
            Assert.That(await store.GetAsync(hidden), Is.Null);
            Assert.That(await store.GetAsync(visible), Is.Not.Null);
            Assert.That((await store.GetAllAsync()).Select(a => a.Version.NodeId), Is.EqualTo(new[] { visible }));
            Assert.That(await store.GetBySlugAsync("hidden"), Is.Null);
            Assert.That(await store.GetBySlugAsync("visible"), Is.Not.Null);
            Assert.That(await store.FindAsync(a => a.Body == "b"), Has.Count.EqualTo(1));
            Assert.That(await store.FindRelatedAsync(ListRelation, hiddenList), Is.Empty);
            Assert.That(await draftStore.GetAsync(hidden), Is.Not.Null, "a draft read never filters");
        });
    }

    [Test]
    public async Task GetBySlug_AHiddenMatch_DoesNotHideAVisibleOne()
    {
        var visibleList = await ListAsync();
        var hiddenList = await ListAsync(publish: false);
        await ArticleAsync(hiddenList, "news");
        var visible = await ArticleAsync(visibleList, "news");

        var found = await Articles().GetBySlugAsync("news");

        Assert.That(found?.Version.NodeId, Is.EqualTo(visible));
    }

    [Test]
    public async Task Visibility_IsTransitive_UpTheContainmentChain()
    {
        var grandparent = await ListAsync(publish: false);
        var parent = await ListAsync(parent: grandparent);
        var article = await ArticleAsync(parent);

        Assert.That(await Articles().GetAsync(article), Is.Null);
    }

    [Test]
    public async Task Visibility_MissingParentNode_Hides_AndACycleFailsClosed()
    {
        var orphan = await ArticleAsync(Guid.NewGuid());

        // Two lists that contain each other (only reachable by writing the rows directly).
        var a = await ListAsync();
        var b = await ListAsync(parent: a);
        await using (var ctx = NewContext())
        {
            var aVersion = ctx.Set<ContentVersion>().Single(v => v.NodeId == a && v.State == ContentVersionState.Published);
            ctx.Add(new ContentRelation { Id = Guid.NewGuid(), SourceVersionId = aVersion.Id, TargetNodeId = b, Name = "Parent", Kind = RelationKind.Containment });
            await ctx.SaveChangesAsync();
        }
        var inCycle = await ArticleAsync(b);

        Assert.Multiple(async () =>
        {
            Assert.That(await Articles().GetAsync(orphan), Is.Null);
            Assert.That(await Articles().GetAsync(inCycle), Is.Null);
        });
    }

    [Test]
    public async Task Visibility_IsMemoizedPerInstance()
    {
        var list = await ListAsync();
        var article = await ArticleAsync(list);
        await using var ctx = NewContext();
        var visibility = new ContainmentVisibility(ctx, new TestReadContext(ContentReadMode.Published));
        var store = new ContentStore<ArticleDTO>(ctx, new TestReadContext(ContentReadMode.Published),
            new ChangeSetScope(ctx, new DefaultContentUserContext()), new DefaultContentUserContext(), "articles", visibility);

        var first = await store.GetAsync(article);
        await Lists().UnpublishAsync(list);
        var second = await store.GetAsync(article);
        var fresh = await Articles().GetAsync(article);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null, "the list's answer was looked up once for this request");
            Assert.That(fresh, Is.Null);
        });
    }

    [Test]
    public async Task Snapshot_GovernedNodes_SkipTheFilter_OthersUseThePublishedRule()
    {
        var hiddenList = await ListAsync(publish: false);
        var governed = await ArticleAsync(hiddenList, "governed");
        var notGoverned = await ArticleAsync(hiddenList, "not governed");
        var governedChild = await ArticleAsync(null, "child of governed", relate: v => v.SetRelation("Parent", governed, RelationKind.Containment));
        await using var ctx = NewContext();
        var versionIds = ctx.Set<ContentVersion>().Where(v => v.NodeId == governed).Select(v => v.Id).ToArray();
        var rc = new SnapshotReadContext(new ContentReadSnapshot([governed], versionIds));
        var store = new ContentStore<ArticleDTO>(ctx, rc, new ChangeSetScope(ctx, new DefaultContentUserContext()),
            new DefaultContentUserContext(), "articles", new ContainmentVisibility(ctx, rc));

        Assert.Multiple(async () =>
        {
            Assert.That(await store.GetAsync(governed), Is.Not.Null);
            Assert.That(await store.GetAsync(notGoverned), Is.Null);
            Assert.That(await store.GetAsync(governedChild), Is.Not.Null, "a governed parent is pinned, so visible");
        });
    }

    [Test]
    public async Task Visibility_RowsWithoutContainment_AreReturnedAsIs()
    {
        await using var ctx = NewContext();
        var visibility = new ContainmentVisibility(ctx, new TestReadContext(ContentReadMode.Published));
        var rows = new List<ArticleDTO> { new() };

        Assert.Multiple(async () =>
        {
            Assert.That(await visibility.FilterVisibleAsync(rows), Is.SameAs(rows));
            Assert.That(await visibility.FilterVisibleAsync((ArticleDTO?)null), Is.Null);
        });
    }

    [Test]
    public void ContainmentVisibility_NullArgs_Throw()
    {
        using var ctx = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(() => new ContainmentVisibility(null!, new TestReadContext(ContentReadMode.Published)), Throws.ArgumentNullException);
            Assert.That(() => new ContainmentVisibility(ctx, null!), Throws.ArgumentNullException);
        });
    }

    private sealed class SnapshotReadContext(ContentReadSnapshot snapshot) : IContentReadContext
    {
        public ContentReadMode Mode => ContentReadMode.Snapshot;
        public string Culture => string.Empty;
        public string Segment => string.Empty;
        public ContentReadSnapshot? Snapshot => snapshot;
    }
}

[TestFixture]
public class ContentRelationsTests
{
    private string _db = null!;

    [SetUp]
    public void SetUp() => _db = TestContexts.NewDb();

    private CmsDbContext NewContext() => TestContexts.Cms(_db);

    private ContentStore<ArticleDTO> Articles() => TestStore.Create<ArticleDTO>(NewContext(), "articles");

    private ContentRelations Relations() => new(NewContext());

    private async Task<Guid> ArticleAsync(string title, Action<ContentVersion>? relate = null, bool publish = false)
    {
        var article = new ArticleDTO { Version = new ContentVersion { Title = title, Node = new ContentNode() } };
        relate?.Invoke(article.Version);
        await Articles().SaveDraftAsync(article, null);
        if (publish)
            await Articles().PublishAsync(article.Version.Node.Id);
        return article.Version.Node.Id;
    }

    private async Task SaveAsync(Guid nodeId, Action<ContentVersion> change, string? title = null)
    {
        var draft = (await Articles().GetCurrentDraftAsync(nodeId))!;
        var edited = draft with { Version = draft.Version with { Title = title ?? draft.Version.Title } };
        change(edited.Version);
        await Articles().SaveDraftAsync(edited, null);
    }

    [Test]
    public void Constructor_Null_Throws()
        => Assert.That(() => new ContentRelations(null!), Throws.ArgumentNullException);

    [Test]
    public async Task GetLiveReferrers_ConsultsOnlyTheCurrentDraftAndPublishedVersions()
    {
        var target = await ArticleAsync("target");
        var historicalOnly = await ArticleAsync("historical", v => v.SetRelation("Ref", target, RelationKind.Reference));
        await SaveAsync(historicalOnly, v => v.SetRelation("Ref", null, RelationKind.Reference));
        var draftOnly = await ArticleAsync("draft", v => v.SetRelation("Ref", target, RelationKind.Reference));
        var publishedOnly = await ArticleAsync("published", v => v.SetRelation("Ref", target, RelationKind.Reference), publish: true);
        await SaveAsync(publishedOnly, v => v.SetRelation("Ref", null, RelationKind.Reference), title: "renamed");
        var both = await ArticleAsync("both", v => v.SetRelation("Ref", target, RelationKind.Reference), publish: true);
        var child = await ArticleAsync("child", v => v.SetRelation("Parent", target, RelationKind.Containment));

        var all = await Relations().GetLiveReferrersAsync(target);
        var references = await Relations().GetLiveReferrersAsync(target, RelationKind.Reference);
        var children = await Relations().GetLiveChildrenAsync(target);

        Assert.Multiple(() =>
        {
            Assert.That(all.Select(r => r.SourceNodeId), Is.EquivalentTo(new[] { draftOnly, publishedOnly, both, child }));
            Assert.That(references.Select(r => r.SourceNodeId), Does.Not.Contain(child).And.Not.Contain(historicalOnly));
            var draft = all.Single(r => r.SourceNodeId == draftOnly);
            Assert.That((draft.OnCurrentDraft, draft.OnPublished), Is.EqualTo((true, false)));
            var published = all.Single(r => r.SourceNodeId == publishedOnly);
            Assert.That((published.OnCurrentDraft, published.OnPublished), Is.EqualTo((false, true)));
            Assert.That(published.Title, Is.EqualTo("published"), "only the published version names it");
            var onBoth = all.Single(r => r.SourceNodeId == both);
            Assert.That((onBoth.OnCurrentDraft, onBoth.OnPublished, onBoth.Name, onBoth.ContentTypeKey), Is.EqualTo((true, true, "Ref", "articles")));
            Assert.That(children.Single().SourceNodeId, Is.EqualTo(child));
            Assert.That(children.Single().Kind, Is.EqualTo(RelationKind.Containment));
        });
    }

    [Test]
    public async Task GetLiveOwnedTargets_ReturnsOwnedRelationsOfLiveVersions()
    {
        var owned = Guid.NewGuid();
        var source = await ArticleAsync("source", v =>
        {
            v.SetRelation("Route", owned, RelationKind.Owned);
            v.SetRelation("Ref", Guid.NewGuid(), RelationKind.Reference);
        });

        Assert.That(await Relations().GetLiveOwnedTargetsAsync(source), Is.EqualTo(new[] { owned }));
    }

    [Test]
    public async Task GetCurrentDraftTarget_ReadsTheCurrentDraftsFirstTarget()
    {
        var oldParent = Guid.NewGuid();
        var parent = Guid.NewGuid();
        var moved = await ArticleAsync("moved", v => v.SetRelation("Parent", oldParent, RelationKind.Containment), publish: true);
        await SaveAsync(moved, v => v.SetRelation("Parent", parent, RelationKind.Containment));
        var root = await ArticleAsync("root");
        var deleted = await ArticleAsync("deleted", v => v.SetRelation("Parent", parent, RelationKind.Containment));
        await Articles().DeleteAsync(deleted, softDelete: true);

        Assert.Multiple(async () =>
        {
            Assert.That(await Relations().GetCurrentDraftTargetAsync(moved, "Parent"), Is.EqualTo(parent), "the draft, not the published version");
            Assert.That(await Relations().GetCurrentDraftTargetAsync(root, "Parent"), Is.Null);
            Assert.That(await Relations().GetCurrentDraftTargetAsync(deleted, "Parent"), Is.Null);
            Assert.That(await Relations().GetCurrentDraftTargetAsync(Guid.NewGuid(), "Parent"), Is.Null);
        });
    }

    [Test]
    public async Task GetNodes_ReturnsLiveCurrentDraftsWithTypeAndTitle()
    {
        var live = await ArticleAsync("live");
        var deleted = await ArticleAsync("deleted");
        await Articles().DeleteAsync(deleted, softDelete: true);

        var nodes = await Relations().GetNodesAsync([live, deleted, Guid.NewGuid(), live]);
        var none = await Relations().GetNodesAsync([]);

        Assert.Multiple(() =>
        {
            Assert.That(nodes.Keys, Is.EqualTo(new[] { live }));
            Assert.That(nodes[live], Is.EqualTo(new RelationNode(live, "articles", "live")));
            Assert.That(none, Is.Empty);
        });
    }
}
