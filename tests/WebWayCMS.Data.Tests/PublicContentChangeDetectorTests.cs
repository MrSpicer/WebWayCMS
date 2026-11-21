using Microsoft.EntityFrameworkCore;

using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Data.Services.Caching;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class PublicContentChangeDetectorTests
{
    private string _db = null!;

    [SetUp]
    public void SetUp() => _db = TestContexts.NewDb();

    private CmsDbContext NewContext() => TestContexts.Cms(_db);

    private static bool Detect(CmsDbContext ctx) => PublicContentChangeDetector.IsPublicChange(ctx.ChangeTracker.Entries());

    private static ContentVersion AddVersion(CmsDbContext ctx, ContentVersionState state)
    {
        var version = new ContentVersion
        {
            Id = Guid.NewGuid(),
            NodeId = Guid.NewGuid(),
            State = state,
        };
        ctx.Add(version);
        return version;
    }

    private static ContentVersion SavedVersion(CmsDbContext ctx, ContentVersionState state)
    {
        var version = AddVersion(ctx, state);
        ctx.SaveChanges();
        return version;
    }

    private static ContentRelation AddRelation(CmsDbContext ctx, ContentVersion source, EntityState state)
    {
        var relation = new ContentRelation
        {
            Id = Guid.NewGuid(),
            SourceVersionId = source.Id,
            TargetNodeId = Guid.NewGuid(),
            Name = "Test",
        };
        ctx.Add(relation);
        if (state == EntityState.Deleted)
            ctx.Remove(relation);
        return relation;
    }

    // ─── version entries ──────────────────────────────────────────────────────

    [Test]
    public void IsPublicChange_AddedPublishedVersion_True()
    {
        using var ctx = NewContext();
        AddVersion(ctx, ContentVersionState.Published);
        Assert.That(Detect(ctx), Is.True);
    }

    [Test]
    public void IsPublicChange_AddedDraftVersion_False()
    {
        using var ctx = NewContext();
        AddVersion(ctx, ContentVersionState.Draft);
        Assert.That(Detect(ctx), Is.False);
    }

    [Test]
    public void IsPublicChange_ModifiedVersionThatWasPublished_True()
    {
        using var ctx = NewContext();
        var version = SavedVersion(ctx, ContentVersionState.Published);
        version.ChangeNote = "changed";
        Assert.That(Detect(ctx), Is.True);
    }

    [Test]
    public void IsPublicChange_ModifiedPublishedVersionOnlyCurrentDraftFlag_False()
    {
        // Minting a draft on a live item flips the published version's IsCurrentDraft (the
        // one-current-draft invariant). Visitors still see the same published content: no bump.
        using var ctx = NewContext();
        var version = AddVersion(ctx, ContentVersionState.Published);
        version.IsCurrentDraft = true;
        ctx.SaveChanges();
        version.IsCurrentDraft = false;
        Assert.That(Detect(ctx), Is.False);
    }

    [Test]
    public void IsPublicChange_ModifiedPublishedVersionCurrentDraftFlagPlusContent_True()
    {
        // The same flag flip plus a real column change (e.g. a re-publish edit) is public.
        using var ctx = NewContext();
        var version = AddVersion(ctx, ContentVersionState.Published);
        version.IsCurrentDraft = true;
        ctx.SaveChanges();
        version.IsCurrentDraft = false;
        version.ChangeNote = "re-edited";
        Assert.That(Detect(ctx), Is.True);
    }

    [Test]
    public void IsPublicChange_ModifiedVersionBecomingPublished_True()
    {
        using var ctx = NewContext();
        var version = SavedVersion(ctx, ContentVersionState.Draft);
        version.State = ContentVersionState.Published;
        Assert.That(Detect(ctx), Is.True);
    }

    [Test]
    public void IsPublicChange_ModifiedVersionStillDraft_False()
    {
        using var ctx = NewContext();
        var version = SavedVersion(ctx, ContentVersionState.Draft);
        version.ChangeNote = "still a draft";
        Assert.That(Detect(ctx), Is.False);
    }

    [Test]
    public void IsPublicChange_DeletedPublishedVersion_True()
    {
        using var ctx = NewContext();
        var version = SavedVersion(ctx, ContentVersionState.Published);
        ctx.Remove(version);
        Assert.That(Detect(ctx), Is.True);
    }

    [Test]
    public void IsPublicChange_DeletedDraftVersion_False()
    {
        using var ctx = NewContext();
        var version = SavedVersion(ctx, ContentVersionState.Draft);
        ctx.Remove(version);
        Assert.That(Detect(ctx), Is.False);
    }

    [Test]
    public void IsPublicChange_UnchangedPublishedVersion_False()
    {
        // Tracked but untouched: nothing in this save changes what visitors see.
        using var ctx = NewContext();
        var version = SavedVersion(ctx, ContentVersionState.Published);
        Assert.Multiple(() =>
        {
            Assert.That(ctx.Entry(version).State, Is.EqualTo(EntityState.Unchanged));
            Assert.That(Detect(ctx), Is.False);
        });
    }

    // ─── content rows (IVersionedContent) ─────────────────────────────────────

    /// <summary>
    /// Loads the content row into the context without tracking its version (the in-place
    /// registration-sync shape), then modifies it: the detector must decide from the version
    /// entry, the loaded navigation, or neither.
    /// </summary>
    private static ContentBlockDTO LoadRowThenModify(CmsDbContext ctx, Guid versionId, ContentVersion? loadedVersion)
    {
        var dto = new ContentBlockDTO { VersionId = versionId, Content = "old", Version = loadedVersion };
        ctx.Attach(dto);
        dto.Content = "new";
        return dto;
    }

    [Test]
    public void IsPublicChange_ContentRowModifiedInPlaceOnPublishedVersion_True()
    {
        // The in-place code-owned registration sync writes the row's code-owned columns without
        // touching the version; the same save's published version entry makes it public.
        using var ctx = NewContext();
        var version = AddVersion(ctx, ContentVersionState.Published);
        LoadRowThenModify(ctx, version.Id, null);
        Assert.That(Detect(ctx), Is.True);
    }

    [Test]
    public void IsPublicChange_ContentRowModifiedDraftVersion_False()
    {
        // The version is saved in the database but not tracked by this context: the detector
        // sees only the row, and a draft edit is not public.
        Guid versionId;
        using (var seed = NewContext())
        {
            versionId = AddVersion(seed, ContentVersionState.Draft).Id;
            seed.SaveChanges();
        }
        using var ctx = NewContext();
        LoadRowThenModify(ctx, versionId, null);
        Assert.That(Detect(ctx), Is.False);
    }

    [Test]
    public void IsPublicChange_ContentRowModifiedUnderLivePublishedVersion_True()
    {
        // The version is published in the database and tracked, though this save does not write it:
        // a change under a live version is a public change.
        using var ctx = NewContext();
        var version = SavedVersion(ctx, ContentVersionState.Published);
        LoadRowThenModify(ctx, version.Id, null);
        Assert.That(Detect(ctx), Is.True);
    }

    [Test]
    public void IsPublicChange_ContentRowModifiedUnderTrackedDraftVersion_False()
    {
        using var ctx = NewContext();
        var version = SavedVersion(ctx, ContentVersionState.Draft);
        LoadRowThenModify(ctx, version.Id, version);
        Assert.That(Detect(ctx), Is.False);
    }

    [Test]
    public void IsPublicChange_ContentRowDeletedWithPublishedVersion_True()
    {
        using var ctx = NewContext();
        var version = SavedVersion(ctx, ContentVersionState.Published);
        var dto = LoadRowThenModify(ctx, version.Id, version);
        ctx.Remove(dto);
        Assert.That(Detect(ctx), Is.True);
    }

    [Test]
    public void IsPublicChange_ContentRowAdded_False()
    {
        // A brand-new row's visibility is decided by its version entry, not by the add itself.
        using var ctx = NewContext();
        var dto = new ContentBlockDTO
        {
            VersionId = Guid.NewGuid(),
            Content = "new",
            Version = new ContentVersion { State = ContentVersionState.Draft },
        };
        ctx.Add(dto);
        Assert.That(Detect(ctx), Is.False);
    }

    // ─── nodes, routes, relations ─────────────────────────────────────────────

    [Test]
    public void IsPublicChange_ContentNodeModified_True()
    {
        using var ctx = NewContext();
        var node = new ContentNode { Id = Guid.NewGuid(), ContentTypeKey = "contentblocks" };
        ctx.Add(node);
        ctx.SaveChanges();
        node.IsHidden = true;
        Assert.That(Detect(ctx), Is.True);
    }

    [Test]
    public void IsPublicChange_ContentNodeDeleted_True()
    {
        using var ctx = NewContext();
        var node = new ContentNode { Id = Guid.NewGuid(), ContentTypeKey = "contentblocks" };
        ctx.Add(node);
        ctx.SaveChanges();
        ctx.Remove(node);
        Assert.That(Detect(ctx), Is.True);
    }

    [Test]
    public void IsPublicChange_ContentNodeAdded_False()
    {
        using var ctx = NewContext();
        ctx.Add(new ContentNode { Id = Guid.NewGuid(), ContentTypeKey = "contentblocks" });
        Assert.That(Detect(ctx), Is.False);
    }

    [TestCase(EntityState.Added)]
    [TestCase(EntityState.Modified)]
    [TestCase(EntityState.Deleted)]
    public void IsPublicChange_CMSRouteChange_True(EntityState state)
    {
        using var ctx = NewContext();
        var route = new CMSRouteDTO { Id = Guid.NewGuid(), Pattern = "/x" };
        ctx.Add(route);
        if (state == EntityState.Added)
        {
            // Not saved yet: the entry is Added, which is a public change on its own.
        }
        else
        {
            ctx.SaveChanges();
            if (state == EntityState.Modified)
                route.Pattern = "/y";
            else
                ctx.Remove(route);
        }

        Assert.That(Detect(ctx), Is.True, $"state={state}");
    }

    [Test]
    public void IsPublicChange_CMSRouteUnchanged_False()
    {
        using var ctx = NewContext();
        var route = new CMSRouteDTO { Id = Guid.NewGuid(), Pattern = "/x" };
        ctx.Add(route);
        ctx.SaveChanges();
        Assert.Multiple(() =>
        {
            Assert.That(ctx.Entry(route).State, Is.EqualTo(EntityState.Unchanged));
            Assert.That(Detect(ctx), Is.False);
        });
    }

    [Test]
    public void IsPublicChange_RelationAddedToPublishedVersion_True()
    {
        using var ctx = NewContext();
        AddVersion(ctx, ContentVersionState.Published);
        var source = ctx.ChangeTracker.Entries<ContentVersion>().Single().Entity;
        AddRelation(ctx, source, EntityState.Added);
        Assert.That(Detect(ctx), Is.True);
    }

    [Test]
    public void IsPublicChange_RelationAddedToDraftVersion_False()
    {
        using var ctx = NewContext();
        var source = AddVersion(ctx, ContentVersionState.Draft);
        AddRelation(ctx, source, EntityState.Added);
        Assert.That(Detect(ctx), Is.False);
    }

    [Test]
    public void IsPublicChange_RelationRemovedFromPublishedVersion_True()
    {
        using var ctx = NewContext();
        var source = SavedVersion(ctx, ContentVersionState.Published);
        AddRelation(ctx, source, EntityState.Added);
        ctx.SaveChanges();
        var relation = ctx.Set<ContentRelation>().Single();
        ctx.Remove(relation);
        Assert.That(Detect(ctx), Is.True);
    }

    [Test]
    public void IsPublicChange_RelationRemovedFromDraftVersion_False()
    {
        using var ctx = NewContext();
        var source = SavedVersion(ctx, ContentVersionState.Draft);
        AddRelation(ctx, source, EntityState.Added);
        ctx.SaveChanges();
        var relation = ctx.Set<ContentRelation>().Single();
        ctx.Remove(relation);
        Assert.That(Detect(ctx), Is.False);
    }

    [Test]
    public void IsPublicChange_RelationModifiedInPlace_False()
    {
        // An ordinal rewritten in place on its own: relation moves mint a new version, so the
        // row alone is not a public change.
        using var ctx = NewContext();
        var source = SavedVersion(ctx, ContentVersionState.Published);
        var relation = AddRelation(ctx, source, EntityState.Added);
        ctx.SaveChanges();
        relation.Ordinal = 3;
        Assert.That(Detect(ctx), Is.False);
    }

    // ─── ignored entities and combinations ────────────────────────────────────

    [Test]
    public void IsPublicChange_OnlyIgnoredEntities_False()
    {
        using var ctx = NewContext();
        ctx.Add(new ChangeSet { Id = Guid.NewGuid() });
        ctx.Add(new PageSetSnapshot { Id = Guid.NewGuid(), PageNodeId = Guid.NewGuid(), PageVersionId = Guid.NewGuid(), Kind = PageSetSnapshotKind.Draft });
        ctx.Add(new ContentSeedRecordDTO { SeedId = Guid.NewGuid(), ContentTypeKey = "x", NodeId = Guid.NewGuid(), ContentHash = "h", Source = "s" });
        ctx.Add(new MediaBlobDTO { Hash = "h", ContentType = "image/png", ByteLength = 1 });
        ctx.Add(new UserProfileDTO { UserId = Guid.NewGuid().ToString() });
        ctx.Add(new ApiTokenDTO { Id = Guid.NewGuid(), UserId = Guid.NewGuid().ToString(), Name = "n", TokenHash = "t" });
        ctx.Add(new FormComponentRegistrationDTO { Id = Guid.NewGuid(), ComponentName = "c" });
        Assert.That(Detect(ctx), Is.False);
    }

    [Test]
    public void IsPublicChange_DraftChangesPlusOnePublicChange_True()
    {
        using var ctx = NewContext();
        AddVersion(ctx, ContentVersionState.Draft);
        var node = new ContentNode { Id = Guid.NewGuid(), ContentTypeKey = "contentblocks" };
        ctx.Add(node);
        ctx.SaveChanges();
        node.IsArchived = true;
        Assert.That(Detect(ctx), Is.True);
    }

    [Test]
    public void IsPublicChange_EmptySet_False()
    {
        using var ctx = NewContext();
        Assert.That(PublicContentChangeDetector.IsPublicChange(ctx.ChangeTracker.Entries()), Is.False);
    }
}
