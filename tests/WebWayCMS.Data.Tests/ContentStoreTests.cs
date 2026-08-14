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
    public async Task GetChildrenAsync_And_GetRootsAsync()
    {
        var ctx = NewContext();
        var store = NewStore(ctx);
        var child = NewBlock("Child");
        child.Version.Node!.ParentNodeId = Guid.NewGuid();
        await store.SaveDraftAsync(child, null);
        await store.PublishAsync(child.Version.Node.Id);

        var root = NewBlock("Root");
        await store.SaveDraftAsync(root, null);
        await store.PublishAsync(root.Version.Node.Id);

        var children = await store.GetChildrenAsync(child.Version.Node.ParentNodeId.Value);
        var roots = await store.GetRootsAsync();
        Assert.Multiple(() =>
        {
            Assert.That(children, Has.Count.EqualTo(1));
            Assert.That(children[0].Version.Title, Is.EqualTo("Child"));
            Assert.That(roots, Has.Count.EqualTo(1));
            Assert.That(roots[0].Version.Title, Is.EqualTo("Root"));
        });
    }

    // ─── version-explicit ─────────────────────────────────────────────────────

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
            Assert.That(dto.Version.Slug, Is.EqualTo(Uri.EscapeDataString("Hello World")));
            Assert.That(dto.Version.Node.ContentTypeKey, Is.EqualTo("contentblocks"));
        });
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
    public async Task SaveDraftAsync_EditDraft_UpdatesInPlace()
    {
        var dto = NewBlock("T");
        var store = NewStore();
        await store.SaveDraftAsync(dto, null);

        var edit = dto with { Content = "c2", Version = dto.Version with { Title = "T2" } };
        var result = await store.SaveDraftAsync(edit, 0);

        var versions = await store.GetAllVersionsAsync(dto.Version.Node!.Id);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(versions, Has.Count.EqualTo(1), "repeated draft saves must not mint versions");
            Assert.That(versions[0].Content, Is.EqualTo("c2"));
            Assert.That(versions[0].Version.Title, Is.EqualTo("T2"));
        });
    }

    [Test]
    public async Task SaveDraftAsync_EditDraft_WithUnsetIncomingKey_PreservesKey()
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

        var versions = await store.GetAllVersionsAsync(dto.Version.Node.Id);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(versions, Has.Count.EqualTo(1), "repeated draft saves must not mint versions");
            Assert.That(versions[0].Content, Is.EqualTo("c2"));
            Assert.That(versions[0].Version.Title, Is.EqualTo("T2"));
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

        Assert.Multiple(async () =>
        {
            Assert.That(await store.DeleteVersionAsync(Guid.NewGuid()), Is.False);
            Assert.That(await store.DeleteVersionAsync(v0Id), Is.True);
        });

        Assert.That((await store.GetAllVersionsAsync(nodeId)), Has.Count.EqualTo(1));
    }
}
