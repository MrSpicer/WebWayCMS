using System.Text.Json;

using Microsoft.AspNetCore.Http;

using ModelContextProtocol;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Models.Shared;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Mcp.Tests;

[TestFixture]
public class ChildContentToolsetTests
{
    private IAdminHandlerRegistry _registry = null!;
    private IAdminCrudHandler _lists = null!;
    private IAdminCrudHandler _items = null!;
    private IAdminCrudHandler _blocks = null!;
    private IAdminCrudHandler _notes = null!;
    private ChildContentToolset _tools = null!;

    private static JsonElement Json(string raw) => JsonSerializer.Deserialize<JsonElement>(raw);

    private static IAdminCrudHandler Handler(string contentType, Func<object> emptyModel)
    {
        var handler = Substitute.For<IAdminCrudHandler>();
        handler.ContentType.Returns(contentType);
        handler.SupportsVersionHistory.Returns(true);
        handler.CreateEmptyUpsertViewModel().Returns(_ => emptyModel());
        return handler;
    }

    [SetUp]
    public void SetUp()
    {
        _registry = Substitute.For<IAdminHandlerRegistry>();
        _lists = Handler("lists", () => new FakeUpsertViewModel());
        _items = Handler("items", () => new ListedItem());
        _blocks = Handler("contentblocks", () => new FakeUpsertViewModel());
        _notes = Handler("notes", () => new ContainedNote());
        _registry.GetHandler(Arg.Any<string>()).Returns((IAdminCrudHandler?)null);
        foreach (var handler in new[] { _lists, _items, _blocks, _notes })
            _registry.GetHandler(handler.ContentType).Returns(handler);
        _registry.All.Returns([_lists, _items, _blocks, _notes]);
        _tools = new ChildContentToolset(_registry, new ContentRelationRegistry(_registry));
    }

