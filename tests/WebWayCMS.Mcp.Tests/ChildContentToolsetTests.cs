using System.Text.Json;
using NSubstitute;
using NUnit.Framework;
using ModelContextProtocol;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Models.Shared;

namespace WebWayCMS.Mcp.Tests;

[TestFixture]
public class ChildContentToolsetTests
{
    private IAdminHandlerRegistry _registry = null!;
    private IAdminCrudHandler _handler = null!;
    private IAdminCrudChildHandler _child = null!;
    private ChildContentToolset _tools = null!;

    private static JsonElement Json(string raw) => JsonSerializer.Deserialize<JsonElement>(raw);

    [SetUp]
    public void SetUp()
    {
        _registry = Substitute.For<IAdminHandlerRegistry>();
        _handler = Substitute.For<IAdminCrudHandler>();
        _child = Substitute.For<IAdminCrudChildHandler>();
        _handler.ContentType.Returns("articles");
        _child.ChildType.Returns("items");
        _child.SupportsReorder.Returns(true);
        _child.SupportsVersionHistory.Returns(true);
        _handler.ChildHandler.Returns(_child);
        _registry.GetHandler(Arg.Any<string>()).Returns((IAdminCrudHandler?)null);
        _registry.GetHandler("articles").Returns(_handler);
        _tools = new ChildContentToolset(_registry);
    }

    [Test]
    public void Constructor_NullRegistry_Throws() =>
        Assert.That(() => new ChildContentToolset(null!), Throws.ArgumentNullException);

    [Test]
    public void Resolve_WhenContentTypeUnknown_Throws() =>
        Assert.That(async () => await _tools.ListChildren("nope", "items", "parent"),
            Throws.TypeOf<McpException>());

    [Test]
    public async Task ListChildren_WhenFound_ReturnsViewModel()
    {
        var vm = new object();
        _child.GetChildIndexViewModelAsync("parent", Arg.Any<CancellationToken>()).Returns(vm);

        Assert.That(await _tools.ListChildren("articles", "items", "parent"), Is.SameAs(vm));
    }

    [Test]
    public void ListChildren_WhenParentMissing_Throws()
    {
        _child.GetChildIndexViewModelAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((object?)null);

        Assert.That(async () => await _tools.ListChildren("articles", "items", "parent"),
            Throws.TypeOf<McpException>());
    }

