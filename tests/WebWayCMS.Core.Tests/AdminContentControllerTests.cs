using Microsoft.AspNetCore.Mvc;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Models.Shared;

namespace WebWayCMS.Core.Tests;

public sealed class FakeUpsert
{
	public string? Name { get; set; }
}

[TestFixture]
public class AdminContentControllerTests
{
	private MvcHarness _harness = null!;
	private IAdminHandlerRegistry _registry = null!;
	private IAdminCrudHandler _handler = null!;
	private IAdminCrudChildHandler _child = null!;
	private IAdminRegistryHandler _registryHandler = null!;
	private AdminContentController _controller = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new MvcHarness();
		_registry = Substitute.For<IAdminHandlerRegistry>();
		_handler = Substitute.For<IAdminCrudHandler>();
		_child = Substitute.For<IAdminCrudChildHandler>();
		_registryHandler = Substitute.For<IAdminRegistryHandler>();

		_handler.IndexViewPath.Returns("~/Index.cshtml");
		_handler.UpsertViewPath.Returns("~/Upsert.cshtml");
		_handler.CreateEmptyUpsertViewModel().Returns(_ => new FakeUpsert());
		_handler.WriteRoles.Returns((string[]?)null);
		// NSubstitute recursively mocks interface returns; force the "not found" / "absent" defaults.
		_handler.ChildHandler.Returns((IAdminCrudChildHandler?)null);
		_handler.RegistryHandler.Returns((IAdminRegistryHandler?)null);
		_registry.GetHandler(Arg.Any<string>()).Returns((IAdminCrudHandler?)null);
		_registry.GetHandler("ct").Returns(_handler);

		_child.ChildType.Returns("items");
		_child.ChildIndexViewPath.Returns("~/Child.cshtml");
		_child.ChildUpsertViewPath.Returns("~/ChildUpsert.cshtml");
		_child.CreateEmptyChildUpsertViewModel().Returns(_ => new FakeUpsert());
		_child.WriteRoles.Returns((string[]?)null);