    [Test]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new ChildContentToolset(null!, new ContentRelationRegistry(_registry)), Throws.ArgumentNullException);
            Assert.That(() => new ChildContentToolset(_registry, null!), Throws.ArgumentNullException);
        });
    }

    // --- containment resolution ---

    [Test]
    public async Task ListChildren_ResolvesTheOneContainment()
    {
        var parent = Guid.NewGuid();
        var child = new RelatedChild(Guid.NewGuid(), "Item", ContentVersionState.Draft, 0);
        _items.GetRelatedChildrenAsync(nameof(ListedItem.ListId), parent, Arg.Any<CancellationToken>()).Returns([child]);

        Assert.That(await _tools.ListChildren("lists", parent, "items"), Is.EqualTo(new[] { child }));
    }

    [Test]
    public void ResolveContainment_UnknownTypes_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(async () => await _tools.ListChildren("nope", Guid.NewGuid(), "items"), Throws.TypeOf<McpException>());
            Assert.That(async () => await _tools.ListChildren("lists", Guid.NewGuid(), "nope"), Throws.TypeOf<McpException>());
        });
    }

    [Test]
    public void ResolveContainment_NoContainmentBetweenTheTypes_Throws()
        => Assert.That(async () => await _tools.ListChildren("contentblocks", Guid.NewGuid(), "items"),
            Throws.TypeOf<McpException>().With.Message.Contains("not placed under"));

    [Test]
    public void ResolveContainment_Several_RequiresTheRelation()
        => Assert.That(async () => await _tools.ListChildren("contentblocks", Guid.NewGuid(), "notes"),
            Throws.TypeOf<McpException>().With.Message.Contains("BlockId, OtherBlockId"));

    [Test]
    public async Task ResolveContainment_NamedRelation_PicksIt_CaseInsensitively()
    {
        var parent = Guid.NewGuid();

        await _tools.ListChildren("contentblocks", parent, "notes", relation: "otherblockid");

        await _notes.Received(1).GetRelatedChildrenAsync(nameof(ContainedNote.OtherBlockId), parent, Arg.Any<CancellationToken>());
    }

    [Test]
    public void ResolveContainment_UnknownRelation_Throws()
        => Assert.That(async () => await _tools.ListChildren("contentblocks", Guid.NewGuid(), "notes", relation: "Nope"),
            Throws.TypeOf<McpException>().With.Message.Contains("no containment 'Nope'"));

    // --- create / get / update / delete ---

    [Test]
    public async Task CreateChild_MergesFieldsAndSetsTheParent()
    {
        var parent = Guid.NewGuid();
        object? saved = null;
        _items.SaveUpsertAsync(Arg.Do<object>(m => saved = m), Arg.Any<CancellationToken>()).Returns(new AdminSaveResult(true));

        // The parent argument wins over a parent named in the fields.
        var result = await _tools.CreateChild("lists", parent, "items", Json($"{{\"title\":\"T\",\"listId\":\"{Guid.NewGuid()}\"}}"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(((ListedItem)saved!).Title, Is.EqualTo("T"));
            Assert.That(((ListedItem)saved!).ListId, Is.EqualTo(parent));
        });
    }

    [Test]
    public async Task GetChild_WhenFound_ReturnsViewModel_ElseThrows()
    {
        var id = Guid.NewGuid();
        var vm = new ListedItem();
        _items.GetUpsertViewModelAsync(id, Arg.Any<IQueryCollection>(), Arg.Any<CancellationToken>()).Returns(vm);

        Assert.Multiple(async () =>
        {
            Assert.That(await _tools.GetChild("items", id), Is.SameAs(vm));
            Assert.That(async () => await _tools.GetChild("items", Guid.NewGuid()), Throws.TypeOf<McpException>());
        });
    }

    [Test]
    public async Task UpdateChild_MergesOntoExistingAndSaves_ElseThrows()
    {
        var id = Guid.NewGuid();
        _items.GetUpsertViewModelAsync(id, Arg.Any<IQueryCollection>(), Arg.Any<CancellationToken>())
            .Returns(new ListedItem { Title = "Old", ListId = Guid.NewGuid() });
        object? saved = null;
        _items.SaveUpsertAsync(Arg.Do<object>(m => saved = m), Arg.Any<CancellationToken>()).Returns(new AdminSaveResult(true));

        await _tools.UpdateChild("items", id, Json("{\"title\":\"New\"}"));

        Assert.Multiple(() =>
        {
            Assert.That(((ListedItem)saved!).Title, Is.EqualTo("New"));
            Assert.That(async () => await _tools.UpdateChild("items", Guid.NewGuid(), Json("{}")), Throws.TypeOf<McpException>());
        });
    }

    [Test]
    public async Task DeleteChild_ReportsTheOutcome()
    {
        _items.TryDeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((true, (string?)null, 2), (false, (string?)null, 0), (false, (string?)"in use", 0));

        Assert.Multiple(async () =>
        {
            Assert.That(await _tools.DeleteChild("items", Guid.NewGuid()), Is.EqualTo(new McpDeleteResult(true, 2)));
            Assert.That(await _tools.DeleteChild("items", Guid.NewGuid()), Is.EqualTo(new McpDeleteResult(false, 0)));
            Assert.That(async () => await _tools.DeleteChild("items", Guid.NewGuid()),
                Throws.TypeOf<McpException>().With.Message.EqualTo("in use"));
        });
    }

    // --- reorder / move ---

    [Test]
    public async Task ReorderChildren_DelegatesToTheChildType()
    {
        var parent = Guid.NewGuid();
        var order = new List<Guid> { Guid.NewGuid() };
        _items.ReorderRelatedAsync(nameof(ListedItem.ListId), parent, order, Arg.Any<CancellationToken>())
            .Returns((true, (string?)null), (false, (string?)"bad order"), (false, (string?)null));

        Assert.Multiple(async () =>
        {
            Assert.That(await _tools.ReorderChildren("lists", parent, "items", order), Is.EqualTo(new McpReorderResult(true)));
            Assert.That(async () => await _tools.ReorderChildren("lists", parent, "items", order),
                Throws.TypeOf<McpException>().With.Message.EqualTo("bad order"));
            Assert.That(async () => await _tools.ReorderChildren("lists", parent, "items", order),
                Throws.TypeOf<McpException>().With.Message.EqualTo("The reorder failed."));
        });
    }

    [Test]
    public async Task MoveChild_PassesPositionAndExpectedVersion()
    {
        var parent = Guid.NewGuid();
        var child = Guid.NewGuid();
        _items.MoveRelatedAsync(nameof(ListedItem.ListId), child, parent, 2, 5, Arg.Any<CancellationToken>())
            .Returns((true, (string?)null), (false, (string?)"stale"), (false, (string?)null));

        Assert.Multiple(async () =>
        {
            Assert.That(await _tools.MoveChild("lists", parent, "items", child, 2, 5), Is.EqualTo(new McpMoveResult(true)));
            Assert.That(async () => await _tools.MoveChild("lists", parent, "items", child, 2, 5),
                Throws.TypeOf<McpException>().With.Message.EqualTo("stale"));
            Assert.That(async () => await _tools.MoveChild("lists", parent, "items", child, 2, 5),
                Throws.TypeOf<McpException>().With.Message.EqualTo("The move failed."));
        });
    }

    // --- versions ---

    [Test]
    public void VersionTools_ChildTypeWithoutHistory_Throw()
    {
        _items.SupportsVersionHistory.Returns(false);

        Assert.Multiple(() =>
        {
            Assert.That(async () => await _tools.ListChildVersions("items", Guid.NewGuid()), Throws.TypeOf<McpException>());
            Assert.That(async () => await _tools.GetChildVersion("items", Guid.NewGuid()), Throws.TypeOf<McpException>());
            Assert.That(async () => await _tools.RestoreChildVersion("items", Guid.NewGuid()), Throws.TypeOf<McpException>());
            Assert.That(async () => await _tools.DeleteChildVersion("items", Guid.NewGuid()), Throws.TypeOf<McpException>());
        });
    }

    [Test]
    public async Task ListChildVersions_And_GetChildVersion_WhenFound_ElseThrow()
    {
        var nodeId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var history = new VersionHistoryViewModel();
        var version = new ListedItem();
        _items.GetVersionHistoryViewModelAsync(nodeId, Arg.Any<CancellationToken>()).Returns(history);
        _items.GetRestoreVersionViewModelAsync(versionId, Arg.Any<CancellationToken>()).Returns(version);

        Assert.Multiple(async () =>
        {
            Assert.That(await _tools.ListChildVersions("items", nodeId), Is.SameAs(history));
            Assert.That(await _tools.GetChildVersion("items", versionId), Is.SameAs(version));
            Assert.That(async () => await _tools.ListChildVersions("items", Guid.NewGuid()), Throws.TypeOf<McpException>());
            Assert.That(async () => await _tools.GetChildVersion("items", Guid.NewGuid()), Throws.TypeOf<McpException>());
        });
    }

    [Test]
    public async Task RestoreChildVersion_PassesTheExpectedVersion_AndReportsAMissingVersion()
    {
        var versionId = Guid.NewGuid();
        _items.RestoreVersionAsync(versionId, 4, Arg.Any<CancellationToken>())
            .Returns(new AdminSaveResult(true), new AdminSaveResult(false, "stale"), new AdminSaveResult(false, IAdminCrudHandler.VersionNotFoundMessage));

        Assert.Multiple(async () =>
        {
            Assert.That((await _tools.RestoreChildVersion("items", versionId, 4)).Success, Is.True);
            Assert.That((await _tools.RestoreChildVersion("items", versionId, 4)).ErrorMessage, Is.EqualTo("stale"));
            Assert.That(async () => await _tools.RestoreChildVersion("items", versionId, 4),
                Throws.TypeOf<McpException>().With.Message.Contains(versionId.ToString()));
        });
    }

    [Test]
    public async Task DeleteChildVersion_ReportsTheOutcome()
    {
        _items.TryDeleteVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((true, (string?)null), (false, (string?)IAdminCrudHandler.VersionNotDeletedMessage), (false, (string?)"in history"));

        Assert.Multiple(async () =>
        {
            Assert.That(await _tools.DeleteChildVersion("items", Guid.NewGuid()), Is.EqualTo(new McpDeleteResult(true)));
            Assert.That(await _tools.DeleteChildVersion("items", Guid.NewGuid()), Is.EqualTo(new McpDeleteResult(false)));
            Assert.That(async () => await _tools.DeleteChildVersion("items", Guid.NewGuid()),
                Throws.TypeOf<McpException>().With.Message.EqualTo("in history"));
        });
    }
}
