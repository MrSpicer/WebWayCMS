using Microsoft.EntityFrameworkCore;

using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class ContentStoreTests
{
    private string _db = null!;

    [SetUp]
    public void SetUp() => _db = TestContexts.NewDb();

    private CmsDbContext NewContext() => TestContexts.Cms(_db);

    private ContentStore<ContentBlockDTO> NewStore(CmsDbContext? ctx = null, ContentReadMode mode = ContentReadMode.Published)
        => TestStore.Create<ContentBlockDTO>(ctx ?? NewContext(), "contentblocks", mode);

    private static ContentBlockDTO NewBlock(string title = "T", string slug = "", bool hidden = false)
        => new()
        {
            Content = "c",
            Version = new ContentVersion
            {
                Title = title,
                Slug = slug,
                Node = new ContentNode { IsHidden = hidden }
            }
        };

    private async Task<Guid> CreatePublishedAsync(string title = "T")
    {
        var store = NewStore();
        var dto = NewBlock(title);
        var save = await store.SaveDraftAsync(dto, null);
        await store.PublishAsync(dto.Version.Node!.Id);
        return dto.Version.Node.Id;
    }

    private sealed class ThrowingStore<T> : ContentStore<T> where T : class, IVersionedContent
    {
        public ThrowingStore(CmsDbContext ctx) : base(
            ctx,
            new TestReadContext(ContentReadMode.Published),
            new ChangeSetScope(ctx, new DefaultContentUserContext()),
            new DefaultContentUserContext(),
            "contentblocks")
        {
        }

        protected override Task<int> SaveChangesAsync(CancellationToken ct)
            => throw new DbUpdateException("conflict", (Exception?)null);
    }

    // Lets the first saves through and throws on save number failOnSave (a DbUpdateException unless told otherwise).
    private sealed class FailingNthSaveStore<T> : ContentStore<T> where T : class, IVersionedContent
    {
        private readonly int _failOnSave;
        private readonly Func<Exception> _exception;
        private int _saves;

        public FailingNthSaveStore(CmsDbContext ctx, int failOnSave, Func<Exception>? exception = null) : base(
            ctx,
            new TestReadContext(ContentReadMode.Published),
            new ChangeSetScope(ctx, new DefaultContentUserContext()),
            new DefaultContentUserContext(),
            "contentblocks")
        {
            _failOnSave = failOnSave;
            _exception = exception ?? (() => new DbUpdateException("conflict", (Exception?)null));
        }

        protected override Task<int> SaveChangesAsync(CancellationToken ct)
            => ++_saves == _failOnSave
                ? throw _exception()
                : base.SaveChangesAsync(ct);
    }

    // ─── constructor ──────────────────────────────────────────────────────────

    [Test]
    public void Constructor_NullArgs_Throws()
    {
        var ctx = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(() => new ContentStore<ContentBlockDTO>(null!, new TestReadContext(ContentReadMode.Published), new ChangeSetScope(ctx, new DefaultContentUserContext()), new DefaultContentUserContext(), "k"), Throws.ArgumentNullException);
            Assert.That(() => new ContentStore<ContentBlockDTO>(ctx, null!, new ChangeSetScope(ctx, new DefaultContentUserContext()), new DefaultContentUserContext(), "k"), Throws.ArgumentNullException);
            Assert.That(() => new ContentStore<ContentBlockDTO>(ctx, new TestReadContext(ContentReadMode.Published), null!, new DefaultContentUserContext(), "k"), Throws.ArgumentNullException);
            Assert.That(() => new ContentStore<ContentBlockDTO>(ctx, new TestReadContext(ContentReadMode.Published), new ChangeSetScope(ctx, new DefaultContentUserContext()), null!, "k"), Throws.ArgumentNullException);
            Assert.That(() => new ContentStore<ContentBlockDTO>(ctx, new TestReadContext(ContentReadMode.Published), new ChangeSetScope(ctx, new DefaultContentUserContext()), new DefaultContentUserContext(), null!), Throws.ArgumentNullException);
        });
    }

    // ─── read-context reads ───────────────────────────────────────────────────

    [Test]
    public async Task GetAsync_ReturnsPublishedOrDraftByMode()
    {
        var nodeId = await CreatePublishedAsync("Title");

        var store = NewStore();
        var saved = await store.GetAsync(nodeId);
        Assert.That(saved!.Version.Title, Is.EqualTo("Title"));
        Assert.That(saved.Version.State, Is.EqualTo(ContentVersionState.Published));

        // edit to create a draft
        var dto = saved with { Version = saved.Version with { Title = "Edited" } };
        await store.SaveDraftAsync(dto, null);

        var published = await NewStore().GetAsync(nodeId);
        var draft = await NewStore(mode: ContentReadMode.Draft).GetAsync(nodeId);
        Assert.Multiple(() =>
        {
            Assert.That(published!.Version.Title, Is.EqualTo("Title"));
            Assert.That(draft!.Version.Title, Is.EqualTo("Edited"));
        });
    }

    [Test]
    public async Task GetAsync_NotFound_ReturnsNull()
    {
        Assert.That(await NewStore().GetAsync(Guid.NewGuid()), Is.Null);
    }

    [Test]
    public async Task GetAllAsync_ReturnsOnePerNodeInMode()
    {
        await CreatePublishedAsync("A");
        await CreatePublishedAsync("B");

        var all = await NewStore().GetAllAsync();
        Assert.That(all, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetBySlugAsync_Whitespace_ReturnsNull()
    {
        Assert.That(await NewStore().GetBySlugAsync("  "), Is.Null);
    }

    [Test]
    public async Task GetBySlugAsync_FoundAndNotFound()
    {
        var store = NewStore();
        var dto = NewBlock("Title", "my-slug");
        await store.SaveDraftAsync(dto, null);
        await store.PublishAsync(dto.Version.Node!.Id);

        Assert.Multiple(async () =>
        {
            Assert.That((await NewStore().GetBySlugAsync("my-slug"))!.Version.Title, Is.EqualTo("Title"));
            Assert.That(await NewStore().GetBySlugAsync("missing"), Is.Null);
        });
    }

    [Test]
    public async Task GetVersionAsync_And_GetAllVersionsAsync()
    {
        var nodeId = await CreatePublishedAsync("v0");
        var store = NewStore();
        var current = (await store.GetAsync(nodeId))!;
        await store.SaveDraftAsync(current with { Version = current.Version with { Title = "v1" } }, null);

        var versions = await store.GetAllVersionsAsync(nodeId);
        var v1 = await store.GetVersionAsync(versions[0].VersionId);

        Assert.Multiple(async () =>
        {
            Assert.That(versions, Has.Count.EqualTo(2));
            Assert.That(versions.Select(v => v.Version.VersionNumber), Is.EqualTo(new[] { 1, 0 }));
            Assert.That(v1!.Version.Title, Is.EqualTo("v1"));
            Assert.That(await store.GetVersionAsync(Guid.NewGuid()), Is.Null);
        });
    }

    [Test]
    public async Task GetCurrentDraftAsync_And_GetAllCurrentDraftsAsync()
    {
        var nodeId = await CreatePublishedAsync("A");
        var store = NewStore();
        var current = (await store.GetAsync(nodeId))!;
        await store.SaveDraftAsync(current with { Version = current.Version with { Title = "A-draft" } }, null);

        var draft = await store.GetCurrentDraftAsync(nodeId);
        var all = await store.GetAllCurrentDraftsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(draft!.Version.Title, Is.EqualTo("A-draft"));
            Assert.That(all, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task FindAsync_FiltersAtTheReadContext()
    {
        var published = await CreatePublishedAsync("Live");
        await CreatePublishedAsync("Other");
        await NewStore().SaveDraftAsync(NewBlock("Draft only"), null);

        var found = await NewStore().FindAsync(b => b.Version.Title != "Other");

        Assert.That(found.Select(b => b.Version.Node.Id), Is.EqualTo(new[] { published }));
    }

    [Test]
    public async Task FindCurrentDraftsAsync_FiltersCurrentDrafts()
    {
        await CreatePublishedAsync("Live");
        await NewStore().SaveDraftAsync(NewBlock("Draft only"), null);

        var found = await NewStore().FindCurrentDraftsAsync(b => b.Version.Title == "Draft only");

        Assert.That(found.Select(b => b.Version.Title), Is.EqualTo(new[] { "Draft only" }));
    }

    [Test]
    public async Task CountCurrentDraftsByAsync_GroupsByTheKey()
    {
        await NewStore().SaveDraftAsync(NewBlock("A"), null);
        await NewStore().SaveDraftAsync(NewBlock("A"), null);
        await NewStore().SaveDraftAsync(NewBlock("B"), null);

        var counts = await NewStore().CountCurrentDraftsByAsync(b => b.Version.Title!);

        Assert.That(counts, Is.EquivalentTo(new Dictionary<string, int> { ["A"] = 2, ["B"] = 1 }));
    }

    [Test]
    public async Task GetCurrentDraftBySlugAsync_HitMissAndBlank()
    {
        var store = NewStore();
        var dto = NewBlock("Title", "draft-slug");
        await store.SaveDraftAsync(dto, null);

        Assert.Multiple(async () =>
        {
            Assert.That((await NewStore().GetCurrentDraftBySlugAsync("draft-slug"))!.Version.Title, Is.EqualTo("Title"));
            Assert.That(await NewStore().GetCurrentDraftBySlugAsync("missing"), Is.Null);
            Assert.That(await NewStore().GetCurrentDraftBySlugAsync("  "), Is.Null);
        });
    }

    [Test]
    public async Task FindCurrentDraftsWithoutRelationAsync_ReturnsDraftsLackingTheRelation_NewestFirst()
    {
        var ctx = NewContext();
        var store = NewStore(ctx);

        var root = NewBlock("Root");
        await store.SaveDraftAsync(root, null);

        var child = NewBlock("Child");
        child.Version.SetRelation("Parent", root.Version.Node!.Id, RelationKind.Containment);
        await store.SaveDraftAsync(child, null);

        var otherRelation = NewBlock("Other");
        otherRelation.Version.SetRelation("Other", Guid.NewGuid(), RelationKind.Reference);
        await store.SaveDraftAsync(otherRelation, null);

        var deleted = NewBlock("Deleted");
        await store.SaveDraftAsync(deleted, null);
        await store.DeleteAsync(deleted.Version.Node!.Id, softDelete: true);

        var roots = await store.FindCurrentDraftsWithoutRelationAsync("Parent");

        Assert.That(roots.Select(r => r.Version.Title), Is.EqualTo(new[] { "Other", "Root" }));
    }

    [Test]
    public async Task FindCurrentDraftsWithoutRelationAsync_RelationOnlyOnAnOlderVersion_CountsTheCurrentDraft()
    {
        var store = NewStore();
        var block = NewBlock("Moved to root");
        block.Version.SetRelation("Parent", Guid.NewGuid(), RelationKind.Containment);
        await store.SaveDraftAsync(block, null);

        var draft = await store.GetCurrentDraftAsync(block.Version.Node!.Id);
        draft!.Version.SetRelation("Parent", null, RelationKind.Containment);
        await store.SaveDraftAsync(draft, draft.Version.VersionNumber);

        var roots = await store.FindCurrentDraftsWithoutRelationAsync("Parent");

        Assert.That(roots, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetPublishedNodeIdsAsync_ReturnsOnlyPublishedNodes()
    {
        var publishedId = await CreatePublishedAsync("P");

        var store = NewStore();
        var draft = NewBlock("D");
        await store.SaveDraftAsync(draft, null);

        var ids = await NewStore().GetPublishedNodeIdsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(ids, Does.Contain(publishedId));
            Assert.That(ids, Does.Not.Contain(draft.Version.Node!.Id));
        });
    }

    [Test]
    public async Task GetPublishedNodeIdsAsync_SoftDeletedNode_IsExcluded()
    {
        var nodeId = await CreatePublishedAsync("T");
        await NewStore().DeleteAsync(nodeId, softDelete: true);

        var ids = await NewStore().GetPublishedNodeIdsAsync();

        Assert.That(ids, Does.Not.Contain(nodeId));
    }

    [Test]
    public async Task CurrentDraftReads_SoftDeletedNode_AreExcluded()
    {
        var nodeId = await CreatePublishedAsync("T");
        await NewStore().DeleteAsync(nodeId, softDelete: true);

        var store = NewStore();
        Assert.Multiple(async () =>
        {
            Assert.That(await store.GetCurrentDraftAsync(nodeId), Is.Null);
            Assert.That(await store.GetAllCurrentDraftsAsync(), Is.Empty);
        });
    }

    // ─── SaveDraftAsync ───────────────────────────────────────────────────────

    [Test]
    public void SaveDraftAsync_Null_Throws()
    {
        Assert.That(async () => await NewStore().SaveDraftAsync(null!, null), Throws.ArgumentNullException);
    }

    [Test]
    public async Task SaveDraftAsync_Create_SetsNodeAndDraftAndSlug()
    {
        var dto = NewBlock("Hello World");
        var result = await NewStore().SaveDraftAsync(dto, null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(dto.Version.Node!.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(dto.Version.VersionNumber, Is.EqualTo(0));
            Assert.That(dto.Version.State, Is.EqualTo(ContentVersionState.Draft));
            Assert.That(dto.Version.Slug, Is.EqualTo("hello-world"));
            Assert.That(dto.Version.Node.ContentTypeKey, Is.EqualTo("contentblocks"));
        });
    }

    [Test]
    public async Task SaveDraftAsync_Create_SlugifiesTitleWithPunctuation()
    {
        var dto = NewBlock("Spaced Slug Page!!");
        await NewStore().SaveDraftAsync(dto, null);

        Assert.That(dto.Version.Slug, Is.EqualTo("spaced-slug-page"));
    }

    [Test]
    public async Task SaveDraftAsync_Create_CollapsesConsecutiveWhitespace()
    {
        var dto = NewBlock("  Two   Spaces  ");
        await NewStore().SaveDraftAsync(dto, null);

        Assert.That(dto.Version.Slug, Is.EqualTo("two-spaces"));
    }

    [Test]
    public async Task SaveDraftAsync_Create_BlankTitleAndSlug_ProducesEmptySlug()
    {
        var dto = NewBlock("", "");
        await NewStore().SaveDraftAsync(dto, null);

        Assert.That(dto.Version.Slug, Is.EqualTo(string.Empty));
    }

    [Test]
    public async Task SaveDraftAsync_Create_NonLatinTitle_FallsBackToEncodedSlug()
    {
        var dto = NewBlock("日本語ページ");
        await NewStore().SaveDraftAsync(dto, null);

        Assert.That(dto.Version.Slug, Is.EqualTo(Uri.EscapeDataString("日本語ページ")));
    }

    [Test]
    public async Task SaveDraftAsync_Create_PreservesPresetSlugAndHidden()
    {
        var dto = NewBlock("T", "custom", hidden: true);
        await NewStore().SaveDraftAsync(dto, null);

        Assert.Multiple(() =>
        {
            Assert.That(dto.Version.Slug, Is.EqualTo("custom"));
            Assert.That(dto.Version.Node!.IsHidden, Is.True);
        });
    }

    [Test]
    public async Task SaveDraftAsync_NotFoundNode_ReturnsFailure()
    {
        var dto = NewBlock("T");
        dto.Version.Node = new ContentNode { Id = Guid.NewGuid() };
        var result = await NewStore().SaveDraftAsync(dto, null);
        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task SaveDraftAsync_EditDraft_MintsANewVersionAndKeepsTheOld()
    {
        var dto = NewBlock("T");
        var store = NewStore();
        await store.SaveDraftAsync(dto, null);
        var firstVersionId = dto.VersionId;

        var edit = dto with { Content = "c2", Version = dto.Version with { Title = "T2" } };
        var result = await store.SaveDraftAsync(edit, 0);

        var versions = await NewStore().GetAllVersionsAsync(dto.Version.Node!.Id);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(versions.Select(v => v.Version.VersionNumber), Is.EqualTo(new[] { 1, 0 }), "every save is a version");
            Assert.That(versions[0].Content, Is.EqualTo("c2"));
            Assert.That(versions[0].Version.Title, Is.EqualTo("T2"));
            Assert.That(versions[0].Version.IsCurrentDraft, Is.True);
            Assert.That(versions[1].VersionId, Is.EqualTo(firstVersionId));
            Assert.That(versions[1].Content, Is.EqualTo("c"), "the earlier draft is kept unchanged");
            Assert.That(versions[1].Version.IsCurrentDraft, Is.False);
        });
    }

    [Test]
    public async Task SaveDraftAsync_EditDraft_WithUnsetIncomingKey_MintsAKeyedVersion()
    {
        var dto = NewBlock("T");
        var store = NewStore();
        await store.SaveDraftAsync(dto, null);

        // Mirrors the mapping profile: a freshly-mapped DTO has no VersionId set (its key is empty).
        var edit = new ContentBlockDTO
        {
            VersionId = Guid.Empty,
            Content = "c2",
            Version = new ContentVersion
            {
                Title = "T2",
                Slug = string.Empty,
                Node = new ContentNode { Id = dto.Version.Node!.Id }
            }
        };
        var result = await store.SaveDraftAsync(edit, 0);

        var versions = await NewStore().GetAllVersionsAsync(dto.Version.Node.Id);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(versions, Has.Count.EqualTo(2));
            Assert.That(versions[0].VersionId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(versions[0].VersionId, Is.EqualTo(result.VersionId));
            Assert.That(versions[0].Content, Is.EqualTo("c2"));
        });
    }

    [Test]
    public async Task SaveDraftAsync_EditPublished_MintsNewDraft()
    {
        var nodeId = await CreatePublishedAsync("v0");
        var store = NewStore();
        var current = (await store.GetAsync(nodeId))!;

        var result = await store.SaveDraftAsync(current with { Version = current.Version with { Title = "v1" } }, 0);

        var versions = await store.GetAllVersionsAsync(nodeId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(versions, Has.Count.EqualTo(2));
            Assert.That(versions.Single(v => v.Version.IsCurrentDraft).Version.Title, Is.EqualTo("v1"));
            Assert.That(versions.Single(v => v.Version.IsCurrentDraft).Version.State, Is.EqualTo(ContentVersionState.Draft));
            Assert.That(versions.Single(v => !v.Version.IsCurrentDraft).Version.State, Is.EqualTo(ContentVersionState.Published));
        });
    }

    [Test]
    public async Task SaveDraftAsync_StaleVersionNumber_ReturnsFriendlyMessage()
    {
        var nodeId = await CreatePublishedAsync("v0");
        var store = NewStore();
        var current = (await store.GetAsync(nodeId))!;

        var result = await store.SaveDraftAsync(current with { Version = current.Version with { Title = "v1" } }, 99);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo(ContentStore<ContentBlockDTO>.StaleVersionMessage));
        });
    }

    [Test]
    public async Task SaveDraftAsync_DbConflict_MapsToFriendlyMessage()
    {
        var nodeId = await CreatePublishedAsync("v0");
        var ctx = NewContext();
        var store = new ThrowingStore<ContentBlockDTO>(ctx);
        var current = (await store.GetAsync(nodeId))!;

        var result = await store.SaveDraftAsync(current with { Version = current.Version with { Title = "v1" } }, 0);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo(ContentStore<ContentBlockDTO>.StaleVersionMessage));
        });
    }

    // ─── PublishAsync ─────────────────────────────────────────────────────────

    [Test]
    public async Task PublishAsync_NotFound_ReturnsFailure()
    {
        var result = await NewStore().PublishAsync(Guid.NewGuid());
        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task PublishAsync_Draft_BecomesPublished()
    {
        var dto = NewBlock("T");
        var store = NewStore();
        await store.SaveDraftAsync(dto, null);

        var result = await store.PublishAsync(dto.Version.Node!.Id);

        var published = await NewStore().GetAsync(dto.Version.Node.Id);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(published!.Version.State, Is.EqualTo(ContentVersionState.Published));
        });
    }

    [Test]
    public async Task PublishAsync_AlreadyPublished_NoOpSuccess()
    {
        var nodeId = await CreatePublishedAsync("T");
        var result = await NewStore().PublishAsync(nodeId);
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task PublishAsync_DemotesPreviousPublished()
    {
        var nodeId = await CreatePublishedAsync("v0");
        var store = NewStore();
        var current = (await store.GetAsync(nodeId))!;
        await store.SaveDraftAsync(current with { Version = current.Version with { Title = "v1" } }, 0);

        await store.PublishAsync(nodeId);

        var versions = await store.GetAllVersionsAsync(nodeId);
        Assert.Multiple(() =>
        {
            Assert.That(versions.Single(v => v.Version.State == ContentVersionState.Published).Version.VersionNumber, Is.EqualTo(1));
            Assert.That(versions.Single(v => v.Version.VersionNumber == 0).Version.State, Is.EqualTo(ContentVersionState.Archived));
        });
    }

    [Test]
    public async Task PublishAsync_DemotesPreviousPublished_ReportsDisplacedVersion()
    {
        var nodeId = await CreatePublishedAsync("v0");
        var store = NewStore();
        var current = (await store.GetAsync(nodeId))!;
        await store.SaveDraftAsync(current with { Version = current.Version with { Title = "v1" } }, 0);

        var result = await store.PublishAsync(nodeId);

        Assert.That(result.DisplacedVersionId, Is.EqualTo(current.VersionId));
    }

    [Test]
    public async Task PublishAsync_FirstPublish_ReportsNoDisplacedVersion()
    {
        var dto = NewBlock("T");
        var store = NewStore();
        await store.SaveDraftAsync(dto, null);

        var result = await store.PublishAsync(dto.Version.Node!.Id);

        Assert.That(result.DisplacedVersionId, Is.EqualTo(Guid.Empty));
    }

    // ─── RevertPublishAsync ───────────────────────────────────────────────────

    // v0 published, v1 the current draft; returns the node and both version ids.
    private async Task<(Guid NodeId, Guid V0, Guid V1)> CreatePublishedWithDraftAsync()
    {
        var nodeId = await CreatePublishedAsync("v0");
        var store = NewStore();
        var current = (await store.GetAsync(nodeId))!;
        var save = await store.SaveDraftAsync(current with { Version = current.Version with { Title = "v1" } }, 0);
        return (nodeId, current.VersionId, save.VersionId);
    }

    private async Task<Dictionary<int, ContentVersion>> VersionsByNumberAsync(Guid nodeId)
        => (await NewStore().GetAllVersionsAsync(nodeId)).ToDictionary(v => v.Version.VersionNumber, v => v.Version);

    [Test]
    public async Task RevertPublishAsync_UnknownVersion_ReturnsNotFound()
    {
        var nodeId = await CreatePublishedAsync("T");

        var result = await NewStore().RevertPublishAsync(nodeId, Guid.NewGuid(), Guid.Empty);

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task RevertPublishAsync_VersionNotPublished_IsNoOp()
    {
        var (nodeId, v0, v1) = await CreatePublishedWithDraftAsync();

        var result = await NewStore().RevertPublishAsync(nodeId, v1, v0);

        var versions = await VersionsByNumberAsync(nodeId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.VersionId, Is.EqualTo(v1));
            Assert.That(versions[0].State, Is.EqualTo(ContentVersionState.Published));
            Assert.That(versions[1].State, Is.EqualTo(ContentVersionState.Draft));
        });
    }

    [Test]
    public async Task RevertPublishAsync_FirstPublish_ReturnsToDraft()
    {
        var dto = NewBlock("T");
        var store = NewStore();
        await store.SaveDraftAsync(dto, null);
        var nodeId = dto.Version.Node!.Id;
        var publish = await store.PublishAsync(nodeId);

        var result = await NewStore().RevertPublishAsync(nodeId, publish.VersionId, publish.DisplacedVersionId);

        var versions = await VersionsByNumberAsync(nodeId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(versions.Single().Value.State, Is.EqualTo(ContentVersionState.Draft));
            Assert.That(versions.Single().Value.IsCurrentDraft, Is.True);
        });
    }

    [Test]
    public async Task RevertPublishAsync_Republish_RestoresDisplacedVersion()
    {
        var (nodeId, v0, v1) = await CreatePublishedWithDraftAsync();
        var publish = await NewStore().PublishAsync(nodeId);

        var result = await NewStore().RevertPublishAsync(nodeId, publish.VersionId, publish.DisplacedVersionId);

        var versions = await VersionsByNumberAsync(nodeId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.VersionId, Is.EqualTo(v0));
            Assert.That(versions[0].State, Is.EqualTo(ContentVersionState.Published));
            Assert.That(versions[1].State, Is.EqualTo(ContentVersionState.Draft));
            Assert.That(versions[1].IsCurrentDraft, Is.True);
        });
    }

    [Test]
    public async Task RevertPublishAsync_PageWasUnpublished_DoesNotBringOldVersionBack()
    {
        var (nodeId, _, _) = await CreatePublishedWithDraftAsync();
        await NewStore().UnpublishAsync(nodeId); // archives v0; the page is offline
        var publish = await NewStore().PublishAsync(nodeId);

        await NewStore().RevertPublishAsync(nodeId, publish.VersionId, publish.DisplacedVersionId);

        var versions = await VersionsByNumberAsync(nodeId);
        Assert.Multiple(() =>
        {
            Assert.That(publish.DisplacedVersionId, Is.EqualTo(Guid.Empty));
            Assert.That(versions[0].State, Is.EqualTo(ContentVersionState.Archived));
            Assert.That(versions[1].State, Is.EqualTo(ContentVersionState.Draft));
        });
    }

    [Test]
    public async Task RevertPublishAsync_DisplacedNoLongerArchived_IsNotPromoted()
    {
        var (nodeId, v0, v1) = await CreatePublishedWithDraftAsync();
        await NewStore().PublishAsync(nodeId);
        await using (var ctx = NewContext())
        {
            (await ctx.Set<ContentVersion>().SingleAsync(v => v.Id == v0)).State = ContentVersionState.Draft;
            await ctx.SaveChangesAsync();
        }

        var result = await NewStore().RevertPublishAsync(nodeId, v1, v0);

        var versions = await VersionsByNumberAsync(nodeId);
        Assert.Multiple(() =>
        {
            Assert.That(result.VersionId, Is.EqualTo(v1));
            Assert.That(versions[0].State, Is.EqualTo(ContentVersionState.Draft));
            Assert.That(versions[1].State, Is.EqualTo(ContentVersionState.Draft));
        });
    }

    [TestCase(1, TestName = "RevertPublishAsync_DemoteFails_ReturnsStaleAndDetaches")]
    [TestCase(2, TestName = "RevertPublishAsync_PromoteFails_ReturnsStaleAndDetaches")]
    public async Task RevertPublishAsync_SaveFails_ReturnsStaleAndDetaches(int failOnSave)
    {
        var (nodeId, v0, _) = await CreatePublishedWithDraftAsync();
        var publish = await NewStore().PublishAsync(nodeId);
        var ctx = NewContext();
        var store = new FailingNthSaveStore<ContentBlockDTO>(ctx, failOnSave);

        var result = await store.RevertPublishAsync(nodeId, publish.VersionId, v0);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo(ContentStore<ContentBlockDTO>.StaleVersionMessage));
            Assert.That(ctx.ChangeTracker.Entries<ContentVersion>(), Is.Empty);
        });
    }

    [Test]
    public async Task RevertPublishAsync_FirstPublishDemoteFails_ReturnsStale()
    {
        var dto = NewBlock("T");
        var store = NewStore();
        await store.SaveDraftAsync(dto, null);
        var publish = await store.PublishAsync(dto.Version.Node!.Id);
        var ctx = NewContext();

        var result = await new ThrowingStore<ContentBlockDTO>(ctx)
            .RevertPublishAsync(dto.Version.Node.Id, publish.VersionId, Guid.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(ctx.ChangeTracker.Entries<ContentVersion>(), Is.Empty);
        });
    }

    // ─── UnpublishAsync ───────────────────────────────────────────────────────

    [Test]
    public async Task UnpublishAsync_NotFound_ReturnsFailure()
    {
        var result = await NewStore().UnpublishAsync(Guid.NewGuid());
        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task UnpublishAsync_PublishedCurrent_DemotesToDraft()
    {
        var nodeId = await CreatePublishedAsync("T");
        var result = await NewStore().UnpublishAsync(nodeId);

        var versions = await NewStore().GetAllVersionsAsync(nodeId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(versions.Single().Version.State, Is.EqualTo(ContentVersionState.Draft));
            Assert.That(versions.Single().Version.IsCurrentDraft, Is.True);
        });
    }

    [Test]
    public async Task UnpublishAsync_SeparateDraft_ArchivesPublished()
    {
        var nodeId = await CreatePublishedAsync("v0");
        var store = NewStore();
        var current = (await store.GetAsync(nodeId))!;
        await store.SaveDraftAsync(current with { Version = current.Version with { Title = "v1" } }, 0);

        await store.UnpublishAsync(nodeId);

        var versions = await store.GetAllVersionsAsync(nodeId);
        Assert.Multiple(() =>
        {
            Assert.That(versions.Single(v => v.Version.VersionNumber == 0).Version.State, Is.EqualTo(ContentVersionState.Archived));
            Assert.That(versions.Single(v => v.Version.VersionNumber == 1).Version.State, Is.EqualTo(ContentVersionState.Draft));
            Assert.That(versions.Single(v => v.Version.VersionNumber == 1).Version.IsCurrentDraft, Is.True);
        });
    }

    // ─── RestoreAsync ─────────────────────────────────────────────────────────

    [Test]
    public async Task RestoreAsync_NotFound_ReturnsFailure()
    {
        var result = await NewStore().RestoreAsync(Guid.NewGuid());
        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task RestoreAsync_CreatesNewDraftFromHistorical()
    {
        var nodeId = await CreatePublishedAsync("v0");
        var store = NewStore();
        var current = (await store.GetAsync(nodeId))!;
        var v0Id = current.VersionId;
        await store.SaveDraftAsync(current with { Version = current.Version with { Title = "v1" } }, 0);
        await store.PublishAsync(nodeId);

        var result = await store.RestoreAsync(v0Id);

        var versions = await store.GetAllVersionsAsync(nodeId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(versions, Has.Count.EqualTo(3));
            Assert.That(versions.Single(v => v.Version.IsCurrentDraft).Version.VersionNumber, Is.EqualTo(2));
            Assert.That(versions.Single(v => v.Version.IsCurrentDraft).Version.Title, Is.EqualTo("v0"));
        });
    }

    [Test]
    public async Task RestoreAsync_CurrentVersion_NoOpSuccess()
    {
        var nodeId = await CreatePublishedAsync("T");
        var store = NewStore();
        var current = (await store.GetAsync(nodeId))!;

        var result = await store.RestoreAsync(current.VersionId);
        Assert.That(result.Success, Is.True);
    }

    [TestCase(1, TestName = "RestoreAsync_DemoteFails_ReturnsStaleAndDetaches")]
    [TestCase(2, TestName = "RestoreAsync_InsertFails_ReturnsStaleAndDetaches")]
    public async Task RestoreAsync_SaveFails_ReturnsStaleAndDetaches(int failOnSave)
    {
        var (nodeId, v0, v1) = await CreatePublishedWithDraftAsync();
        var ctx = NewContext();

        var result = await new FailingNthSaveStore<ContentBlockDTO>(ctx, failOnSave).RestoreAsync(v0);

        Assert.Multiple(async () =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo(ContentStore<ContentBlockDTO>.StaleVersionMessage));
            Assert.That(ctx.ChangeTracker.Entries<ContentVersion>(), Is.Empty, "the demotion must never be replayed by a later save");
            Assert.That(ctx.ChangeTracker.Entries<ContentBlockDTO>(), Is.Empty);
            if (failOnSave == 1)
                Assert.That((await NewStore().GetCurrentDraftAsync(nodeId))!.VersionId, Is.EqualTo(v1));
        });
    }

    [Test]
    public async Task RestoreAsync_SaveThrows_RethrowsAndDetaches()
    {
        var (_, v0, _) = await CreatePublishedWithDraftAsync();
        var ctx = NewContext();
        var store = new FailingNthSaveStore<ContentBlockDTO>(ctx, 2, () => new InvalidOperationException("boom"));

        Assert.That(async () => await store.RestoreAsync(v0), Throws.InvalidOperationException);
        Assert.That(ctx.ChangeTracker.Entries<ContentVersion>(), Is.Empty);
    }

    [Test]
    public async Task RestoreAsync_NoCurrentDraft_InsertsTheRestoredDraft()
    {
        var (nodeId, v0, v1) = await CreatePublishedWithDraftAsync();
        await using (var ctx = NewContext())
        {
            (await ctx.Set<ContentVersion>().SingleAsync(v => v.Id == v1)).IsCurrentDraft = false;
            await ctx.SaveChangesAsync();
        }

        var result = await NewStore().RestoreAsync(v0);

        Assert.Multiple(async () =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That((await NewStore().GetCurrentDraftAsync(nodeId))!.VersionId, Is.EqualTo(result.VersionId));
        });
    }

    [Test]
    public async Task SaveDraftAsync_TrackedCurrentDraftInstance_InsertFails_DetachesTheOldVersion()
    {
        // The caller hands back the very instance the store loads as the current draft (identity resolution).
        var (nodeId, _, _) = await CreatePublishedWithDraftAsync();
        var ctx = NewContext();
        var tracked = await ctx.Set<ContentBlockDTO>()
            .Include(e => e.Version).ThenInclude(v => v.Node)
            .SingleAsync(e => e.Version.NodeId == nodeId && e.Version.IsCurrentDraft);

        var result = await new FailingNthSaveStore<ContentBlockDTO>(ctx, 2).SaveDraftAsync(tracked, null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(ctx.ChangeTracker.Entries<ContentVersion>(), Is.Empty);
            Assert.That(ctx.ChangeTracker.Entries<ContentBlockDTO>(), Is.Empty);
        });
    }

    // ─── DeleteAsync ──────────────────────────────────────────────────────────

    [Test]
    public async Task DeleteAsync_NotFound_ReturnsFalse()
    {
        Assert.That(await NewStore().DeleteAsync(Guid.NewGuid(), false), Is.False);
    }

    [Test]
    public async Task DeleteAsync_SoftDelete_MarksNodeDeleted()
    {
        var nodeId = await CreatePublishedAsync("T");
        var ok = await NewStore().DeleteAsync(nodeId, softDelete: true);

        var ctx = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(ctx.Set<ContentNode>().Single().IsDeleted, Is.True);
        });
    }

    [Test]
    public async Task SaveDraftAsync_EditingSoftDeletedNode_DoesNotResurrectIt()
    {
        var nodeId = await CreatePublishedAsync("T");
        await NewStore().DeleteAsync(nodeId, softDelete: true);

        var dto = NewBlock("T2");
        dto.Version.Node = new ContentNode { Id = nodeId };
        var result = await NewStore().SaveDraftAsync(dto, null);

        var ctx = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(ctx.Set<ContentNode>().Single(n => n.Id == nodeId).IsDeleted, Is.True);
        });
    }

    [Test]
    public async Task DeleteAsync_HardDelete_RemovesEverything()
    {
        var nodeId = await CreatePublishedAsync("T");
        var ok = await NewStore().DeleteAsync(nodeId, softDelete: false);

        var ctx = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(ctx.Set<ContentBlockDTO>().Any(), Is.False);
            Assert.That(ctx.Set<ContentVersion>().Any(), Is.False);
            Assert.That(ctx.Set<ContentNode>().Any(), Is.False);
        });
    }

    // ─── DeleteVersionAsync ───────────────────────────────────────────────────

    [Test]
    public async Task DeleteVersionAsync_FoundAndNotFound()
    {
        var nodeId = await CreatePublishedAsync("v0");
        var store = NewStore();
        var current = (await store.GetAsync(nodeId))!;
        var v0Id = current.VersionId;
        await store.SaveDraftAsync(current with { Version = current.Version with { Title = "v1" } }, 0);
        await store.PublishAsync(nodeId);
        var published = (await store.GetAsync(nodeId))!;
        await store.SaveDraftAsync(published with { Version = published.Version with { Title = "v2" } }, 1);

        Assert.Multiple(async () =>
        {
            Assert.That(await store.DeleteVersionAsync(Guid.NewGuid()), Is.False);
            Assert.That(await store.DeleteVersionAsync(v0Id), Is.True, "v0 was archived by the v1 publish");
        });

        Assert.That((await store.GetAllVersionsAsync(nodeId)), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task DeleteVersionAsync_PublishedVersion_Refused()
    {
        var nodeId = await CreatePublishedAsync("v0");
        var store = NewStore();
        var current = (await store.GetAsync(nodeId))!;
        await store.SaveDraftAsync(current with { Version = current.Version with { Title = "v1" } }, 0);

        var ok = await store.DeleteVersionAsync(current.VersionId);

        Assert.Multiple(async () =>
        {
            Assert.That(ok, Is.False, "a published version is what is live; unpublish first");
            Assert.That(await store.GetAllVersionsAsync(nodeId), Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task DeleteVersionAsync_SingleVersion_Refused()
    {
        var nodeId = await CreatePublishedAsync("v0");
        var store = NewStore();
        var current = (await store.GetAsync(nodeId))!;

        var ok = await store.DeleteVersionAsync(current.VersionId);

        Assert.Multiple(async () =>
        {
            Assert.That(ok, Is.False);
            Assert.That(await store.GetAllVersionsAsync(nodeId), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task DeleteVersionAsync_MaxVersion_Refused()
    {
        var nodeId = await CreatePublishedAsync("v0");
        var store = NewStore();
        var current = (await store.GetAsync(nodeId))!;
        await store.SaveDraftAsync(current with { Version = current.Version with { Title = "v1" } }, 0);

        var versions = await store.GetAllVersionsAsync(nodeId);
        var maxVersionId = versions.Single(v => v.Version.VersionNumber == 1).VersionId;

        var ok = await store.DeleteVersionAsync(maxVersionId);

        Assert.Multiple(async () =>
        {
            Assert.That(ok, Is.False);
            Assert.That(await store.GetAllVersionsAsync(nodeId), Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task DeleteVersionAsync_NonEmptyCulture_NonMaxVersionDeletable()
    {
        var nodeId = Guid.NewGuid();
        var v0Id = Guid.NewGuid();
        var v1Id = Guid.NewGuid();

        var ctx = NewContext();
        var node = new ContentNode { Id = nodeId, ContentTypeKey = "contentblocks" };
        var v0 = new ContentVersion
        {
            Id = v0Id,
            NodeId = nodeId,
            Node = node,
            VersionNumber = 0,
            Culture = "fr",
            Segment = string.Empty,
            State = ContentVersionState.Archived
        };
        var v1 = new ContentVersion
        {
            Id = v1Id,
            NodeId = nodeId,
            Node = node,
            VersionNumber = 1,
            Culture = "fr",
            Segment = string.Empty,
            State = ContentVersionState.Published,
            IsCurrentDraft = true
        };
        ctx.Set<ContentBlockDTO>().AddRange(
            new ContentBlockDTO { VersionId = v0Id, Version = v0, Content = "c0" },
            new ContentBlockDTO { VersionId = v1Id, Version = v1, Content = "c1" });
        await ctx.SaveChangesAsync();

        var ok = await NewStore().DeleteVersionAsync(v0Id);

        Assert.That(ok, Is.True);
        Assert.That((await NewStore().GetAllVersionsAsync(nodeId)), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task PublishAsync_AmbientChangeSetScope_IsJoinedRatherThanMinted()
    {
        // Mirrors the page publish cascade: an ambient Publish change set (opened for the page) must
        // cover the zone items' own PublishAsync calls, so the composite save reads back as one
        // history entry instead of one change set per item.
        var ctx = NewContext();
        var scope = new ChangeSetScope(ctx, new DefaultContentUserContext());
        var store = new ContentStore<ContentBlockDTO>(
            ctx, new TestReadContext(ContentReadMode.Published), scope, new DefaultContentUserContext(), "contentblocks");

        var standalone = NewBlock("Standalone");
        await store.SaveDraftAsync(standalone, null);
        await store.PublishAsync(standalone.Version.Node!.Id);

        var cascaded = NewBlock("Cascaded");
        await store.SaveDraftAsync(cascaded, null);

        Guid ambientId;
        var pageNodeId = Guid.NewGuid();
        using (scope.Begin(ChangeSetKind.Publish, pageNodeId, null))
        {
            ambientId = scope.Current;
            await store.PublishAsync(cascaded.Version.Node!.Id);
        }

        await using var verify = NewContext();
        var changeSets = verify.Set<ChangeSet>().ToList();
        var cascadedVersion = verify.Set<ContentVersion>().Single(v => v.NodeId == cascaded.Version.Node!.Id);
        Assert.Multiple(() =>
        {
            Assert.That(cascadedVersion.ChangeSetId, Is.EqualTo(ambientId));
            Assert.That(changeSets.Any(cs => cs.Kind == ChangeSetKind.Publish && cs.RootNodeId == cascaded.Version.Node!.Id),
                Is.False, "the ambient page change set covers the item publish");
        });
    }

    // ─── page-set history snapshots ───────────────────────────────────────────

    private async Task<Guid> AddSnapshotAsync(Guid pageNodeId, Guid pageVersionId, params Guid[] itemVersionIds)
    {
        await using var ctx = NewContext();
        var snapshot = new PageSetSnapshot
        {
            Id = Guid.NewGuid(),
            PageNodeId = pageNodeId,
            PageVersionId = pageVersionId,
            CreatedUtc = DateTime.UtcNow,
            ItemCount = itemVersionIds.Length,
            Items = itemVersionIds.Select(id => new PageSetSnapshotItem { ItemVersionId = id, ItemNodeId = Guid.NewGuid() }).ToList()
        };
        ctx.Set<PageSetSnapshot>().Add(snapshot);
        await ctx.SaveChangesAsync();
        return snapshot.Id;
    }

    private async Task<(Guid NodeId, Guid VersionId)> CreateUnpublishedSnapshotVersionAsync(bool asItem)
    {
        var nodeId = await CreatePublishedAsync("Captured");
        var versionId = (await NewStore().GetCurrentDraftAsync(nodeId))!.VersionId;
        if (asItem)
            await AddSnapshotAsync(Guid.NewGuid(), Guid.NewGuid(), versionId);
        else
            await AddSnapshotAsync(nodeId, versionId);

        // An unpublish turns the captured live version back into the current draft.
        await NewStore().UnpublishAsync(nodeId);
        return (nodeId, versionId);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SaveDraftAsync_EditingASnapshotVersion_MintsANewVersionInstead(bool asItem)
    {
        var (nodeId, versionId) = await CreateUnpublishedSnapshotVersionAsync(asItem);
        var store = NewStore();
        var draft = (await store.GetCurrentDraftAsync(nodeId))!;

        var result = await store.SaveDraftAsync(draft with { Version = draft.Version with { Title = "Edited" } }, draft.Version.VersionNumber);

        await using var ctx = NewContext();
        var captured = await ctx.Set<ContentVersion>().SingleAsync(v => v.Id == versionId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.VersionId, Is.Not.EqualTo(versionId));
            Assert.That(captured.Title, Is.EqualTo("Captured"), "the captured version is frozen");
            Assert.That(captured.IsCurrentDraft, Is.False);
        });
    }

    [Test]
    public async Task DeleteAsync_HardDelete_RemovesEverySnapshotThatCapturedTheNode()
    {
        var nodeId = await CreatePublishedAsync("T");
        var versionId = (await NewStore().GetCurrentDraftAsync(nodeId))!.VersionId;
        await AddSnapshotAsync(nodeId, versionId);
        await AddSnapshotAsync(Guid.NewGuid(), Guid.NewGuid(), versionId);
        var unrelated = await AddSnapshotAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var ok = await NewStore().DeleteAsync(nodeId, softDelete: false);

        await using var ctx = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(ctx.Set<PageSetSnapshot>().Select(s => s.Id), Is.EqualTo(new[] { unrelated }));
            Assert.That(ctx.Set<PageSetSnapshotItem>().Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task DeleteVersionAsync_RemovesEverySnapshotThatCapturedTheVersion()
    {
        var nodeId = await CreatePublishedAsync("v0");
        var store = NewStore();
        var current = (await store.GetAsync(nodeId))!;
        await AddSnapshotAsync(Guid.NewGuid(), Guid.NewGuid(), current.VersionId);
        await store.SaveDraftAsync(current with { Version = current.Version with { Title = "v1" } }, 0);
        await store.PublishAsync(nodeId);

        var ok = await NewStore().DeleteVersionAsync(current.VersionId);

        await using var ctx = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(ctx.Set<PageSetSnapshot>().Any(), Is.False);
        });
    }

    // ─── change notes ─────────────────────────────────────────────────────────

    private (ContentStore<ContentBlockDTO> Store, ChangeSetScope Scope) NewStoreWithScope()
    {
        var ctx = NewContext();
        var scope = new ChangeSetScope(ctx, new DefaultContentUserContext());
        return (new ContentStore<ContentBlockDTO>(ctx, new TestReadContext(ContentReadMode.Published), scope,
            new DefaultContentUserContext(), "contentblocks"), scope);
    }

    [Test]
    public async Task SaveDraftAsync_ChangeNote_ComesFromTheScopeElseTheDefault()
    {
        var (store, scope) = NewStoreWithScope();
        var dto = NewBlock("T");
        await store.SaveDraftAsync(dto, null);
        using (scope.UseChangeNote("Content edited"))
            await store.SaveDraftAsync(dto with { Content = "c2", Version = dto.Version with { } }, 0);
        await store.SaveDraftAsync(dto with { Content = "c3", Version = dto.Version with { } }, 1);

        var notes = (await NewStore().GetAllVersionsAsync(dto.Version.Node!.Id)).Select(v => v.Version.ChangeNote);

        Assert.That(notes, Is.EqualTo(new[] { "Edited", "Content edited", "Created" }),
            "a note describes only the save that wrote it, never inherited by the next one");
    }

    [Test]
    public async Task SaveDraftAsync_TrackedIncomingEntity_IsSavedAsANewRowWithoutTouchingTheOld()
    {
        var ctx = NewContext();
        var store = NewStore(ctx);
        var dto = NewBlock("T");
        await store.SaveDraftAsync(dto, null);
        var firstVersionId = dto.VersionId;

        // The same (still tracked) instance, edited and saved again through the same context.
        dto.Content = "c2";
        var result = await store.SaveDraftAsync(dto, 0);

        var versions = await NewStore().GetAllVersionsAsync(dto.Version.Node.Id);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(versions, Has.Count.EqualTo(2));
            Assert.That(versions.Single(v => v.VersionId == firstVersionId).Content, Is.EqualTo("c"));
            Assert.That(versions.Single(v => v.Version.IsCurrentDraft).Content, Is.EqualTo("c2"));
        });
    }

    [Test]
    public async Task RestoreAsync_DefaultChangeNote_NamesTheRestoredVersion()
    {
        var nodeId = await CreatePublishedAsync("v0");
        var store = NewStore();
        var v0 = (await store.GetAsync(nodeId))!;
        await store.SaveDraftAsync(v0 with { Version = v0.Version with { Title = "v1" } }, 0);

        await NewStore().RestoreAsync(v0.VersionId);

        var current = (await NewStore().GetCurrentDraftAsync(nodeId))!;
        Assert.That(current.Version.ChangeNote, Is.EqualTo("Restored from version 0"));
    }

    [Test]
    public void ChangeSetScope_UseChangeNote_NestsAndRestoresThePreviousNote()
    {
        var scope = new ChangeSetScope(NewContext(), new DefaultContentUserContext());

        string? inner, outer;
        using (scope.UseChangeNote("outer"))
        {
            using (scope.UseChangeNote("inner"))
                inner = scope.CurrentChangeNote;
            outer = scope.CurrentChangeNote;
        }

        Assert.Multiple(() =>
        {
            Assert.That(inner, Is.EqualTo("inner"));
            Assert.That(outer, Is.EqualTo("outer"));
            Assert.That(scope.CurrentChangeNote, Is.Null);
        });
    }
}
