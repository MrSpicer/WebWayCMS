using Microsoft.EntityFrameworkCore;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Data.Services.Registrations;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class VersionedRegistrationSyncTargetTests
{
    private string _db = null!;

    [SetUp]
    public void SetUp() => _db = TestContexts.NewDb();

    private CmsDbContext NewContext() => TestContexts.Cms(_db);

    private static VersionedRegistrationSyncTarget<ContentTypeRegistrationDTO> Target(CmsDbContext ctx, IContentStore<ContentTypeRegistrationDTO>? store = null)
        => new(ctx, store ?? TestStore.Create<ContentTypeRegistrationDTO>(ctx, "contenttypes"), r => r.ContentTypeKey);

    private static ContentTypeRegistrationDTO New(string key) => new()
    {
        Version = new ContentVersion { Title = key, Slug = key },
        ContentTypeKey = key,
        ClrTypeName = "Old." + key,
        DisplayName = key,
        PluralDisplayName = key + "s",
    };

    [Test]
    public async Task InsertAsync_SavesAndPublishes()
    {
        await using var ctx = NewContext();

        var result = await Target(ctx).InsertAsync(New("pages"));

        await using var check = NewContext();
        var row = await check.Set<ContentTypeRegistrationDTO>().SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo((true, (string?)null)));
            Assert.That(row.Version.State, Is.EqualTo(ContentVersionState.Published));
            Assert.That(row.ContentTypeKey, Is.EqualTo("pages"));
        });
    }

    [Test]
    public async Task LoadRows_SaveCodeOwnedChanges_UpdatesEveryVersionInPlace_AndSkipsDeletedNodes()
    {
        await using (var ctx = NewContext())
        {
            var store = TestStore.Create<ContentTypeRegistrationDTO>(ctx, "contenttypes");
            var saved = await store.SaveDraftAsync(New("pages"), null);
            var draft = (await store.GetCurrentDraftAsync(saved.NodeId))!;
            await store.SaveDraftAsync(draft with { DisplayName = "Edited" }, draft.Version.VersionNumber);
            var deleted = await store.SaveDraftAsync(New("gone"), null);
            await store.DeleteAsync(deleted.NodeId, softDelete: true);
        }

        await using (var ctx = NewContext())
        {
            var target = Target(ctx);
            var rows = await target.LoadRowsAsync();
            Assert.That(rows.Select(target.KeyOf), Is.EqualTo(new[] { "pages", "pages" }));

            foreach (var row in rows)
                row.ClrTypeName = "New.pages";
            await target.SaveCodeOwnedChangesAsync();
            Assert.That(ctx.ChangeTracker.Entries(), Is.Empty);
        }

        await using var check = NewContext();
        var versions = await check.Set<ContentTypeRegistrationDTO>().Where(r => r.ContentTypeKey == "pages").ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(versions, Has.Count.EqualTo(2));
            Assert.That(versions.Select(v => v.ClrTypeName), Is.All.EqualTo("New.pages"));
            Assert.That(versions.Select(v => v.DisplayName), Is.EquivalentTo(new[] { "pages", "Edited" }));
        });
    }

    [Test]
    public async Task InsertAsync_SaveFails_ReturnsTheStoreError_WithoutPublishing()
    {
        await using var ctx = NewContext();
        var store = Substitute.For<IContentStore<ContentTypeRegistrationDTO>>();
        store.SaveDraftAsync(Arg.Any<ContentTypeRegistrationDTO>(), null, Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(false, "Nope."));

        var result = await Target(ctx, store).InsertAsync(New("pages"));

        Assert.That(result, Is.EqualTo((false, (string?)"Nope.")));
        await store.DidNotReceiveWithAnyArgs().PublishAsync(default);
    }

    [Test]
    public async Task InsertAsync_PublishFails_ReturnsThePublishError()
    {
        await using var ctx = NewContext();
        var nodeId = Guid.NewGuid();
        var store = Substitute.For<IContentStore<ContentTypeRegistrationDTO>>();
        store.SaveDraftAsync(Arg.Any<ContentTypeRegistrationDTO>(), null, Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true, NodeId: nodeId));
        store.PublishAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(false, "Not live."));

        var result = await Target(ctx, store).InsertAsync(New("pages"));

        Assert.That(result, Is.EqualTo((false, (string?)"Not live.")));
    }

    [Test]
    public void Ctor_NullArguments_Throw()
    {
        using var ctx = NewContext();
        var store = Substitute.For<IContentStore<ContentTypeRegistrationDTO>>();

        Assert.Multiple(() =>
        {
            Assert.That(() => new VersionedRegistrationSyncTarget<ContentTypeRegistrationDTO>(null!, store, r => r.ContentTypeKey), Throws.ArgumentNullException);
            Assert.That(() => new VersionedRegistrationSyncTarget<ContentTypeRegistrationDTO>(ctx, null!, r => r.ContentTypeKey), Throws.ArgumentNullException);
            Assert.That(() => new VersionedRegistrationSyncTarget<ContentTypeRegistrationDTO>(ctx, store, null!), Throws.ArgumentNullException);
        });
    }
}
