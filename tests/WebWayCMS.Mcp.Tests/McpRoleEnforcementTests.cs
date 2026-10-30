using System.Text.Json;

using ModelContextProtocol;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Models.PageHistory;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Mcp.Tests;

/// <summary>
/// Every MCP write and publish tool applies its content type's roles to the token's user, the rule
/// <c>AdminContentController</c> applies (a null role list means Admin only).
/// </summary>
[TestFixture]
public class McpRoleEnforcementTests
{
    private static readonly JsonElement Fields = JsonSerializer.Deserialize<JsonElement>("{}");

    private IAdminHandlerRegistry _registry = null!;
    private IAdminCrudHandler _blocks = null!;
    private IAdminCrudHandler _lists = null!;
    private IAdminCrudHandler _items = null!;

    private static IAdminCrudHandler Handler(string contentType, Func<object> emptyModel)
    {
        var handler = Substitute.For<IAdminCrudHandler>();
        handler.ContentType.Returns(contentType);
        handler.SupportsVersionHistory.Returns(true);
        handler.SupportsPublishing.Returns(true);
        handler.CreateEmptyUpsertViewModel().Returns(_ => emptyModel());
        handler.AdminOnly();
        return handler;
    }

    [SetUp]
    public void SetUp()
    {
        _registry = Substitute.For<IAdminHandlerRegistry>();
        _blocks = Handler("contentblocks", () => new FakeUpsertViewModel());
        _lists = Handler("lists", () => new FakeUpsertViewModel());
        _items = Handler("items", () => new ListedItem());
        _registry.GetHandler(Arg.Any<string>()).Returns((IAdminCrudHandler?)null);
        foreach (var handler in new[] { _blocks, _lists, _items })
            _registry.GetHandler(handler.ContentType).Returns(handler);
        _registry.All.Returns([_blocks, _lists, _items]);
    }

    private ContentToolset Content(IMcpCaller caller) => new(_registry, [_blocks], caller);

    private VersionToolset Versions(IMcpCaller caller) => new(_registry, caller);

    private ChildContentToolset Children(IMcpCaller caller) => new(_registry, new ContentRelationRegistry(_registry), caller);

    private static void AssertDenied(Func<Task> call, string expectedFragment) =>
        Assert.That(call, Throws.TypeOf<McpException>().With.Message.Contains(expectedFragment));

    [Test]
    public void ContentWrites_NonAdminOnAnAdminOnlyType_AreRefusedBeforeTheHandlerRuns()
    {
        var tools = Content(TestCaller.Editor);

        Assert.Multiple(() =>
        {
            AssertDenied(() => tools.CreateContent("contentblocks", Fields), "permission to change 'contentblocks'");
            AssertDenied(() => tools.UpdateContent("contentblocks", Guid.NewGuid(), Fields), "permission to change");
            AssertDenied(() => tools.DeleteContent("contentblocks", Guid.NewGuid()), "permission to change");
            AssertDenied(() => tools.PublishContent("contentblocks", Guid.NewGuid()), "permission to publish 'contentblocks'");
            AssertDenied(() => tools.UnpublishContent("contentblocks", Guid.NewGuid()), "permission to publish");
        });
        _blocks.DidNotReceiveWithAnyArgs().SaveUpsertAsync(default!, default);
        _blocks.DidNotReceiveWithAnyArgs().GetUpsertViewModelAsync(default, default!, default);
        _blocks.DidNotReceiveWithAnyArgs().TryDeleteAsync(default, default);
        _blocks.DidNotReceiveWithAnyArgs().PublishAsync(default, default);
        _blocks.DidNotReceiveWithAnyArgs().UnpublishAsync(default, default);
    }

    [Test]
    public void ContentWrites_NoUser_AreRefused()
    {
        var tools = Content(TestCaller.Anonymous);

        AssertDenied(() => tools.CreateContent("contentblocks", Fields), "permission to change");
    }

    [Test]
    public async Task ContentWrites_UserInADeclaredRole_Proceed()
    {
        _blocks.WriteRoles.Returns(["Admin", "Editor"]);
        _blocks.PublishRoles.Returns(["Editor"]);
        var tools = Content(TestCaller.Editor);

        await tools.CreateContent("contentblocks", Fields);
        await tools.PublishContent("contentblocks", Guid.NewGuid());

        await _blocks.ReceivedWithAnyArgs(1).SaveUpsertAsync(default!, default);
        await _blocks.ReceivedWithAnyArgs(1).PublishAsync(default, default);
    }

