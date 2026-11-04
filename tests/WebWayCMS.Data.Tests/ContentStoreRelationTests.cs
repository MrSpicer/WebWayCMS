using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

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
        await Articles().PublishAsync(article);

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
