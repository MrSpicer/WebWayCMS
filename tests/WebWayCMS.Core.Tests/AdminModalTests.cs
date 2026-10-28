using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.ContentZones;
using WebWayCMS.Controllers.Admin;
using WebWayCMS.Controllers.Admin.Handlers;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class AdminModalTests
{
    private sealed class ModalModel
    {
        public string? Title { get; set; }
    }

    private MvcHarness _harness = null!;
    private IAdminHandlerRegistry _registry = null!;
    private IAdminCrudHandler _handler = null!;
    private AdminContentController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _harness = new MvcHarness();
        _registry = Substitute.For<IAdminHandlerRegistry>();
        _handler = Substitute.For<IAdminCrudHandler>();
        _handler.ContentType.Returns("notes");
        _handler.IndexViewPath.Returns("~/Index.cshtml");
        _handler.UpsertViewPath.Returns("~/Upsert.cshtml");
        _handler.CreateEmptyUpsertViewModel().Returns(_ => new ModalModel());
        _handler.WriteRoles.Returns((string[]?)null);
        _handler.ChildHandler.Returns((IAdminCrudChildHandler?)null);
        _handler.RegistryHandler.Returns((IAdminRegistryHandler?)null);
        _registry.GetHandler("notes").Returns(_handler);

        _controller = new AdminContentController(_registry, Substitute.For<IWidgetRegistry>());
        _harness.Configure(_controller, new[] { "Admin" }, Query(modal: true));
    }

    private static IQueryCollection Query(bool modal)
        => new QueryCollection(modal
            ? new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["modal"] = "1" }
            : new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>());

    [Test]
    public void AdminModal_ReadsTheQueryAndViewData()
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?modal=1");
        var viewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(
            new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(),
            new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary());

        Assert.Multiple(() =>
        {
            Assert.That(AdminModal.IsModalRequest(context.Request), Is.True);
            Assert.That(AdminModal.IsModal(viewData), Is.False);
            viewData[AdminModal.ViewDataKey] = true;
            Assert.That(AdminModal.IsModal(viewData), Is.True);
        });
    }

    [Test]
    public async Task CreateAndEdit_SetTheModalViewData()
    {
        _handler.GetUpsertViewModelAsync(Arg.Any<Guid?>(), Arg.Any<IQueryCollection>(), Arg.Any<CancellationToken>())
            .Returns(new ModalModel());

        await _controller.Create("notes", default);
        var createModal = _controller.ViewData[AdminModal.ViewDataKey];

        await _controller.Edit("notes", Guid.NewGuid(), default);
        var editModal = _controller.ViewData[AdminModal.ViewDataKey];

        Assert.Multiple(() =>
        {
            Assert.That(createModal, Is.True);
            Assert.That(editModal, Is.True);
        });
    }

    [Test]
    public async Task EditPost_ModalSuccess_ReturnsJson()
    {
        var id = Guid.NewGuid();
        _handler.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new AdminSaveResult(true, NodeId: id));
        _controller.ModelState.Clear();

        var result = await _controller.EditPost("notes", null, default);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.InstanceOf<JsonResult>());
            Assert.That(Property(((JsonResult)result).Value!, "id"), Is.EqualTo(id));
        });
    }

    [Test]
    public async Task EditPost_ModalValidationFailure_Returns422View()
    {
        _controller.ModelState.AddModelError("Title", "Required.");

        var result = await _controller.EditPost("notes", null, default);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.InstanceOf<ViewResult>());
            Assert.That(_controller.Response.StatusCode, Is.EqualTo(StatusCodes.Status422UnprocessableEntity));
            Assert.That(_controller.ViewData[AdminModal.ViewDataKey], Is.True);
        });
    }

    [Test]
    public async Task EditPost_ModalSaveFailure_Returns422View()
    {
        _handler.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new AdminSaveResult(false, "Bad", "Title"));

        var result = await _controller.EditPost("notes", null, default);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.InstanceOf<ViewResult>());
            Assert.That(_controller.Response.StatusCode, Is.EqualTo(StatusCodes.Status422UnprocessableEntity));
        });
    }

    private static object? Property(object value, string name)
        => value.GetType().GetProperty(name)?.GetValue(value);
}
