using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class FormComponentRegistrationServiceTests
{
    private string _db = null!;

    [SetUp]
    public void SetUp() => _db = TestContexts.NewDb();

    private CmsDbContext NewContext() => TestContexts.Cms(_db);

    private FormComponentRegistrationService NewService() => new(NewContext());

    private static ContentNode Node(Guid id, bool isDeleted = false)
        => new() { Id = id, ContentTypeKey = "formcomponents", IsDeleted = isDeleted };

    private static FormComponentRegistrationDTO Component(
        string componentName = "TestComponent",
        string displayName = "Test Component",
        string category = "General",
        bool isActive = true,
        ContentVersionState state = ContentVersionState.Published,
        bool isDeleted = false,
        int version = 0,
        ContentNode? node = null,
        int order = 0)
    {
        node ??= Node(Guid.NewGuid(), isDeleted);
        var versionId = Guid.NewGuid();
        return new FormComponentRegistrationDTO
        {
            VersionId = versionId,
            ComponentName = componentName,
            DisplayName = displayName,
            Category = category,
            IsActive = isActive,
            Order = order,
            Version = new ContentVersion
            {
                Id = versionId,
                NodeId = node.Id,
                Node = node,
                VersionNumber = version,
                State = state,
                Title = displayName,
                Slug = componentName.ToLowerInvariant(),
                CreatedUtc = DateTime.UtcNow
            }
        };
    }

    private async Task SeedAsync(params FormComponentRegistrationDTO[] components)
    {
        await using var ctx = NewContext();
        ctx.Set<FormComponentRegistrationDTO>().AddRange(components);
        await ctx.SaveChangesAsync();
    }

    [Test]
    public async Task GetActiveAsync_ReturnsOnlyActivePublishedNonDeleted()
    {
        await SeedAsync(
            Component("A", isActive: true),
            Component("B", isActive: false),
            Component("C", isActive: true, state: ContentVersionState.Draft),
            Component("D", isActive: true, isDeleted: true));

        var service = NewService();
        var result = await service.GetActiveAsync();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].ComponentName, Is.EqualTo("A"));
    }

    [Test]
    public async Task GetActiveAsync_ReturnsPublishedVersionOnly()
    {
        var node = Node(Guid.NewGuid());
        await SeedAsync(
            Component("A", version: 0, node: node, state: ContentVersionState.Archived),
            Component("A", version: 1, node: node, state: ContentVersionState.Published));

        var service = NewService();
        var result = await service.GetActiveAsync();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Version.VersionNumber, Is.EqualTo(1));
    }

    [Test]
    public async Task GetActiveAsync_SortedByCategoryThenOrderThenDisplayName()
    {
        await SeedAsync(
            Component("C", "CCC", "Layout"),
            Component("A", "AAA", "Content"),
            Component("B", "BBB", "Content"));

        var service = NewService();
        var result = await service.GetActiveAsync();

        Assert.That(result.Select(c => c.DisplayName),
            Is.EqualTo(new[] { "AAA", "BBB", "CCC" }));
    }

    [Test]
    public async Task GetActiveAsync_SortedByOrderWithinCategory()
    {
        await SeedAsync(
            Component("A", "AAA", "Content", order: 2),
            Component("B", "BBB", "Content", order: 1));

        var service = NewService();
        var result = await service.GetActiveAsync();

        Assert.That(result.Select(c => c.DisplayName),
            Is.EqualTo(new[] { "BBB", "AAA" }));
    }

    [Test]
    public async Task GetByComponentNameAsync_ReturnsComponent()
    {
        await SeedAsync(Component("TextBox", "Text Box"));

        var service = NewService();
        var result = await service.GetByComponentNameAsync("TextBox");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.DisplayName, Is.EqualTo("Text Box"));
    }

    [Test]
    public async Task GetByComponentNameAsync_NotFound_ReturnsNull()
    {
        await SeedAsync(Component("A"));

        var service = NewService();
        var result = await service.GetByComponentNameAsync("B");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetByComponentNameAsync_IgnoresDeleted()
    {
        await SeedAsync(Component("A", isDeleted: true));

        var service = NewService();
        var result = await service.GetByComponentNameAsync("A");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetByComponentNameAsync_ReturnsLatestVersion()
    {
        var node = Node(Guid.NewGuid());
        await SeedAsync(
            Component("A", version: 0, node: node),
            Component("A", version: 1, node: node));

        var service = NewService();
        var result = await service.GetByComponentNameAsync("A");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Version.VersionNumber, Is.EqualTo(1));
    }

    [Test]
    public async Task GetActiveByCategoryAsync_GroupsByCategory()
    {
        await SeedAsync(
            Component("A", "A", "Content"),
            Component("B", "B", "Content"),
            Component("C", "C", "Layout"));

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
    public async Task GetActiveByCategoryAsync_EmptyWhenNoComponents()
    {
        var service = NewService();
        var result = await service.GetActiveByCategoryAsync();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Constructor_NullContext_Throws()
    {
        Assert.That(() => new FormComponentRegistrationService(null!),
            Throws.ArgumentNullException);
    }
}