    [Test]
    public void Publish_UsesPublishRoles_NotWriteRoles()
    {
        _blocks.WriteRoles.Returns(["Editor"]);
        _blocks.PublishRoles.Returns(["Admin"]);
        var tools = Content(TestCaller.Editor);

        AssertDenied(() => tools.PublishContent("contentblocks", Guid.NewGuid()), "permission to publish");
    }

    [Test]
    public void VersionWrites_NonAdmin_AreRefused()
    {
        var tools = Versions(TestCaller.Editor);

        Assert.Multiple(() =>
        {
            AssertDenied(() => tools.RestoreVersion("contentblocks", Guid.NewGuid()), "permission to change");
            AssertDenied(() => tools.DeleteVersion("contentblocks", Guid.NewGuid()), "permission to change");
        });
        _blocks.DidNotReceiveWithAnyArgs().RestoreVersionAsync(default, default);
        _blocks.DidNotReceiveWithAnyArgs().TryDeleteVersionAsync(default, default);
    }

    [Test]
    public void ChildWrites_CheckTheChildTypesRoles()
    {
        // The parent is open to Editors; the child type is Admin-only, and that is what counts.
        _lists.WriteRoles.Returns(["Editor"]);
        var tools = Children(TestCaller.Editor);
        var parent = Guid.NewGuid();

        Assert.Multiple(() =>
        {
            AssertDenied(() => tools.CreateChild("lists", parent, "items", Fields), "permission to change 'items'");
            AssertDenied(() => tools.UpdateChild("items", Guid.NewGuid(), Fields), "permission to change 'items'");
            AssertDenied(() => tools.DeleteChild("items", Guid.NewGuid()), "permission to change 'items'");
            AssertDenied(() => tools.ReorderChildren("lists", parent, "items", [Guid.NewGuid()]), "permission to change 'items'");
            AssertDenied(() => tools.MoveChild("lists", parent, "items", Guid.NewGuid()), "permission to change 'items'");
            AssertDenied(() => tools.RestoreChildVersion("items", Guid.NewGuid()), "permission to change 'items'");
            AssertDenied(() => tools.DeleteChildVersion("items", Guid.NewGuid()), "permission to change 'items'");
        });
        _items.DidNotReceiveWithAnyArgs().SaveUpsertAsync(default!, default);
        _items.DidNotReceiveWithAnyArgs().TryDeleteAsync(default, default);
        _items.DidNotReceiveWithAnyArgs().ReorderRelatedAsync(default!, default, default!, default);
        _items.DidNotReceiveWithAnyArgs().MoveRelatedAsync(default!, default, default, default, default, default);
        _items.DidNotReceiveWithAnyArgs().RestoreVersionAsync(default, default, default);
        _items.DidNotReceiveWithAnyArgs().TryDeleteVersionAsync(default, default);
    }

    [Test]
    public async Task ChildWrites_UserInTheChildTypesRole_Proceed()
    {
        _items.WriteRoles.Returns(["Editor"]);
        _items.ReorderRelatedAsync(default!, default, default!, default).ReturnsForAnyArgs((true, (string?)null));
        var tools = Children(TestCaller.Editor);

        var result = await tools.ReorderChildren("lists", Guid.NewGuid(), "items", [Guid.NewGuid()]);

        Assert.That(result.Reordered, Is.True);
    }

    [Test]
    public void PageHistoryWrites_RequireAdmin()
    {
        var model = Substitute.For<IPageHistoryModel>();
        var tools = new PageHistoryToolset(model, TestCaller.Editor);

        Assert.Multiple(() =>
        {
            AssertDenied(() => tools.RevertPageToEntry(Guid.NewGuid(), Guid.NewGuid(), "token"), "requires the Admin role");
            AssertDenied(() => tools.DeletePageHistoryEntry(Guid.NewGuid(), Guid.NewGuid()), "requires the Admin role");
        });
        model.DidNotReceiveWithAnyArgs().RevertAsync(default, default, default!, default);
        model.DidNotReceiveWithAnyArgs().DeleteAsync(default, default, default);
    }

    [Test]
    public void Constructors_NullCaller_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new ContentToolset(_registry, [_blocks], null!), Throws.ArgumentNullException);
            Assert.That(() => new VersionToolset(_registry, null!), Throws.ArgumentNullException);
            Assert.That(() => new ChildContentToolset(_registry, new ContentRelationRegistry(_registry), null!), Throws.ArgumentNullException);
            Assert.That(() => new PageHistoryToolset(Substitute.For<IPageHistoryModel>(), null!), Throws.ArgumentNullException);
        });
    }
}
