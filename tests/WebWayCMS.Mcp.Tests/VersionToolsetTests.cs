using ModelContextProtocol;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Models.Shared;

namespace WebWayCMS.Mcp.Tests;

[TestFixture]
public class VersionToolsetTests
{
    private IAdminHandlerRegistry _registry = null!;
    private IAdminCrudHandler _handler = null!;
    private VersionToolset _tools = null!;

    [SetUp]
    public void SetUp()
    {
        _registry = Substitute.For<IAdminHandlerRegistry>();
        _handler = Substitute.For<IAdminCrudHandler>();
        _handler.ContentType.Returns("contentblocks");
        _handler.SupportsVersionHistory.Returns(true);
        _registry.GetHandler(Arg.Any<string>()).Returns((IAdminCrudHandler?)null);
        _registry.GetHandler("contentblocks").Returns(_handler);
        _tools = new VersionToolset(_registry);
    }

    [Test]
    public void Constructor_NullRegistry_Throws() =>
        Assert.That(() => new VersionToolset(null!), Throws.ArgumentNullException);

    [Test]
    public void Tools_WhenVersioningUnsupported_Throw()
    {
        _handler.SupportsVersionHistory.Returns(false);

        Assert.That(async () => await _tools.ListVersions("contentblocks", Guid.NewGuid()),
            Throws.TypeOf<McpException>());
    }

    [Test]
    public async Task ListVersions_WhenFound_ReturnsHistory()
    {
        var vm = new VersionHistoryViewModel { NodeId = Guid.NewGuid() };
        _handler.GetVersionHistoryViewModelAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(vm);

        Assert.That(await _tools.ListVersions("contentblocks", vm.NodeId), Is.SameAs(vm));
    }

    [Test]
    public void ListVersions_WhenMissing_Throws()
    {
        _handler.GetVersionHistoryViewModelAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((VersionHistoryViewModel?)null);

        Assert.That(async () => await _tools.ListVersions("contentblocks", Guid.NewGuid()),
            Throws.TypeOf<McpException>());
    }

    [Test]
    public async Task GetVersion_WhenFound_ReturnsViewModel()
    {
        var vm = new FakeUpsertViewModel();
        _handler.GetRestoreVersionViewModelAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(vm);

        Assert.That(await _tools.GetVersion("contentblocks", Guid.NewGuid()), Is.SameAs(vm));
    }

    [Test]
    public void GetVersion_WhenMissing_Throws()
    {
        _handler.GetRestoreVersionViewModelAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((object?)null);

        Assert.That(async () => await _tools.GetVersion("contentblocks", Guid.NewGuid()),
            Throws.TypeOf<McpException>());
    }

    [Test]
    public async Task RestoreVersion_ReturnsHandlerResult()
    {
        var versionId = Guid.NewGuid();
        _handler.RestoreVersionAsync(versionId, Arg.Any<CancellationToken>()).Returns(new AdminSaveResult(true));

        var result = await _tools.RestoreVersion("contentblocks", versionId);

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task DeleteVersion_ReturnsHandlerResult()
    {
        _handler.DeleteVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        Assert.That((await _tools.DeleteVersion("contentblocks", Guid.NewGuid())).Deleted, Is.True);
    }
}
