using Microsoft.AspNetCore.Mvc;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.ContentZones;
using WebWayCMS.Controllers.Admin;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Core.Tests;

/// <summary>The edit form's "Save and Publish" (<c>submitAction=publish</c>), offered for live items too.</summary>
[TestFixture]
public class AdminContentControllerSaveAndPublishTests
{
    private sealed class Upsert
    {
        public string? Title { get; set; }
    }

    private MvcHarness _harness = null!;
    private IAdminCrudHandler _handler = null!;
    private AdminContentController _controller = null!;
    private readonly Guid _nodeId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _harness = new MvcHarness();
        var registry = Substitute.For<IAdminHandlerRegistry>();
        _handler = Substitute.For<IAdminCrudHandler>();
        _handler.ContentType.Returns("ct");
        _handler.UpsertViewPath.Returns("~/Upsert.cshtml");
        _handler.CreateEmptyUpsertViewModel().Returns(_ => new Upsert());
        _handler.RegistryHandler.Returns((IAdminRegistryHandler?)null);
        _handler.WriteRoles.Returns(["Admin", "Editor"]);
        _handler.PublishRoles.Returns((string[]?)null);
        _handler.SupportsPublishing.Returns(true);
        _handler.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new AdminSaveResult(true, NodeId: _nodeId));
        _handler.PublishAsync(_nodeId, Arg.Any<CancellationToken>()).Returns(new AdminSaveResult(true));
        registry.GetHandler("ct").Returns(_handler);
        registry.All.Returns([_handler]);

        _controller = new AdminContentController(registry, Substitute.For<IWidgetRegistry>(), new ContentRelationRegistry(registry));
    }

    [Test]
    public async Task EditPost_SaveAndPublishOnExistingItem_SavesThenPublishes()
    {
        _harness.Configure(_controller, new[] { "Admin" });

        var result = await _controller.EditPost("ct", _nodeId, default, submitAction: "publish");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.InstanceOf<RedirectToActionResult>());
            Assert.That(_controller.TempData["Error"], Is.Null);
        });
        Received.InOrder(() =>
        {
            _handler.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
            _handler.PublishAsync(_nodeId, Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task EditPost_SaveAndPublishWithoutPublishRole_KeepsTheDraftAndFlashes()
    {
        _harness.Configure(_controller, new[] { "Editor" });

        var result = await _controller.EditPost("ct", _nodeId, default, submitAction: "publish");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.InstanceOf<RedirectToActionResult>());
            Assert.That(_controller.TempData["Error"], Is.EqualTo(AdminContentController.PublishDeniedMessage));
        });
        await _handler.Received(1).SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
        await _handler.DidNotReceive().PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EditPost_SaveAndPublishFails_FlashesThePublishError()
    {
        _harness.Configure(_controller, new[] { "Admin" });
        _handler.PublishAsync(_nodeId, Arg.Any<CancellationToken>()).Returns(new AdminSaveResult(false, "Route taken."));

        await _controller.EditPost("ct", _nodeId, default, submitAction: "publish");

        Assert.That(_controller.TempData["Error"], Is.EqualTo("Route taken."));
    }
}
