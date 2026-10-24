using Microsoft.EntityFrameworkCore;

using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class ContentQueryExtensionsTests
{
    private string _db = null!;

    [SetUp]
    public void SetUp() => _db = TestContexts.NewDb();

    private CmsDbContext NewContext() => TestContexts.Cms(_db);

    private sealed class SnapshotReadContext : IContentReadContext
    {
        public SnapshotReadContext(ContentReadSnapshot? snapshot) => Snapshot = snapshot;

        public ContentReadMode Mode => ContentReadMode.Snapshot;
        public string Culture => string.Empty;
        public string Segment => string.Empty;
        public ContentReadSnapshot? Snapshot { get; }
    }

    /// <summary>A node with an archived v0 and a published, current v1.</summary>
    private async Task<(Guid NodeId, Guid Archived, Guid Published)> SeedNodeAsync()
    {
        var node = new ContentNode { Id = Guid.NewGuid(), ContentTypeKey = "contentblocks" };
        var archived = Block(node, 0, ContentVersionState.Archived, currentDraft: false);
        var published = Block(node, 1, ContentVersionState.Published, currentDraft: true);
        await using var ctx = NewContext();
        ctx.Set<ContentBlockDTO>().AddRange(archived, published);
        await ctx.SaveChangesAsync();
        return (node.Id, archived.VersionId, published.VersionId);
    }

    private static ContentBlockDTO Block(ContentNode node, int number, ContentVersionState state, bool currentDraft)
    {
        var id = Guid.NewGuid();
        return new ContentBlockDTO
        {
            VersionId = id,
            Content = $"v{number}",
            Version = new ContentVersion
            {
                Id = id,
                NodeId = node.Id,
                Node = node,
                VersionNumber = number,
                State = state,
                IsCurrentDraft = currentDraft
            }
        };
    }

    private async Task<List<Guid>> ReadAsync(IContentReadContext readContext)
    {
        await using var ctx = NewContext();
        return await ctx.Set<ContentBlockDTO>().AsNoTracking()
            .AtReadContext(readContext)
            .Select(b => b.VersionId)
            .ToListAsync();
    }

    [Test]
    public async Task AtReadContext_Snapshot_GovernedNodeReadsTheCapturedVersion()
    {
        var (nodeId, archived, _) = await SeedNodeAsync();

        var read = await ReadAsync(new SnapshotReadContext(new ContentReadSnapshot([nodeId], [archived])));

        Assert.That(read, Is.EqualTo(new[] { archived }));
    }

    [Test]
    public async Task AtReadContext_Snapshot_GovernedNodeNotCaptured_ReadsNothing()
    {
        var (nodeId, _, _) = await SeedNodeAsync();

        var read = await ReadAsync(new SnapshotReadContext(new ContentReadSnapshot([nodeId], [])));

        Assert.That(read, Is.Empty);
    }

    [Test]
    public async Task AtReadContext_Snapshot_UngovernedNodeReadsPublished()
    {
        var (_, archived, published) = await SeedNodeAsync();

        var read = await ReadAsync(new SnapshotReadContext(new ContentReadSnapshot([Guid.NewGuid()], [archived])));

        Assert.That(read, Is.EqualTo(new[] { published }));
    }

    [Test]
    public async Task AtReadContext_SnapshotModeWithoutSnapshot_ReadsPublished()
    {
        var (_, _, published) = await SeedNodeAsync();

        var read = await ReadAsync(new SnapshotReadContext(null));

        Assert.That(read, Is.EqualTo(new[] { published }));
    }

    [Test]
    public void Snapshot_DefaultInterfaceMember_IsNull()
    {
        IContentReadContext readContext = new TestReadContext(ContentReadMode.Published);

        Assert.That(readContext.Snapshot, Is.Null);
    }
}
