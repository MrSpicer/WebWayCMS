using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services.ContentTypes;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class ContentTypeRegistrationServiceTests
{
    private string _db = null!;

    [SetUp]
    public void SetUp() => _db = TestContexts.NewDb();

    private CmsDbContext NewContext() => TestContexts.Cms(_db);

    private static ContentTypeRegistrationDTO Dto(
        string key,
        string displayName,
        ContentVersionState state = ContentVersionState.Published,
        bool isDeleted = false,
        DateTime? created = null)
    {
        var node = new ContentNode
        {
            Id = Guid.NewGuid(),
            ContentTypeKey = "contenttypes",
            IsDeleted = isDeleted,
            CreatedUtc = created ?? DateTime.UtcNow,
        };
        var versionId = Guid.NewGuid();
        return new ContentTypeRegistrationDTO
        {
            VersionId = versionId,
            ContentTypeKey = key,
            ClrTypeName = "Type." + key,
            DisplayName = displayName,
            PluralDisplayName = displayName + "s",
            Version = new ContentVersion
            {
                Id = versionId,
                NodeId = node.Id,
                Node = node,
                State = state,
                Title = displayName,
                CreatedUtc = DateTime.UtcNow,
            },
        };
    }

    private async Task SeedAsync(params ContentTypeRegistrationDTO[] dtos)
    {
        await using var ctx = NewContext();
        ctx.Set<ContentTypeRegistrationDTO>().AddRange(dtos);
        await ctx.SaveChangesAsync();
    }

    [Test]
    public async Task GetPublishedByKeyAsync_ReturnsPublishedNonDeleted_ByKeyCaseInsensitively()
    {
        await SeedAsync(
            Dto("pages", "Page"),
            Dto("articles", "Article", state: ContentVersionState.Draft),
            Dto("images", "Image", isDeleted: true));

        var result = await new ContentTypeRegistrationService(NewContext()).GetPublishedByKeyAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Keys, Is.EquivalentTo(new[] { "pages" }));
            Assert.That(result["PAGES"].DisplayName, Is.EqualTo("Page"));
        });
    }

    [Test]
    public async Task GetPublishedByKeyAsync_DuplicateKey_OldestWins()
    {
        await SeedAsync(
            Dto("pages", "Newer", created: DateTime.UtcNow),
            Dto("pages", "Older", created: DateTime.UtcNow.AddDays(-1)));

        var result = await new ContentTypeRegistrationService(NewContext()).GetPublishedByKeyAsync();

        Assert.That(result["pages"].DisplayName, Is.EqualTo("Older"));
    }

    [Test]
    public async Task GetPublishedByKeyAsync_IsReadOncePerInstance()
    {
        await SeedAsync(Dto("pages", "Page"));
        var service = new ContentTypeRegistrationService(NewContext());

        var first = await service.GetPublishedByKeyAsync();
        await SeedAsync(Dto("articles", "Article"));
        var second = await service.GetPublishedByKeyAsync();
        var sync = service.GetPublishedByKey();

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.SameAs(first));
            Assert.That(sync, Is.SameAs(first));
        });
    }

    [Test]
    public async Task GetPublishedByKey_ReadsSynchronously_AndIsReadOncePerInstance()
    {
        await SeedAsync(Dto("pages", "Page"));
        var service = new ContentTypeRegistrationService(NewContext());

        var first = service.GetPublishedByKey();
        var second = await service.GetPublishedByKeyAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first["pages"].DisplayName, Is.EqualTo("Page"));
            Assert.That(second, Is.SameAs(first));
        });
    }

    [Test]
    public void Ctor_NullContext_Throws()
        => Assert.That(() => new ContentTypeRegistrationService(null!), Throws.ArgumentNullException);
}