    [Test]
    public async Task GetChild_WhenFound_ReturnsViewModel()
    {
        var vm = new FakeUpsertViewModel();
        _child.GetChildUpsertViewModelAsync("parent", Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(vm);

        Assert.That(await _tools.GetChild("articles", "items", "parent", Guid.NewGuid()), Is.SameAs(vm));
    }

    [Test]
    public void GetChild_WhenMissing_Throws()
    {
        _child.GetChildUpsertViewModelAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((object?)null);

        Assert.That(async () => await _tools.GetChild("articles", "items", "parent", Guid.NewGuid()),
            Throws.TypeOf<McpException>());
    }

    [Test]
    public async Task CreateChild_MergesFieldsAndSaves()
    {
        _child.CreateEmptyChildUpsertViewModel().Returns(new FakeUpsertViewModel());
        FakeUpsertViewModel? saved = null;
        _child.SaveChildUpsertAsync("parent", Arg.Do<object>(o => saved = (FakeUpsertViewModel)o), Arg.Any<CancellationToken>())
            .Returns(new AdminSaveResult(true));

        var result = await _tools.CreateChild("articles", "items", "parent", Json("{\"title\":\"New\"}"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(saved!.Title, Is.EqualTo("New"));
        });
    }

    [Test]
    public async Task UpdateChild_WhenFound_MergesOntoExistingAndSaves()
    {
        _child.GetChildUpsertViewModelAsync("parent", Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new FakeUpsertViewModel { Title = "old", Body = "keep" });
        FakeUpsertViewModel? saved = null;
        _child.SaveChildUpsertAsync("parent", Arg.Do<object>(o => saved = (FakeUpsertViewModel)o), Arg.Any<CancellationToken>())
            .Returns(new AdminSaveResult(true));

        await _tools.UpdateChild("articles", "items", "parent", Guid.NewGuid(), Json("{\"title\":\"changed\"}"));

        Assert.Multiple(() =>
        {
            Assert.That(saved!.Title, Is.EqualTo("changed"));
            Assert.That(saved.Body, Is.EqualTo("keep"));
        });
    }

    [Test]
    public void UpdateChild_WhenMissing_Throws()
    {
        _child.GetChildUpsertViewModelAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((object?)null);

        Assert.That(async () => await _tools.UpdateChild("articles", "items", "parent", Guid.NewGuid(), Json("{}")),
            Throws.TypeOf<McpException>());
    }

    [Test]
    public async Task DeleteChild_ReturnsHandlerResult()
    {
        _child.DeleteChildAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        Assert.That((await _tools.DeleteChild("articles", "items", Guid.NewGuid())).Deleted, Is.True);
    }

    [Test]
    public async Task ReorderChildren_WhenSupported_ReturnsResult()
    {
        var ids = new List<Guid> { Guid.NewGuid() };
        _child.ReorderAsync("parent", ids, Arg.Any<CancellationToken>()).Returns(true);

        Assert.That((await _tools.ReorderChildren("articles", "items", "parent", ids)).Reordered, Is.True);
    }

    [Test]
    public void ReorderChildren_WhenUnsupported_Throws()
    {
        _child.SupportsReorder.Returns(false);

        Assert.That(async () => await _tools.ReorderChildren("articles", "items", "parent", new List<Guid>()),
            Throws.TypeOf<McpException>());
    }

    [Test]
    public void ChildVersionTools_WhenUnsupported_Throw()
    {
        _child.SupportsVersionHistory.Returns(false);

        Assert.That(async () => await _tools.ListChildVersions("articles", "items", "parent", Guid.NewGuid()),
            Throws.TypeOf<McpException>());
    }

    [Test]
    public async Task ListChildVersions_WhenFound_ReturnsHistory()
    {
        var vm = new VersionHistoryViewModel { MasterId = Guid.NewGuid() };
        _child.GetChildVersionHistoryViewModelAsync("parent", Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(vm);

        Assert.That(await _tools.ListChildVersions("articles", "items", "parent", vm.MasterId), Is.SameAs(vm));
    }

    [Test]
    public void ListChildVersions_WhenMissing_Throws()
    {
        _child.GetChildVersionHistoryViewModelAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((VersionHistoryViewModel?)null);

        Assert.That(async () => await _tools.ListChildVersions("articles", "items", "parent", Guid.NewGuid()),
            Throws.TypeOf<McpException>());
    }

    [Test]
    public async Task RestoreChildVersion_WhenFound_SavesRestoredModel()
    {
        var vm = new FakeUpsertViewModel { Title = "restored" };
        _child.GetChildRestoreVersionViewModelAsync("parent", Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(vm);
        _child.SaveChildUpsertAsync("parent", vm, Arg.Any<CancellationToken>()).Returns(new AdminSaveResult(true));

        var result = await _tools.RestoreChildVersion("articles", "items", "parent", Guid.NewGuid());

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public void RestoreChildVersion_WhenMissing_Throws()
    {
        _child.GetChildRestoreVersionViewModelAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((object?)null);

        Assert.That(async () => await _tools.RestoreChildVersion("articles", "items", "parent", Guid.NewGuid()),
            Throws.TypeOf<McpException>());
    }

    [Test]
    public async Task DeleteChildVersion_ReturnsHandlerResult()
    {
        _child.DeleteChildVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        Assert.That((await _tools.DeleteChildVersion("articles", "items", Guid.NewGuid())).Deleted, Is.True);
    }
}
