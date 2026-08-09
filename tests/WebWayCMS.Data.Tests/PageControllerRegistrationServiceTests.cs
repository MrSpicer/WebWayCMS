using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class PageControllerRegistrationServiceTests
{
    private string _db = null!;

    [SetUp]
    public void SetUp() => _db = TestContexts.NewDb();

    private CmsDbContext NewContext() => TestContexts.Cms(_db);

    private PageControllerRegistrationService NewService() => new(NewContext());

    private static PageControllerRegistrationDTO Dto(
        string controllerName = "GenericPage",
        string displayName = "Generic Page",
        string category = "General",
        bool isActive = true,
        bool isPublished = true,
        bool isDeleted = false,
        int version = 0,
        Guid? masterId = null) =>
        new()
        {
            ContentId = Guid.NewGuid(),
            ControllerName = controllerName,
            ControllerTypeName = "Type." + controllerName,
            DisplayName = displayName,
            Category = category,
            IsActive = isActive,
            PropertyDefinitionsJson = "[]",
            ContentMeta = new ContentDTO
            {
                Id = Guid.NewGuid(),
                MasterId = masterId ?? Guid.NewGuid(),
                Version = version,
                Title = displayName,
                Slug = controllerName.ToLowerInvariant(),
                IsPublished = isPublished,
                IsDeleted = isDeleted,
                CreationDate = DateTime.UtcNow,
                ModificationDate = DateTime.UtcNow,
            }
        };

    private async Task SeedAsync(params PageControllerRegistrationDTO[] dtos)
    {
        await using var ctx = NewContext();
        ctx.Set<PageControllerRegistrationDTO>().AddRange(dtos);
        await ctx.SaveChangesAsync();
    }

    [Test]
    public async Task GetActiveAsync_ReturnsOnlyActivePublishedNonDeleted()
    {
        await SeedAsync(
            Dto("A", isActive: true, isPublished: true),
            Dto("B", isActive: false, isPublished: true),
            Dto("C", isActive: true, isPublished: false),
            Dto("D", isActive: true, isPublished: true, isDeleted: true));

        var service = NewService();
        var result = await service.GetActiveAsync();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].ControllerName, Is.EqualTo("A"));
    }

    [Test]
    public async Task GetActiveAsync_ReturnsLatestVersionOnly()
    {
        var master = Guid.NewGuid();
        await SeedAsync(
            Dto("A", version: 0, masterId: master),
            Dto("A", version: 1, masterId: master));

        var service = NewService();
        var result = await service.GetActiveAsync();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].ContentMeta.Version, Is.EqualTo(1));
    }

    [Test]
    public async Task GetActiveAsync_SortedByCategoryThenOrderThenDisplayName()
    {
        await SeedAsync(
            Dto("C", "CCC", "Layout"),
            Dto("A", "AAA", "Content"),
            Dto("B", "BBB", "Content"));

        var service = NewService();
        var result = await service.GetActiveAsync();

        Assert.That(result.Select(p => p.DisplayName),
            Is.EqualTo(new[] { "AAA", "BBB", "CCC" }));
    }

    [Test]
    public async Task GetByControllerNameAsync_ReturnsController()
    {
        await SeedAsync(Dto("GenericPage", "Generic Page"));

        var service = NewService();
        var result = await service.GetByControllerNameAsync("GenericPage");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.DisplayName, Is.EqualTo("Generic Page"));
    }

    [Test]
    public async Task GetByControllerNameAsync_NotFound_ReturnsNull()
    {
        await SeedAsync(Dto("A"));

        var service = NewService();
        var result = await service.GetByControllerNameAsync("B");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetByControllerNameAsync_IgnoresDeleted()
    {
        await SeedAsync(Dto("A", isDeleted: true));

        var service = NewService();
        var result = await service.GetByControllerNameAsync("A");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetActiveByCategoryAsync_GroupsByCategory()
    {
        await SeedAsync(
            Dto("A", "A", "Content"),
            Dto("B", "B", "Content"),
            Dto("C", "C", "Layout"));

        var service = NewService();
        var result = await service.GetActiveByCategoryAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Keys, Has.Count.EqualTo(2));
            Assert.That(result["Content"], Has.Count.EqualTo(2));
            Assert.That(result["Layout"], Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task GetActiveByCategoryAsync_EmptyWhenNoControllers()
    {
        var service = NewService();
        var result = await service.GetActiveByCategoryAsync();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Constructor_NullContext_Throws()
    {
        Assert.That(() => new PageControllerRegistrationService(null!),
            Throws.ArgumentNullException);
    }
}
