using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

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
