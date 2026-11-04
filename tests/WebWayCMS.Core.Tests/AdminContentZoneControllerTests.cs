using Microsoft.AspNetCore.Mvc;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.ContentZones;
using WebWayCMS.Controllers.Admin;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.ContentZone;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class AdminContentZoneControllerTests
{
    [Test]
    public void Constructor_NullArguments_Throw()
    {
        var model = Substitute.For<IContentZoneModel>();
        var registry = Substitute.For<IWidgetRegistry>();
        var pageStore = Substitute.For<IContentStore<PageDTO>>();
        var pageHistory = Substitute.For<IPageHistoryService>();

        Assert.Multiple(() =>
        {
            Assert.That(() => new AdminContentZoneController(null!, registry, pageStore, pageHistory), Throws.ArgumentNullException);
            Assert.That(() => new AdminContentZoneController(model, null!, pageStore, pageHistory), Throws.ArgumentNullException);
            Assert.That(() => new AdminContentZoneController(model, registry, null!, pageHistory), Throws.ArgumentNullException);
            Assert.That(() => new AdminContentZoneController(model, registry, pageStore, null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public async Task ZoneEdit_NotFoundAndView()
    {
        var model = Substitute.For<IContentZoneModel>();
        var controller = new AdminContentZoneController(model, Substitute.For<IWidgetRegistry>(),
            Substitute.For<IContentStore<PageDTO>>(), Substitute.For<IPageHistoryService>());
        new MvcHarness().Configure(controller, new[] { "Admin" });

        model.GetViewModelByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ContentZoneViewModel?)null, new ContentZoneViewModel());

        Assert.Multiple(async () =>
        {
            Assert.That(await controller.ZoneEdit(Guid.NewGuid(), default), Is.InstanceOf<NotFoundResult>());
            Assert.That(await controller.ZoneEdit(Guid.NewGuid(), default), Is.InstanceOf<ViewResult>());
        });
    }

    [Test]
    public async Task ZoneEdit_MarksRequestAsUncachedDraftRead()
    {
        var model = Substitute.For<IContentZoneModel>();
        var controller = new AdminContentZoneController(model, Substitute.For<IWidgetRegistry>(),
            Substitute.For<IContentStore<PageDTO>>(), Substitute.For<IPageHistoryService>());
        new MvcHarness().Configure(controller, new[] { "Admin" });
        model.GetViewModelByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new ContentZoneViewModel());

        await controller.ZoneEdit(Guid.NewGuid(), default);

        Assert.Multiple(() =>
        {
            Assert.That(controller.HttpContext.Items[WebWayCMS.PreviewConstants.DraftRequestItemKey], Is.EqualTo(true));
            Assert.That(controller.Response.Headers.CacheControl.ToString(), Is.EqualTo(WebWayCMS.PreviewConstants.DraftCacheControl));
        });
    }
}
