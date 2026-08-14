using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class WidgetRegistrationServiceTests
{
    private string _db = null!;

    [SetUp]
    public void SetUp() => _db = TestContexts.NewDb();

    private CmsDbContext NewContext() => TestContexts.Cms(_db);

    private WidgetRegistrationService NewService() => new(NewContext());

    private static ContentNode Node(Guid id, bool isDeleted = false)
        => new() { Id = id, ContentTypeKey = "widgets", IsDeleted = isDeleted };

    private static WidgetRegistrationDTO Widget(
        string componentName = "TestWidget",
        string displayName = "Test Widget",
        string category = "General",
        bool isActive = true,
        ContentVersionState state = ContentVersionState.Published,
        bool isDeleted = false,
        int version = 0,
        ContentNode? node = null)
    {
        node ??= Node(Guid.NewGuid(), isDeleted);
        var versionId = Guid.NewGuid();
        return new WidgetRegistrationDTO
        {
            VersionId = versionId,
            ComponentName = componentName,
            DisplayName = displayName,
            Category = category,
            IsActive = isActive,
            PropertyDefinitionsJson = "[]",
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

    private async Task SeedAsync(params WidgetRegistrationDTO[] widgets)
    {
        await using var ctx = NewContext();
        ctx.Set<WidgetRegistrationDTO>().AddRange(widgets);
        await ctx.SaveChangesAsync();
    }

    [Test]
    public async Task GetActiveAsync_ReturnsOnlyActivePublishedNonDeleted()
    {
        await SeedAsync(
            Widget("A", isActive: true),
            Widget("B", isActive: false),
            Widget("C", isActive: true, state: ContentVersionState.Draft),
            Widget("D", isActive: true, isDeleted: true));

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
            Widget("A", version: 0, node: node, state: ContentVersionState.Archived),
            Widget("A", version: 1, node: node, state: ContentVersionState.Published));

        var service = NewService();
        var result = await service.GetActiveAsync();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Version.VersionNumber, Is.EqualTo(1));
    }

    [Test]
    public async Task GetActiveAsync_SortedByCategoryThenOrderThenDisplayName()
    {
        await SeedAsync(
            Widget("C", "CCC", "Layout"),
            Widget("A", "AAA", "Content"),
            Widget("B", "BBB", "Content"));

        var service = NewService();
        var result = await service.GetActiveAsync();

        Assert.That(result.Select(w => w.DisplayName),
            Is.EqualTo(new[] { "AAA", "BBB", "CCC" }));
    }

    [Test]
    public async Task GetByComponentNameAsync_ReturnsWidget()
    {
        await SeedAsync(Widget("ContentBlock", "Content Block"));

        var service = NewService();
        var result = await service.GetByComponentNameAsync("ContentBlock");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.DisplayName, Is.EqualTo("Content Block"));
    }

    [Test]
    public async Task GetByComponentNameAsync_NotFound_ReturnsNull()
    {
        await SeedAsync(Widget("A"));

        var service = NewService();
        var result = await service.GetByComponentNameAsync("B");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetByComponentNameAsync_IgnoresDeleted()
    {
        await SeedAsync(Widget("A", isDeleted: true));

        var service = NewService();
        var result = await service.GetByComponentNameAsync("A");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetActiveByCategoryAsync_GroupsByCategory()
    {
        await SeedAsync(
            Widget("A", "A", "Content"),
            Widget("B", "B", "Content"),
            Widget("C", "C", "Layout"));

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
    public async Task GetActiveByCategoryAsync_EmptyWhenNoWidgets()
    {
        var service = NewService();
        var result = await service.GetActiveByCategoryAsync();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Constructor_NullContext_Throws()
    {
        Assert.That(() => new WidgetRegistrationService(null!),
            Throws.ArgumentNullException);
    }
}
