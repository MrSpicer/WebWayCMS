using Microsoft.EntityFrameworkCore;

using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class PageServiceTests
{
    private string _db = null!;

    [SetUp]
    public void SetUp() => _db = TestContexts.NewDb();

    private CmsDbContext NewContext() => TestContexts.Cms(_db);

    private PageService NewService() => new(NewContext());

    private static PageDTO PageRow(Guid master, int version, bool published = true,
        bool deleted = false)
    {
        var id = Guid.NewGuid();
        return new PageDTO
        {
            ContentId = id,
            ContentMeta = new ContentDTO
            {
                Id = id,
                MasterId = master,
                Version = version,
                Title = "t",
                IsPublished = published,
                IsDeleted = deleted
            }
        };
    }

    private static PageDTO PageRow(ContentDTO meta) => new()
    {
        ContentId = meta.Id,
        ContentMeta = meta
    };

    private async Task SeedAsync(params PageDTO[] pages)
    {
        await using var ctx = NewContext();
        ctx.Set<PageDTO>().AddRange(pages);
        await ctx.SaveChangesAsync();
    }

    [Test]
    public void Constructor_NullContext_Throws()
    {
        Assert.That(() => new PageService(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task GetAllAsync_ReturnsLatestNonDeleted()
    {
        var m1 = Guid.NewGuid();
        var m2 = Guid.NewGuid();
        var m3 = Guid.NewGuid();
        await SeedAsync(
            PageRow(m1, 0),
            PageRow(m1, 1),
            PageRow(m2, 0),
            PageRow(m3, 0, deleted: true));

        var all = await NewService().GetAllAsync();

        Assert.Multiple(() =>
        {
            Assert.That(all, Has.Count.EqualTo(2));
            Assert.That(all.Single(p => p.ContentMeta.MasterId == m1).ContentMeta.Version, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GetByIdAsync_FoundAndNotFound()
    {
        var page = PageRow(Guid.NewGuid(), 0);
        await SeedAsync(page);

        Assert.Multiple(async () =>
        {
            Assert.That(await NewService().GetByIdAsync(page.ContentMeta.Id), Is.Not.Null);
            Assert.That(await NewService().GetByIdAsync(Guid.NewGuid()), Is.Null);
        });
    }

    [Test]
    public async Task GetAllVersionsAsync_OrderedDesc()
    {
        var m = Guid.NewGuid();
        await SeedAsync(PageRow(m, 0), PageRow(m, 1), PageRow(m, 2));

        var versions = await NewService().GetAllVersionsAsync(m);

        Assert.That(versions.Select(v => v.ContentMeta.Version), Is.EqualTo(new[] { 2, 1, 0 }));
    }

    [Test]
    public void CreateAsync_Null_Throws()
    {
        Assert.That(async () => await NewService().CreateAsync(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task CreateAsync_InitializesVersioning()
    {
        var created = await NewService().CreateAsync(new PageDTO { ContentMeta = new ContentDTO { Title = "t" } });

        Assert.Multiple(() =>
        {
            Assert.That(created.ContentMeta.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(created.ContentId, Is.EqualTo(created.ContentMeta.Id));
            Assert.That(created.ContentMeta.MasterId, Is.EqualTo(created.ContentMeta.Id));
            Assert.That(created.ContentMeta.Version, Is.EqualTo(0));
            Assert.That(created.ContentMeta.PublicationDate, Is.Not.EqualTo(default(DateTime)));
        });
    }

    [Test]
    public async Task CreateAsync_PresetIdAndPublicationDate_AreKept()
    {
        var id = Guid.NewGuid();
        var when = new DateTime(2020, 5, 5, 0, 0, 0, DateTimeKind.Utc);
        var created = await NewService().CreateAsync(new PageDTO { ContentMeta = new ContentDTO { Id = id, Title = "t", PublicationDate = when } });

        Assert.Multiple(() =>
        {
            Assert.That(created.ContentMeta.Id, Is.EqualTo(id));
            Assert.That(created.ContentMeta.PublicationDate, Is.EqualTo(when));
        });
    }

    [Test]
    public void UpdateAsync_Null_Throws()
    {
        Assert.That(async () => await NewService().UpdateAsync(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task UpdateAsync_NonExistent_ReturnsFalse()
    {
        Assert.That(await NewService().UpdateAsync(PageRow(Guid.NewGuid(), 0)), Is.False);
    }

    [Test]
    public async Task UpdateAsync_Published_CreatesVersionAndUnpublishesPrevious()
    {
        var m = Guid.NewGuid();
        var existing = PageRow(m, 0, published: true);
        await SeedAsync(existing);

        var ok = await NewService().UpdateAsync(PageRow(new ContentDTO { Id = existing.ContentMeta.Id, MasterId = m, Version = 0, Title = "t", IsPublished = true }));

        await using var verify = NewContext();
        var versions = await verify.Set<PageDTO>().Where(p => p.ContentMeta.MasterId == m).ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(versions, Has.Count.EqualTo(2));
            Assert.That(versions.Count(p => p.ContentMeta.IsPublished), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task UpdateAsync_Unpublished_SkipsUnpublishStep()
    {
        var m = Guid.NewGuid();
        var existing = PageRow(m, 0, published: false);
        await SeedAsync(existing);

        var ok = await NewService().UpdateAsync(PageRow(new ContentDTO { Id = existing.ContentMeta.Id, MasterId = m, Version = 0, Title = "t", IsPublished = false }));

        Assert.That(ok, Is.True);
    }

    [Test]
    public async Task UpdateAsync_PublishedWithDefaultPublicationDate_SetsIt()
    {
        var m = Guid.NewGuid();
        var existing = PageRow(m, 0, published: false);
        await SeedAsync(existing);

        var update = PageRow(new ContentDTO { Id = existing.ContentMeta.Id, MasterId = m, Version = 0, Title = "t", IsPublished = true, PublicationDate = default });
        await NewService().UpdateAsync(update);

        Assert.That(update.ContentMeta.PublicationDate, Is.Not.EqualTo(default(DateTime)));
    }

    [Test]
    public async Task DeleteAsync_NotFound_ReturnsFalse()
    {
        Assert.That(await NewService().DeleteAsync(Guid.NewGuid()), Is.False);
    }

    [Test]
    public async Task DeleteAsync_RemovesAllVersions()
    {
        var m = Guid.NewGuid();
        await SeedAsync(PageRow(m, 0), PageRow(m, 1));
        var id = (await NewService().GetAllVersionsAsync(m)).First().ContentMeta.Id;

        var ok = await NewService().DeleteAsync(id);

        await using var verify = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(verify.Set<PageDTO>().Any(p => p.ContentMeta.MasterId == m), Is.False);
        });
    }

    [Test]
    public async Task DeleteVersionAsync_NotFoundAndFound()
    {
        var m = Guid.NewGuid();
        await SeedAsync(PageRow(m, 0), PageRow(m, 1));
        var id = (await NewService().GetAllVersionsAsync(m)).First().ContentMeta.Id;

        Assert.Multiple(async () =>
        {
            Assert.That(await NewService().DeleteVersionAsync(Guid.NewGuid()), Is.False);
            Assert.That(await NewService().DeleteVersionAsync(id), Is.True);
        });

        await using var verify = NewContext();
        Assert.That(verify.Set<PageDTO>().Count(p => p.ContentMeta.MasterId == m), Is.EqualTo(1));
    }
}