		_controller = new AdminContentController(_registry);
		_harness.Configure(_controller, new[] { "Admin" });
	}

	private void AsRoles(params string[] roles) => _harness.Configure(_controller, roles);

	[Test]
	public void Constructor_Null_Throws()
		=> Assert.That(() => new AdminContentController(null!), Throws.ArgumentNullException);

	[Test]
	public async Task Index_NotFoundAndView()
	{
		_handler.GetIndexViewModelAsync(Arg.Any<Microsoft.AspNetCore.Http.IQueryCollection>(), Arg.Any<CancellationToken>()).Returns("vm");

		Assert.Multiple(async () =>
		{
			Assert.That(await _controller.Index("missing", default), Is.InstanceOf<NotFoundObjectResult>());
			var view = await _controller.Index("ct", default) as ViewResult;
			Assert.That(view!.ViewName, Is.EqualTo("~/Index.cshtml"));
		});
	}

	[Test]
	public async Task Create_NotFound_ViewModelAndFallbackEmpty()
	{
		_handler.GetUpsertViewModelAsync(null, Arg.Any<Microsoft.AspNetCore.Http.IQueryCollection>(), Arg.Any<CancellationToken>())
			.Returns((object?)null, new FakeUpsert());

		Assert.Multiple(async () =>
		{
			Assert.That(await _controller.Create("missing", default), Is.InstanceOf<NotFoundObjectResult>());
			Assert.That(((ViewResult)await _controller.Create("ct", default)).Model, Is.InstanceOf<FakeUpsert>()); // null -> empty
			Assert.That(((ViewResult)await _controller.Create("ct", default)).Model, Is.InstanceOf<FakeUpsert>()); // vm
		});
	}

	[Test]
	public async Task Edit_NotFoundMissingAndView()
	{
		_handler.GetUpsertViewModelAsync(Arg.Any<Guid?>(), Arg.Any<Microsoft.AspNetCore.Http.IQueryCollection>(), Arg.Any<CancellationToken>())
			.Returns((object?)null, new FakeUpsert());

		Assert.Multiple(async () =>
		{
			Assert.That(await _controller.Edit("missing", Guid.NewGuid(), default), Is.InstanceOf<NotFoundObjectResult>());
			Assert.That(await _controller.Edit("ct", Guid.NewGuid(), default), Is.InstanceOf<NotFoundResult>());
			Assert.That(await _controller.Edit("ct", Guid.NewGuid(), default), Is.InstanceOf<ViewResult>());
		});
	}

	[Test]
	public async Task EditPost_HandlerMissing_Forbidden_Invalid_Fail_Success()
	{
		Assert.That(await _controller.EditPost("missing", null, default), Is.InstanceOf<NotFoundObjectResult>());

		// Forbidden: handler requires Admin, user is not Admin.
		AsRoles("Viewer");
		Assert.That(await _controller.EditPost("ct", null, default), Is.InstanceOf<ForbidResult>());

		// Invalid model state.
		AsRoles("Admin");
		_controller.ModelState.AddModelError("x", "bad");
		Assert.That(await _controller.EditPost("ct", null, default), Is.InstanceOf<ViewResult>());

		// Save failure surfaces error.
		_controller.ModelState.Clear();
		_harness.Configure(_controller, new[] { "Admin" });
		_handler.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(new AdminSaveResult(false, "nope", "Field"));
		Assert.That(await _controller.EditPost("ct", null, default), Is.InstanceOf<ViewResult>());

		// Success redirects.
		_harness.Configure(_controller, new[] { "Admin" });
		_handler.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(new AdminSaveResult(true));
		Assert.That(await _controller.EditPost("ct", null, default), Is.InstanceOf<RedirectToActionResult>());
	}

	[Test]
	public async Task EditPost_WithExplicitWriteRoles_AllowsEditor()
	{
		_handler.WriteRoles.Returns(new[] { "Admin", "Editor" });
		_handler.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(new AdminSaveResult(true));
		AsRoles("Editor");

		Assert.That(await _controller.EditPost("ct", null, default), Is.InstanceOf<RedirectToActionResult>());
	}

	[Test]
	public async Task Delete_NotFound_Forbidden_Success()
	{
		Assert.That(await _controller.Delete("missing", Guid.NewGuid(), default), Is.InstanceOf<NotFoundObjectResult>());

		AsRoles("Viewer");
		Assert.That(await _controller.Delete("ct", Guid.NewGuid(), default), Is.InstanceOf<ForbidResult>());

		AsRoles("Admin");
		Assert.That(await _controller.Delete("ct", Guid.NewGuid(), default), Is.InstanceOf<RedirectToActionResult>());
	}

	[Test]
	public async Task ApiList_NotFoundAndJson()
	{
		_handler.GetApiListAsync(Arg.Any<CancellationToken>()).Returns(new List<object>());

		Assert.Multiple(async () =>
		{
			Assert.That(await _controller.ApiList("missing", default), Is.InstanceOf<NotFoundObjectResult>());
			Assert.That(await _controller.ApiList("ct", default), Is.InstanceOf<JsonResult>());
		});
	}

	[Test]
	public async Task SecondaryApiList_NotFound_NoSecondary_Json()
	{
		Assert.That(await _controller.SecondaryApiList("missing", "k", default), Is.InstanceOf<NotFoundObjectResult>());

		_handler.HasSecondaryApiList.Returns(false);
		Assert.That(await _controller.SecondaryApiList("ct", "k", default), Is.InstanceOf<NotFoundResult>());

		_handler.HasSecondaryApiList.Returns(true);
		_handler.GetSecondaryApiListAsync("k", Arg.Any<CancellationToken>()).Returns(new List<object>());
		Assert.That(await _controller.SecondaryApiList("ct", "k", default), Is.InstanceOf<JsonResult>());
	}

	[Test]
	public void RegistryList_NotFound_NoRegistry_Result()
	{
		Assert.That(_controller.RegistryList("missing"), Is.InstanceOf<NotFoundObjectResult>());

		_handler.RegistryHandler.Returns((IAdminRegistryHandler?)null);
		Assert.That(_controller.RegistryList("ct"), Is.InstanceOf<NotFoundResult>());

		_registryHandler.GetAll().Returns(new OkResult());
		_handler.RegistryHandler.Returns(_registryHandler);
		Assert.That(_controller.RegistryList("ct"), Is.InstanceOf<OkResult>());
	}

	[Test]
	public void RegistryProperties_NotFound_NoRegistry_Result()
	{
		Assert.That(_controller.RegistryProperties("missing", "n"), Is.InstanceOf<NotFoundObjectResult>());

		_handler.RegistryHandler.Returns((IAdminRegistryHandler?)null);
		Assert.That(_controller.RegistryProperties("ct", "n"), Is.InstanceOf<NotFoundResult>());

		_registryHandler.GetProperties("n").Returns(new OkResult());
		_handler.RegistryHandler.Returns(_registryHandler);
		Assert.That(_controller.RegistryProperties("ct", "n"), Is.InstanceOf<OkResult>());
	}

	[Test]
	public async Task VersionHistory_NotFound_Unsupported_NullVm_View()
	{
		Assert.That(await _controller.VersionHistory("missing", Guid.NewGuid(), default), Is.InstanceOf<NotFoundObjectResult>());

		_handler.SupportsVersionHistory.Returns(false);
		Assert.That(await _controller.VersionHistory("ct", Guid.NewGuid(), default), Is.InstanceOf<NotFoundResult>());

		_handler.SupportsVersionHistory.Returns(true);
		_handler.GetVersionHistoryViewModelAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((VersionHistoryViewModel?)null, new VersionHistoryViewModel());
		Assert.That(await _controller.VersionHistory("ct", Guid.NewGuid(), default), Is.InstanceOf<NotFoundResult>());
		Assert.That(await _controller.VersionHistory("ct", Guid.NewGuid(), default), Is.InstanceOf<ViewResult>());
	}

	[Test]
	public async Task VersionRestoreEdit_NotFound_Unsupported_NullVm_View()
	{
		Assert.That(await _controller.VersionRestoreEdit("missing", Guid.NewGuid(), Guid.NewGuid(), default), Is.InstanceOf<NotFoundObjectResult>());

		_handler.SupportsVersionHistory.Returns(false);
		Assert.That(await _controller.VersionRestoreEdit("ct", Guid.NewGuid(), Guid.NewGuid(), default), Is.InstanceOf<NotFoundResult>());

		_handler.SupportsVersionHistory.Returns(true);
		_handler.GetRestoreVersionViewModelAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((object?)null, new FakeUpsert());
		Assert.That(await _controller.VersionRestoreEdit("ct", Guid.NewGuid(), Guid.NewGuid(), default), Is.InstanceOf<NotFoundResult>());
		Assert.That(await _controller.VersionRestoreEdit("ct", Guid.NewGuid(), Guid.NewGuid(), default), Is.InstanceOf<ViewResult>());
	}

	[Test]
	public async Task VersionDelete_NotFound_Unsupported_Forbidden_Redirect()
	{
		Assert.That(await _controller.VersionDelete("missing", Guid.NewGuid(), Guid.NewGuid(), default), Is.InstanceOf<NotFoundObjectResult>());

		_handler.SupportsVersionHistory.Returns(false);
		Assert.That(await _controller.VersionDelete("ct", Guid.NewGuid(), Guid.NewGuid(), default), Is.InstanceOf<NotFoundResult>());

		_handler.SupportsVersionHistory.Returns(true);
		AsRoles("Viewer");
		Assert.That(await _controller.VersionDelete("ct", Guid.NewGuid(), Guid.NewGuid(), default), Is.InstanceOf<ForbidResult>());

		AsRoles("Admin");
		Assert.That(await _controller.VersionDelete("ct", Guid.NewGuid(), Guid.NewGuid(), default), Is.InstanceOf<RedirectToActionResult>());
	}

	// --- Child actions ---

	private void WireChild() => _handler.ChildHandler.Returns(_child);

	[Test]
	public async Task ChildIndex_NotFound_TypeMismatch_NullVm_View()
	{
		Assert.That(await _controller.ChildIndex("missing", "p", "items", default), Is.InstanceOf<NotFoundObjectResult>());

		// child null
		Assert.That(await _controller.ChildIndex("ct", "p", "items", default), Is.InstanceOf<NotFoundResult>());

		// type mismatch
		WireChild();
		Assert.That(await _controller.ChildIndex("ct", "p", "wrong", default), Is.InstanceOf<NotFoundResult>());

		// vm null
		_child.GetChildIndexViewModelAsync("p", Arg.Any<CancellationToken>()).Returns((object?)null, new FakeUpsert());
		Assert.That(await _controller.ChildIndex("ct", "p", "items", default), Is.InstanceOf<NotFoundResult>());
		Assert.That(await _controller.ChildIndex("ct", "p", "items", default), Is.InstanceOf<ViewResult>());
	}

	[Test]
	public async Task ChildCreate_NullVm_And_View()
	{
		WireChild();
		Assert.That(await _controller.ChildCreate("missing", "p", "items", default), Is.InstanceOf<NotFoundObjectResult>());

		_child.GetChildUpsertViewModelAsync("p", null, Arg.Any<CancellationToken>()).Returns((object?)null, new FakeUpsert());
		Assert.That(await _controller.ChildCreate("ct", "p", "items", default), Is.InstanceOf<NotFoundResult>());
		Assert.That(await _controller.ChildCreate("ct", "p", "items", default), Is.InstanceOf<ViewResult>());
	}

	[Test]
	public async Task ChildEdit_NullVm_And_View()
	{
		WireChild();
		_child.GetChildUpsertViewModelAsync("p", Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns((object?)null, new FakeUpsert());

		Assert.That(await _controller.ChildEdit("ct", "p", "items", Guid.NewGuid(), default), Is.InstanceOf<NotFoundResult>());
		Assert.That(await _controller.ChildEdit("ct", "p", "items", Guid.NewGuid(), default), Is.InstanceOf<ViewResult>());
	}

	[Test]
	public async Task ChildEditPost_Forbidden_Invalid_Fail_Success()
	{
		WireChild();
		Assert.That(await _controller.ChildEditPost("missing", "p", "items", null, default), Is.InstanceOf<NotFoundObjectResult>());

		AsRoles("Viewer");
		Assert.That(await _controller.ChildEditPost("ct", "p", "items", null, default), Is.InstanceOf<ForbidResult>());

		AsRoles("Admin");
		_controller.ModelState.AddModelError("x", "bad");
		Assert.That(await _controller.ChildEditPost("ct", "p", "items", null, default), Is.InstanceOf<ViewResult>());

		_controller.ModelState.Clear();
		_harness.Configure(_controller, new[] { "Admin" });
		_child.SaveChildUpsertAsync("p", Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(new AdminSaveResult(false, "e"));
		Assert.That(await _controller.ChildEditPost("ct", "p", "items", null, default), Is.InstanceOf<ViewResult>());

		_harness.Configure(_controller, new[] { "Admin" });
		_child.SaveChildUpsertAsync("p", Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(new AdminSaveResult(true));
		Assert.That(await _controller.ChildEditPost("ct", "p", "items", null, default), Is.InstanceOf<RedirectToActionResult>());
	}

	[Test]
	public async Task ChildDelete_Forbidden_And_Redirect()
	{
		WireChild();
		AsRoles("Viewer");
		Assert.That(await _controller.ChildDelete("ct", "p", "items", Guid.NewGuid(), default), Is.InstanceOf<ForbidResult>());

		AsRoles("Admin");
		Assert.That(await _controller.ChildDelete("ct", "p", "items", Guid.NewGuid(), default), Is.InstanceOf<RedirectToActionResult>());
	}

	[Test]
	public async Task ChildReorder_Forbidden_Unsupported_OkAnd500()
	{
		WireChild();
		AsRoles("Viewer");
		Assert.That(await _controller.ChildReorder("ct", "p", "items", new List<Guid>(), default), Is.InstanceOf<ForbidResult>());

		AsRoles("Admin");
		_child.SupportsReorder.Returns(false);
		Assert.That(await _controller.ChildReorder("ct", "p", "items", new List<Guid>(), default), Is.InstanceOf<BadRequestObjectResult>());

		_child.SupportsReorder.Returns(true);
		_child.ReorderAsync("p", Arg.Any<List<Guid>>(), Arg.Any<CancellationToken>()).Returns(true, false);
		Assert.That(await _controller.ChildReorder("ct", "p", "items", new List<Guid>(), default), Is.InstanceOf<OkResult>());
		Assert.That(((StatusCodeResult)await _controller.ChildReorder("ct", "p", "items", new List<Guid>(), default)).StatusCode, Is.EqualTo(500));
	}

	[Test]
	public async Task ChildVersionHistory_Unsupported_NullVm_View()
	{
		WireChild();
		Assert.That(await _controller.ChildVersionHistory("ct", "p", "wrong", Guid.NewGuid(), default), Is.InstanceOf<NotFoundResult>());

		_child.SupportsVersionHistory.Returns(false);
		Assert.That(await _controller.ChildVersionHistory("ct", "p", "items", Guid.NewGuid(), default), Is.InstanceOf<NotFoundResult>());

		_child.SupportsVersionHistory.Returns(true);
		_child.GetChildVersionHistoryViewModelAsync("p", Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((VersionHistoryViewModel?)null, new VersionHistoryViewModel());
		Assert.That(await _controller.ChildVersionHistory("ct", "p", "items", Guid.NewGuid(), default), Is.InstanceOf<NotFoundResult>());
		Assert.That(await _controller.ChildVersionHistory("ct", "p", "items", Guid.NewGuid(), default), Is.InstanceOf<ViewResult>());
	}

	[Test]
	public async Task ChildVersionRestoreEdit_Unsupported_NullVm_View()
	{
		WireChild();
		_child.SupportsVersionHistory.Returns(false);
		Assert.That(await _controller.ChildVersionRestoreEdit("ct", "p", "items", Guid.NewGuid(), Guid.NewGuid(), default), Is.InstanceOf<NotFoundResult>());

		_child.SupportsVersionHistory.Returns(true);
		_child.GetChildRestoreVersionViewModelAsync("p", Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((object?)null, new FakeUpsert());
		Assert.That(await _controller.ChildVersionRestoreEdit("ct", "p", "items", Guid.NewGuid(), Guid.NewGuid(), default), Is.InstanceOf<NotFoundResult>());
		Assert.That(await _controller.ChildVersionRestoreEdit("ct", "p", "items", Guid.NewGuid(), Guid.NewGuid(), default), Is.InstanceOf<ViewResult>());
	}

	[Test]
	public async Task ChildVersionDelete_Unsupported_Forbidden_Redirect()
	{
		WireChild();
		_child.SupportsVersionHistory.Returns(false);
		Assert.That(await _controller.ChildVersionDelete("ct", "p", "items", Guid.NewGuid(), Guid.NewGuid(), default), Is.InstanceOf<NotFoundResult>());

		_child.SupportsVersionHistory.Returns(true);
		AsRoles("Viewer");
		Assert.That(await _controller.ChildVersionDelete("ct", "p", "items", Guid.NewGuid(), Guid.NewGuid(), default), Is.InstanceOf<ForbidResult>());

		AsRoles("Admin");
		Assert.That(await _controller.ChildVersionDelete("ct", "p", "items", Guid.NewGuid(), Guid.NewGuid(), default), Is.InstanceOf<RedirectToActionResult>());
	}

	[Test]
	public async Task ChildActions_HandlerMissingReturnsNotFound()
	{
		Assert.Multiple(async () =>
		{
			Assert.That(await _controller.ChildCreate("missing", "p", "items", default), Is.InstanceOf<NotFoundObjectResult>());
			Assert.That(await _controller.ChildEdit("missing", "p", "items", Guid.NewGuid(), default), Is.InstanceOf<NotFoundObjectResult>());
			Assert.That(await _controller.ChildDelete("missing", "p", "items", Guid.NewGuid(), default), Is.InstanceOf<NotFoundObjectResult>());
			Assert.That(await _controller.ChildReorder("missing", "p", "items", new List<Guid>(), default), Is.InstanceOf<NotFoundObjectResult>());
			Assert.That(await _controller.ChildVersionHistory("missing", "p", "items", Guid.NewGuid(), default), Is.InstanceOf<NotFoundObjectResult>());
			Assert.That(await _controller.ChildVersionRestoreEdit("missing", "p", "items", Guid.NewGuid(), Guid.NewGuid(), default), Is.InstanceOf<NotFoundObjectResult>());
			Assert.That(await _controller.ChildVersionDelete("missing", "p", "items", Guid.NewGuid(), Guid.NewGuid(), default), Is.InstanceOf<NotFoundObjectResult>());
		});
	}
}
