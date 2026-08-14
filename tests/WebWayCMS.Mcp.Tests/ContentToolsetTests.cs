using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using ModelContextProtocol;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Models.Shared;

namespace WebWayCMS.Mcp.Tests;

[TestFixture]
public class ContentToolsetTests
{
    private IAdminHandlerRegistry _registry = null!;
    private IAdminCrudHandler _handler = null!;
    private ContentToolset _tools = null!;

    private static JsonElement Json(string raw) => JsonSerializer.Deserialize<JsonElement>(raw);

    private static T Prop<T>(object o, string name) => (T)o.GetType().GetProperty(name)!.GetValue(o)!;

    [SetUp]
    public void SetUp()
    {
        _registry = Substitute.For<IAdminHandlerRegistry>();
        _handler = Substitute.For<IAdminCrudHandler>();
        _handler.ContentType.Returns("contentblocks");
        _handler.DisplayName.Returns("Content Block");
        _registry.GetHandler(Arg.Any<string>()).Returns((IAdminCrudHandler?)null);
        _registry.GetHandler("contentblocks").Returns(_handler);
        _tools = new ContentToolset(_registry, new[] { _handler });
    }

    [Test]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new ContentToolset(null!, new[] { _handler }), Throws.ArgumentNullException);
            Assert.That(() => new ContentToolset(_registry, null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void ListContentTypes_ProjectsHandlerMetadata()
    {
        var child = Substitute.For<IAdminCrudChildHandler>();
        child.ChildType.Returns("articles");
        _handler.ChildHandler.Returns(child);
        _handler.RegistryHandler.Returns(Substitute.For<IAdminRegistryHandler>());
        _handler.SupportsVersionHistory.Returns(true);
        _handler.SupportsPublishing.Returns(true);

        var info = _tools.ListContentTypes().Single();

        Assert.Multiple(() =>
        {
            Assert.That(info.ContentType, Is.EqualTo("contentblocks"));
            Assert.That(info.DisplayName, Is.EqualTo("Content Block"));
            Assert.That(info.SupportsVersionHistory, Is.True);
            Assert.That(info.SupportsPublishing, Is.True);
            Assert.That(info.HasChildren, Is.True);
            Assert.That(info.ChildType, Is.EqualTo("articles"));
            Assert.That(info.HasRegistry, Is.True);
        });
    }

    [Test]
    public void ListContentTypes_WithoutChildOrRegistry_FlagsAreFalse()
    {
        _handler.ChildHandler.Returns((IAdminCrudChildHandler?)null);
        _handler.RegistryHandler.Returns((IAdminRegistryHandler?)null);

        var info = _tools.ListContentTypes().Single();

        Assert.Multiple(() =>
        {
            Assert.That(info.SupportsVersionHistory, Is.False);
            Assert.That(info.SupportsPublishing, Is.False);
            Assert.That(info.HasChildren, Is.False);
            Assert.That(info.ChildType, Is.Null);
            Assert.That(info.HasRegistry, Is.False);
        });
    }

    [Test]
    public void DescribeContentType_ReturnsFieldContract()
    {
        _handler.CreateEmptyUpsertViewModel().Returns(new FakeUpsertViewModel());

        var description = _tools.DescribeContentType("contentblocks");

        Assert.Multiple(() =>
        {
            Assert.That(description.ContentType, Is.EqualTo("contentblocks"));
            Assert.That(description.DisplayName, Is.EqualTo("Content Block"));
            Assert.That(description.Fields.Select(f => f.Name), Does.Contain("title"));
        });
    }

    [Test]
    public void DescribeContentType_UnknownType_Throws() =>
        Assert.That(() => _tools.DescribeContentType("nope"), Throws.TypeOf<McpException>());

    [Test]
    public async Task ListContent_ReturnsHandlerApiList()
    {
        var items = new object[] { new { id = Guid.NewGuid(), title = "x" } };
        _handler.GetApiListAsync(Arg.Any<CancellationToken>()).Returns(items);

        Assert.That(await _tools.ListContent("contentblocks"), Is.EqualTo(items));
    }

    [Test]
    public async Task GetContent_WhenFound_ReturnsViewModel()
    {
        var vm = new FakeUpsertViewModel { Title = "hi" };
        _handler.GetUpsertViewModelAsync(Arg.Any<Guid?>(), Arg.Any<IQueryCollection>(), Arg.Any<CancellationToken>())
            .Returns(vm);

        Assert.That(await _tools.GetContent("contentblocks", Guid.NewGuid()), Is.SameAs(vm));
    }

    [Test]
    public void GetContent_WhenMissing_Throws()
    {
        _handler.GetUpsertViewModelAsync(Arg.Any<Guid?>(), Arg.Any<IQueryCollection>(), Arg.Any<CancellationToken>())
            .Returns((object?)null);

        Assert.That(async () => await _tools.GetContent("contentblocks", Guid.NewGuid()),
            Throws.TypeOf<McpException>());
    }

    [Test]
    public async Task CreateContent_MergesFieldsAndSaves()
    {
        _handler.CreateEmptyUpsertViewModel().Returns(new FakeUpsertViewModel());
        FakeUpsertViewModel? saved = null;
        _handler.SaveUpsertAsync(Arg.Do<object>(o => saved = (FakeUpsertViewModel)o), Arg.Any<CancellationToken>())
            .Returns(new AdminSaveResult(true));

        var result = await _tools.CreateContent("contentblocks", Json("{\"title\":\"New\"}"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(saved!.Title, Is.EqualTo("New"));
        });
    }

    [Test]
    public async Task UpdateContent_WhenFound_MergesOntoExistingAndSaves()
    {
        _handler.GetUpsertViewModelAsync(Arg.Any<Guid?>(), Arg.Any<IQueryCollection>(), Arg.Any<CancellationToken>())
            .Returns(new FakeUpsertViewModel { Title = "old", Body = "keep" });
        FakeUpsertViewModel? saved = null;
        _handler.SaveUpsertAsync(Arg.Do<object>(o => saved = (FakeUpsertViewModel)o), Arg.Any<CancellationToken>())
            .Returns(new AdminSaveResult(true));

        await _tools.UpdateContent("contentblocks", Guid.NewGuid(), Json("{\"title\":\"changed\"}"));

        Assert.Multiple(() =>
        {
            Assert.That(saved!.Title, Is.EqualTo("changed"));
            Assert.That(saved.Body, Is.EqualTo("keep"));
        });
    }

    [Test]
    public void UpdateContent_WhenMissing_Throws()
    {
        _handler.GetUpsertViewModelAsync(Arg.Any<Guid?>(), Arg.Any<IQueryCollection>(), Arg.Any<CancellationToken>())
            .Returns((object?)null);

        Assert.That(async () => await _tools.UpdateContent("contentblocks", Guid.NewGuid(), Json("{}")),
            Throws.TypeOf<McpException>());
    }

    [Test]
    public async Task DeleteContent_ReturnsHandlerResult()
    {
        _handler.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        Assert.That((await _tools.DeleteContent("contentblocks", Guid.NewGuid())).Deleted, Is.True);
    }

    [Test]
    public async Task PublishContent_WhenSupported_ReturnsResult()
    {
        _handler.SupportsPublishing.Returns(true);
        _handler.PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new AdminSaveResult(true));

        Assert.That((await _tools.PublishContent("contentblocks", Guid.NewGuid())).Success, Is.True);
    }

    [Test]
    public void PublishContent_WhenUnsupported_Throws()
    {
        _handler.SupportsPublishing.Returns(false);

        Assert.That(async () => await _tools.PublishContent("contentblocks", Guid.NewGuid()),
            Throws.TypeOf<McpException>());
    }

    [Test]
    public async Task UnpublishContent_WhenSupported_ReturnsResult()
    {
        _handler.SupportsPublishing.Returns(true);
        _handler.UnpublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new AdminSaveResult(true));

        Assert.That((await _tools.UnpublishContent("contentblocks", Guid.NewGuid())).Success, Is.True);
    }

    [Test]
    public void UnpublishContent_WhenUnsupported_Throws()
    {
        _handler.SupportsPublishing.Returns(false);

        Assert.That(async () => await _tools.UnpublishContent("contentblocks", Guid.NewGuid()),
            Throws.TypeOf<McpException>());
    }

    [Test]
    public void GetContentState_WhenMissing_Throws()
    {
        _handler.GetVersionHistoryViewModelAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((VersionHistoryViewModel?)null);

        Assert.That(async () => await _tools.GetContentState("contentblocks", Guid.NewGuid()),
            Throws.TypeOf<McpException>());
    }

    [Test]
    public async Task GetContentState_ReturnsDraftAndPublishedSummary()
    {
        var nodeId = Guid.NewGuid();
        _handler.GetVersionHistoryViewModelAsync(nodeId, Arg.Any<CancellationToken>()).Returns(
            new VersionHistoryViewModel
            {
                Versions = new List<VersionItemViewModel>
                {
                    new VersionItemViewModel { State = ContentVersionState.Published, IsPublished = true, IsLatest = false, Version = 3 },
                    new VersionItemViewModel { State = ContentVersionState.Draft, IsPublished = false, IsLatest = true, Version = 4 }
                }
            },
            new VersionHistoryViewModel
            {
                Versions = new List<VersionItemViewModel>
                {
                    new VersionItemViewModel { State = ContentVersionState.Draft, IsPublished = false, IsLatest = false, Version = 2 }
                }
            });

        var published = await _tools.GetContentState("contentblocks", nodeId);
        Assert.Multiple(() =>
        {
            Assert.That(Prop<bool>(published, "isPublished"), Is.True);
            Assert.That(Prop<bool>(published, "hasDraft"), Is.True);
            Assert.That(Prop<int?>(published, "currentVersionNumber"), Is.EqualTo(4));
        });

        var draftOnly = await _tools.GetContentState("contentblocks", nodeId);
        Assert.Multiple(() =>
        {
            Assert.That(Prop<bool>(draftOnly, "isPublished"), Is.False);
            Assert.That(Prop<bool>(draftOnly, "hasDraft"), Is.False);
            Assert.That(Prop<int?>(draftOnly, "currentVersionNumber"), Is.Null);
        });
    }

    [Test]
    public void ListRegistry_WhenPresent_ReturnsUnwrappedValue()
    {
        var registryHandler = Substitute.For<IAdminRegistryHandler>();
        registryHandler.GetAll().Returns(new JsonResult(new[] { "controller" }));
        _handler.RegistryHandler.Returns(registryHandler);

        Assert.That(_tools.ListRegistry("contentblocks"), Is.EqualTo(new[] { "controller" }));
    }

    [Test]
    public void ListRegistry_WhenAbsent_Throws()
    {
        _handler.RegistryHandler.Returns((IAdminRegistryHandler?)null);

        Assert.That(() => _tools.ListRegistry("contentblocks"), Throws.TypeOf<McpException>());
    }

    [Test]
    public void GetRegistryProperties_WhenPresent_ReturnsUnwrappedValue()
    {
        var registryHandler = Substitute.For<IAdminRegistryHandler>();
        registryHandler.GetProperties("Home").Returns(new JsonResult("props"));
        _handler.RegistryHandler.Returns(registryHandler);

        Assert.That(_tools.GetRegistryProperties("contentblocks", "Home"), Is.EqualTo("props"));
    }

    [Test]
    public void GetRegistryProperties_WhenAbsent_Throws()
    {
        _handler.RegistryHandler.Returns((IAdminRegistryHandler?)null);

        Assert.That(() => _tools.GetRegistryProperties("contentblocks", "Home"), Throws.TypeOf<McpException>());
    }
}
