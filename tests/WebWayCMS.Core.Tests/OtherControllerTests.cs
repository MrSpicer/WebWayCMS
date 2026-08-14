using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.ContentZones;
using WebWayCMS.Controllers;
using WebWayCMS.Controllers.Admin;
using WebWayCMS.Controllers.Api;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Services;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class AdminContentZoneControllerTests
{
    [Test]
    public void Constructor_NullArguments_Throw()
    {
        var model = Substitute.For<IContentZoneModel>();
        var registry = Substitute.For<IWidgetRegistry>();

        Assert.Multiple(() =>
        {
            Assert.That(() => new AdminContentZoneController(null!, registry), Throws.ArgumentNullException);
            Assert.That(() => new AdminContentZoneController(model, null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public async Task ZoneEdit_NotFoundAndView()
    {
        var model = Substitute.For<IContentZoneModel>();
        var controller = new AdminContentZoneController(model, Substitute.For<IWidgetRegistry>());
        new MvcHarness().Configure(controller, new[] { "Admin" });

        model.GetViewModelByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ContentZoneViewModel?)null, new ContentZoneViewModel());

        Assert.Multiple(async () =>
        {
            Assert.That(await controller.ZoneEdit(Guid.NewGuid(), default), Is.InstanceOf<NotFoundResult>());
            Assert.That(await controller.ZoneEdit(Guid.NewGuid(), default), Is.InstanceOf<ViewResult>());
        });
    }
}

[TestFixture]
public class ContentZoneApiControllerTests
{
    private IContentZoneService _service = null!;
    private IRouteRegistrationService _routeRegistration = null!;
    private ContentZoneApiController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _service = Substitute.For<IContentZoneService>();
        _routeRegistration = Substitute.For<IRouteRegistrationService>();
        _controller = new ContentZoneApiController(_service, _routeRegistration);
        new MvcHarness().Configure(_controller, new[] { "Admin" });
    }

    [Test]
    public void Constructor_Null_Throws()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new ContentZoneApiController(null!, _routeRegistration), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneApiController(_service, null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public async Task SaveItem_ValidationErrors()
    {
        Assert.Multiple(async () =>
        {
            Assert.That(await _controller.SaveItem(null!, default), Is.InstanceOf<BadRequestObjectResult>());
            Assert.That(await _controller.SaveItem(new SaveItemRequest { ComponentName = "", ZoneName = "Z" }, default), Is.InstanceOf<BadRequestObjectResult>());
            Assert.That(await _controller.SaveItem(new SaveItemRequest { ComponentName = "C", ZoneName = "" }, default), Is.InstanceOf<BadRequestObjectResult>());
        });
    }

    [Test]
    public async Task SaveItem_ByZoneId_CreatesItem()
    {
        var zoneId = Guid.NewGuid();
        _service.AddItemAsync(zoneId, Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>())
            .Returns(c =>
            {
                var item = c.Arg<ContentZoneItemDTO>();
                item.Version.Node = new ContentNode { Id = Guid.NewGuid() };
                return item;
            });
        _service.GetParentPageNodeForZoneAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var result = await _controller.SaveItem(new SaveItemRequest { ComponentName = "C", ZoneName = "Z", ZoneId = zoneId }, default);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public async Task SaveItem_ByPageSlot_UpdatesItem_SuccessAndNotFound()
    {
        var pageNodeId = Guid.NewGuid();
        var zone = new ContentZoneDTO { Version = new ContentVersion { Node = new ContentNode { Id = Guid.NewGuid() } } };
        _service.GetOrCreateByPageSlotAsync(pageNodeId, "Main", Arg.Any<CancellationToken>()).Returns((zone, new ContentZoneAssignmentDTO()));
        _service.UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>()).Returns(true, false);

        var req = new SaveItemRequest { ComponentName = "C", ZoneName = "Z", ParentPageNodeId = pageNodeId, SlotName = "Main", ItemId = Guid.NewGuid() };

        Assert.Multiple(async () =>
        {
            Assert.That(await _controller.SaveItem(req, default), Is.InstanceOf<OkObjectResult>());
            Assert.That(await _controller.SaveItem(req, default), Is.InstanceOf<NotFoundObjectResult>());
        });
    }

    [Test]
    public async Task SaveItem_ByName_ExistingZone()
    {
        _service.GetZoneByNameAsync("Z", Arg.Any<CancellationToken>()).Returns(new ContentZoneDTO
        {
            Version = new ContentVersion { Node = new ContentNode { Id = Guid.NewGuid() } }
        });
        _service.AddItemAsync(Arg.Any<Guid>(), Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>())
            .Returns(c =>
            {
                var item = c.Arg<ContentZoneItemDTO>();
                item.Version.Node = new ContentNode { Id = Guid.NewGuid() };
                return item;
            });
        _service.GetParentPageNodeForZoneAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var result = await _controller.SaveItem(new SaveItemRequest { ComponentName = "C", ZoneName = "Z" }, default);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public async Task SaveItem_ByName_CreatesZone()
    {
        _service.GetZoneByNameAsync("Z", Arg.Any<CancellationToken>()).Returns((ContentZoneDTO?)null);
        _service.GetOrCreateByNameAsync("Z", Arg.Any<CancellationToken>()).Returns(new ContentZoneDTO
        {
            Version = new ContentVersion { Node = new ContentNode { Id = Guid.NewGuid() } }
        });
        _service.AddItemAsync(Arg.Any<Guid>(), Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>())
            .Returns(c =>
            {
                var item = c.Arg<ContentZoneItemDTO>();
                item.Version.Node = new ContentNode { Id = Guid.NewGuid() };
                return item;
            });
        _service.GetParentPageNodeForZoneAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var result = await _controller.SaveItem(new SaveItemRequest { ComponentName = "C", ZoneName = "Z" }, default);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public async Task SaveItem_ExceptionReturns500()
    {
        _service.GetZoneByNameAsync("Z", Arg.Any<CancellationToken>()).Returns<ContentZoneDTO?>(_ => throw new InvalidOperationException("boom"));

        var result = await _controller.SaveItem(new SaveItemRequest { ComponentName = "C", ZoneName = "Z" }, default) as ObjectResult;

        Assert.That(result!.StatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task DeleteItem_Success_NotFound_Exception()
    {
        _service.RemoveItemAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true, false);
        Assert.That(await _controller.DeleteItem(Guid.NewGuid(), default), Is.InstanceOf<OkObjectResult>());
        Assert.That(await _controller.DeleteItem(Guid.NewGuid(), default), Is.InstanceOf<NotFoundObjectResult>());

        _service.RemoveItemAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns<bool>(_ => throw new InvalidOperationException());
        Assert.That(((ObjectResult)await _controller.DeleteItem(Guid.NewGuid(), default)).StatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task GetItem_FoundAndNotFound()
    {
        var nodeId = Guid.NewGuid();
        var item = new ContentZoneItemDTO
        {
            Version = new ContentVersion { Node = new ContentNode { Id = nodeId } },
            ContentZoneNodeId = Guid.NewGuid(),
            ComponentName = "C"
        };
        _service.GetItemByNodeIdAsync(nodeId, Arg.Any<CancellationToken>()).Returns(item);
        _service.GetItemByNodeIdAsync(Arg.Is<Guid>(g => g != nodeId), Arg.Any<CancellationToken>()).Returns((ContentZoneItemDTO?)null);

        Assert.Multiple(async () =>
        {
            Assert.That(await _controller.GetItem(nodeId, default), Is.InstanceOf<OkObjectResult>());
            Assert.That(await _controller.GetItem(Guid.NewGuid(), default), Is.InstanceOf<NotFoundObjectResult>());
        });
    }
}

[TestFixture]
public class GenericPageControllerTests
{
    private static T Build<T>() where T : Controller, new()
    {
        var controller = new T();
        var http = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private static PageDTO Page(string? viewName, string title = "T")
        => new()
        {
            ViewName = viewName,
            Version = new ContentVersion
            {
                Node = new ContentNode { Id = Guid.NewGuid() },
                Title = title
            }
        };

    [Test]
    public async Task GenericPage_Index_DefaultView_CustomView_AndConfigFallback()
    {
        var controller = Build<GenericPageController>();
        // No CMS:PageData / CMS:PageConfig -> default view + new config.
        var def = (ViewResult)await controller.Index();
        Assert.That(def.ViewName, Is.Null);
        Assert.That(def.Model, Is.InstanceOf<GenericPageConfiguration>());

        // With a page that specifies a view name and an existing config.
        controller.HttpContext.Items["CMS:PageData"] = Page("Custom");
        controller.HttpContext.Items["CMS:PageConfig"] = new GenericPageConfiguration { Style = "x" };
        var custom = (ViewResult)await controller.Index();
        Assert.That(custom.ViewName, Is.EqualTo("Custom"));

        // A page with the "Default" view name falls back to the default view.
        controller.HttpContext.Items["CMS:PageData"] = Page("Default");
        var defaultNamed = (ViewResult)await controller.Index();
        Assert.That(defaultNamed.ViewName, Is.Null);
    }

    [Test]
    public async Task GenericAdminPage_Index_DefaultAndCustomView()
    {
        var controller = Build<GenericAdminPageController>();
        Assert.That(((ViewResult)await controller.Index()).ViewName, Is.Null);

        controller.HttpContext.Items["CMS:PageData"] = Page("AdminView");
        Assert.That(((ViewResult)await controller.Index()).ViewName, Is.EqualTo("AdminView"));
    }
}

[TestFixture]
public class ErrorControllerTests
{
    private ErrorController Build()
    {
        var controller = new ErrorController();
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    [Test]
    public void Index_NoExceptionFeature_ReturnsErrorView()
    {
        var result = (ViewResult)Build().Index();
        Assert.That(result.ViewName, Is.EqualTo("Error"));
    }

    [Test]
    public void Index_WithExceptionFeature_LogsAndReturnsView()
    {
        var controller = Build();
        controller.HttpContext.Features.Set<IExceptionHandlerPathFeature>(
            new ExceptionHandlerFeature { Error = new InvalidOperationException("x"), Path = "/p" });

        Assert.That(((ViewResult)controller.Index()).ViewName, Is.EqualTo("Error"));
    }

    [Test]
    public void Index_FeatureWithoutError_SkipsLogging()
    {
        var controller = Build();
        controller.HttpContext.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature());

        Assert.That(((ViewResult)controller.Index()).ViewName, Is.EqualTo("Error"));
    }

    [Test]
    public void StatusCodeHandler_ReturnsErrorView()
    {
        Assert.That(((ViewResult)Build().StatusCodeHandler(404)).ViewName, Is.EqualTo("Error"));
    }

    [Test]
    public void Index_WithCurrentActivity_UsesActivityId()
    {
        using var activity = new System.Diagnostics.Activity("test");
        activity.Start();
        try
        {
            Assert.That(((ViewResult)Build().Index()).ViewName, Is.EqualTo("Error"));
            Assert.That(((ViewResult)Build().StatusCodeHandler(500)).ViewName, Is.EqualTo("Error"));
        }
        finally
        {
            activity.Stop();
        }
    }
}
