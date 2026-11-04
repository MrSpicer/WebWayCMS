using Microsoft.AspNetCore.Mvc;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.ContentZones;
using WebWayCMS.Controllers.Admin;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class AdminContentControllerPublishRedirectTests
{
    private MvcHarness _harness = null!;
    private IAdminCrudHandler _handler = null!;
    private AdminContentController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _harness = new MvcHarness();
        var registry = Substitute.For<IAdminHandlerRegistry>();
        _handler = Substitute.For<IAdminCrudHandler>();
        _handler.SupportsPublishing.Returns(true);
        _handler.PublishRoles.Returns((string[]?)null);
        _handler.PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new AdminSaveResult(true));
        _handler.UnpublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new AdminSaveResult(true));
        registry.GetHandler("pages").Returns(_handler);
        _controller = new AdminContentController(registry, Substitute.For<IWidgetRegistry>(), new ContentRelationRegistry(registry));
        _harness.Configure(_controller, new[] { "Admin" });
        _controller.Url = new Microsoft.AspNetCore.Mvc.Routing.UrlHelper(_controller.ControllerContext);
    }

    [Test]
    public async Task Publish_LocalReturnUrl_RedirectsBackThere()
    {
        var result = await _controller.Publish("pages", Guid.NewGuid(), "/wadmin/contentzones/page/abc", default);

        Assert.That(((LocalRedirectResult)result).Url, Is.EqualTo("/wadmin/contentzones/page/abc"));
    }

    [Test]
    public async Task Unpublish_LocalReturnUrl_RedirectsBackThere()
    {
        var result = await _controller.Unpublish("pages", Guid.NewGuid(), "/wadmin/contentzones/page/abc", default);

        Assert.That(((LocalRedirectResult)result).Url, Is.EqualTo("/wadmin/contentzones/page/abc"));
    }

    [TestCase("https://evil.example/")]
    [TestCase("//evil.example/")]
    [TestCase("")]
    [TestCase(null)]
    public async Task Publish_ForeignOrMissingReturnUrl_RedirectsToIndex(string? returnUrl)
    {
        var publish = await _controller.Publish("pages", Guid.NewGuid(), returnUrl, default);
        var unpublish = await _controller.Unpublish("pages", Guid.NewGuid(), returnUrl, default);

        Assert.Multiple(() =>
        {
            Assert.That(((RedirectToActionResult)publish).ActionName, Is.EqualTo(nameof(AdminContentController.Index)));
            Assert.That(((RedirectToActionResult)unpublish).ActionName, Is.EqualTo(nameof(AdminContentController.Index)));
        });
    }
}
