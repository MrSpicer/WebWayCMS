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

    private static FormComponentRegistrationDTO Component(
        string componentName = "TestComponent",
        string displayName = "Test Component",
        string category = "General",
        bool isActive = true,
        int order = 0)
        => new()
        {
            ComponentName = componentName,
            ViewComponentName = $"VC-{componentName}",
            DisplayName = displayName,
            Category = category,
            IsActive = isActive,
            Order = order
        };

    private async Task SeedAsync(params FormComponentRegistrationDTO[] components)
    {
        await using var ctx = NewContext();
        ctx.Set<FormComponentRegistrationDTO>().AddRange(components);
        await ctx.SaveChangesAsync();
    }

    [Test]
    public async Task GetActiveAsync_ReturnsOnlyActive()
    {
        await SeedAsync(
            Component("A", isActive: true),
            Component("B", isActive: false),
            Component("C", isActive: true));

        var service = NewService();
        var result = await service.GetActiveAsync();

        Assert.That(result.Select(c => c.ComponentName), Is.EqualTo(new[] { "A", "C" }));
    }

    [Test]
    public async Task GetActiveAsync_EmptyWhenNone()
    {
        var service = NewService();
        var result = await service.GetActiveAsync();

        Assert.That(result, Is.Empty);
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
    public async Task GetAllAsync_ReturnsAllIncludingInactive()
    {
        await SeedAsync(
            Component("A", isActive: true),
            Component("B", isActive: false));

        var service = NewService();
        var result = await service.GetAllAsync();

        Assert.That(result.Select(c => c.ComponentName), Is.EqualTo(new[] { "A", "B" }));
    }

    [Test]
    public async Task GetAllAsync_EmptyWhenNone()
    {
        var service = NewService();
        var result = await service.GetAllAsync();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetByIdAsync_ReturnsDto()
    {
        var dto = Component("TextBox", "Text Box");
        dto.Id = Guid.NewGuid();
        await SeedAsync(dto);

        var service = NewService();
        var result = await service.GetByIdAsync(dto.Id);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.DisplayName, Is.EqualTo("Text Box"));
    }

    [Test]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        await SeedAsync(Component("A"));

        var service = NewService();
        var result = await service.GetByIdAsync(Guid.NewGuid());

        Assert.That(result, Is.Null);
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
    public async Task UpsertAsync_Create_AssignsIdAndSaves()
    {
        var service = NewService();
        var dto = Component("TextBox", "Text Box");

        var result = await service.UpsertAsync(dto);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Registration!.Id, Is.Not.EqualTo(Guid.Empty));
        });

        var saved = await service.GetByComponentNameAsync("TextBox");
        Assert.That(saved, Is.Not.Null);
        Assert.That(saved!.Id, Is.EqualTo(result.Registration!.Id));
    }

    [Test]
    public async Task UpsertAsync_Update_UpdatesExistingById()
    {
        var existing = Component("TextBox", "Old Name");
        existing.Id = Guid.NewGuid();
        await SeedAsync(existing);

        var service = NewService();
        var update = Component("TextBox", "New Name");
        update.Id = existing.Id;

        var result = await service.UpsertAsync(update);

        Assert.That(result.Success, Is.True);

        var saved = await service.GetByIdAsync(existing.Id);
        Assert.That(saved!.DisplayName, Is.EqualTo("New Name"));
    }

    [Test]
    public async Task UpsertAsync_Update_NotFound_ReturnsError()
    {
        var service = NewService();
        var dto = Component("TextBox");
        dto.Id = Guid.NewGuid();

        var result = await service.UpsertAsync(dto);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("Form component registration not found."));
        });
    }

    [Test]
    public async Task UpsertAsync_DuplicateName_ReturnsError()
    {
        await SeedAsync(Component("TextBox"));

        var service = NewService();
        var dto = Component("TextBox", "Other");

        var result = await service.UpsertAsync(dto);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("A form component with this name already exists."));
        });
    }

    [Test]
    public async Task UpsertAsync_DuplicateName_SameId_IsAllowed()
    {
        var existing = Component("TextBox", "Original");
        existing.Id = Guid.NewGuid();
        await SeedAsync(existing);

        var service = NewService();
        var update = Component("TextBox", "Renamed");
        update.Id = existing.Id;

        var result = await service.UpsertAsync(update);

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public void UpsertAsync_NullRegistration_Throws()
    {
        var service = NewService();

        Assert.That(
            () => service.UpsertAsync(null!),
            Throws.ArgumentNullException);
    }

    [Test]
    public async Task DeleteAsync_Existing_ReturnsTrue()
    {
        var existing = Component("TextBox");
        existing.Id = Guid.NewGuid();
        await SeedAsync(existing);

        var service = NewService();
        var result = await service.DeleteAsync(existing.Id);

        Assert.That(result, Is.True);
        Assert.That(await service.GetByIdAsync(existing.Id), Is.Null);
    }

    [Test]
    public async Task DeleteAsync_NotFound_ReturnsFalse()
    {
        var service = NewService();
        var result = await service.DeleteAsync(Guid.NewGuid());

        Assert.That(result, Is.False);
    }

    [Test]
    public void Constructor_NullContext_Throws()
    {
        Assert.That(() => new FormComponentRegistrationService(null!),
            Throws.ArgumentNullException);
    }
}
