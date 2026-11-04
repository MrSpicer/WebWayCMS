using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class RelationDeleteSurfaceTests
{
    [Test]
    public async Task AdminCrudHandler_TryDeleteAsync_DefaultsToDeleteAsync()
    {
        IAdminCrudHandler handler = new MinimalHandler();

        Assert.That(await handler.TryDeleteAsync(Guid.NewGuid()), Is.EqualTo((true, (string?)null, 0)));
    }

    [Test]
    public async Task AdminCrudModel_RelationBinderRejection_StopsTheSave()
    {
        var binder = Substitute.For<IRelationBinder>();
        binder.ValidateAsync(Arg.Any<object>(), "Minimal Crud", Arg.Any<CancellationToken>())
            .Returns(new AdminSaveResult(false, "Choose the list.", "ListId"));
        var model = new MinimalCrudModel(Substitute.For<IContentStore<ContentBlockDTO>>(), Substitute.For<IChangeSetScope>(),
            binder, TestSupport.PassThroughDeletes());

        var result = await model.SaveUpsertAsync(new Models.ContentBlock.ContentBlockUpsertViewModel { Title = "T", Content = "c" });

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new AdminSaveResult(false, "Choose the list.", "ListId")));
            Assert.That(model.SaveUpsertCoreCalled, Is.False);
        });
    }

    [Test]
    public async Task AdminCrudModel_IsTheDeleterForItsContentType()
    {
        IContentDeleter model = new MinimalCrudModel(Substitute.For<IContentStore<ContentBlockDTO>>(), Substitute.For<IChangeSetScope>(),
            TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes());

        Assert.Multiple(async () =>
        {
            Assert.That(model.ContentTypeKey, Is.EqualTo("minimal-crud"));
            Assert.That(await model.DeleteSelfAsync(Guid.NewGuid()), Is.True);
        });
    }

    [Test]
    public async Task AdminContentController_Delete_ReportsTheRefusalOrTheCascade()
    {
        var handler = Substitute.For<IAdminCrudHandler>();
        handler.ContentType.Returns("ct");
        handler.WriteRoles.Returns((string[]?)null);
        handler.TryDeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((false, (string?)"'Logo' is used by 1 item(s): 'Home'.", 0), (false, (string?)null, 0), (true, (string?)null, 2), (true, (string?)null, 0));
        var registry = Substitute.For<IAdminHandlerRegistry>();
        registry.GetHandler("ct").Returns(handler);
        registry.All.Returns([handler]);
        var controller = new Controllers.Admin.AdminContentController(registry, Substitute.For<ContentZones.IWidgetRegistry>(), new ContentRelationRegistry(registry));
        new MvcHarness().Configure(controller, ["Admin"]);

        await controller.Delete("ct", Guid.NewGuid(), null, default);
        var refused = controller.TempData["Error"];
        controller.TempData.Clear();
        await controller.Delete("ct", Guid.NewGuid(), null, default);
        var failed = controller.TempData["Error"];
        controller.TempData.Clear();
        await controller.Delete("ct", Guid.NewGuid(), null, default);
        var cascaded = controller.TempData["Success"];
        controller.TempData.Clear();
        var plain = await controller.Delete("ct", Guid.NewGuid(), null, default);

        Assert.Multiple(() =>
        {
            Assert.That(refused, Is.EqualTo("'Logo' is used by 1 item(s): 'Home'."));
            Assert.That(failed, Is.EqualTo("The item could not be deleted."));
            Assert.That(cascaded, Is.EqualTo("Deleted, together with the 2 item(s) it contained."));
            Assert.That(controller.TempData.Count, Is.Zero);
            Assert.That(plain, Is.InstanceOf<Microsoft.AspNetCore.Mvc.RedirectToActionResult>());
        });
    }
}
