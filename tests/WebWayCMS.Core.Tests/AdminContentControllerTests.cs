using Microsoft.AspNetCore.Mvc;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Controllers.Admin;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Models.Shared;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Core.Tests;

public sealed class FakeUpsert
{
    public string? Name { get; set; }
}

/// <summary>A child type placed under "ct" by an ordered containment, for the children panel.</summary>
public sealed class FakeChildUpsert
{
    [ContentRelation("ct", Kind = ContentRelationKind.Containment, Ordered = true)]
    public Guid ParentId { get; set; }
}

/// <summary>A child type whose containment under "ct" opts out of the parent's admin.</summary>
public sealed class FakeHiddenChildUpsert
{
    [ContentRelation("ct", Kind = ContentRelationKind.Containment, ShowInParentAdmin = false)]
    public Guid ParentId { get; set; }
}

[TestFixture]
public class AdminContentControllerTests
{
    private MvcHarness _harness = null!;
    private IAdminHandlerRegistry _registry = null!;
    private IWidgetRegistry _widgetRegistry = null!;
    private IAdminCrudHandler _handler = null!;
    private IAdminCrudHandler _child = null!;
    private IAdminRegistryHandler _registryHandler = null!;
    private AdminContentController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _harness = new MvcHarness();
        _registry = Substitute.For<IAdminHandlerRegistry>();
        _widgetRegistry = Substitute.For<IWidgetRegistry>();
        _handler = Substitute.For<IAdminCrudHandler>();
        _child = Substitute.For<IAdminCrudHandler>();
        _registryHandler = Substitute.For<IAdminRegistryHandler>();

        _handler.IndexViewPath.Returns("~/Index.cshtml");
        _handler.UpsertViewPath.Returns("~/Upsert.cshtml");
        _handler.CreateEmptyUpsertViewModel().Returns(_ => new FakeUpsert());
        _handler.WriteRoles.Returns((string[]?)null);
        // NSubstitute recursively mocks interface returns; force the "not found" / "absent" defaults.
        _handler.RegistryHandler.Returns((IAdminRegistryHandler?)null);
        _registry.GetHandler(Arg.Any<string>()).Returns((IAdminCrudHandler?)null);
        _registry.GetHandler("ct").Returns(_handler);

        _handler.ContentType.Returns("ct");
        _registry.All.Returns([_handler]);

        _controller = new AdminContentController(_registry, _widgetRegistry, new ContentRelationRegistry(_registry));
        _harness.Configure(_controller, new[] { "Admin" });
    }

    private void AsRoles(params string[] roles) => _harness.Configure(_controller, roles);

    [Test]
    public void Constructor_Null_Throws()
    {
        var relations = new ContentRelationRegistry(_registry);
        Assert.That(() => new AdminContentController(null!, _widgetRegistry, relations), Throws.ArgumentNullException);
        Assert.That(() => new AdminContentController(_registry, null!, relations), Throws.ArgumentNullException);
        Assert.That(() => new AdminContentController(_registry, _widgetRegistry, null!), Throws.ArgumentNullException);
    }

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
        Assert.That(await _controller.Delete("missing", Guid.NewGuid(), null, default), Is.InstanceOf<NotFoundObjectResult>());

        AsRoles("Viewer");
        Assert.That(await _controller.Delete("ct", Guid.NewGuid(), null, default), Is.InstanceOf<ForbidResult>());

        AsRoles("Admin");
        Assert.That(await _controller.Delete("ct", Guid.NewGuid(), null, default), Is.InstanceOf<RedirectToActionResult>());
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
    public async Task ApiList_ExcludeSubtreeOf_AsksTheHandlerForTheListWithoutThatSubtree()
    {
        var nodeId = Guid.NewGuid();
        var items = new List<object> { "kept" };
        _handler.GetApiListAsync(Arg.Any<CancellationToken>()).Returns(new List<object> { "all" });
        _handler.GetApiListExcludingSubtreeAsync(nodeId, Arg.Any<CancellationToken>()).Returns(items);

        var excluded = (JsonResult)await _controller.ApiList("ct", default, nodeId);
        var emptyId = (JsonResult)await _controller.ApiList("ct", default, Guid.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(excluded.Value, Is.SameAs(items));
            Assert.That(emptyId.Value, Is.EqualTo(new List<object> { "all" }));
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
        Assert.That(await _controller.VersionHistory("missing", Guid.NewGuid(), null, default), Is.InstanceOf<NotFoundObjectResult>());

        _handler.SupportsVersionHistory.Returns(false);
        Assert.That(await _controller.VersionHistory("ct", Guid.NewGuid(), null, default), Is.InstanceOf<NotFoundResult>());

        _handler.SupportsVersionHistory.Returns(true);
        _handler.GetVersionHistoryViewModelAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((VersionHistoryViewModel?)null, new VersionHistoryViewModel());
        Assert.That(await _controller.VersionHistory("ct", Guid.NewGuid(), null, default), Is.InstanceOf<NotFoundResult>());
        Assert.That(await _controller.VersionHistory("ct", Guid.NewGuid(), null, default), Is.InstanceOf<ViewResult>());
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
        Assert.That(await _controller.VersionDelete("missing", Guid.NewGuid(), Guid.NewGuid(), null, default), Is.InstanceOf<NotFoundObjectResult>());

        _handler.SupportsVersionHistory.Returns(false);
        Assert.That(await _controller.VersionDelete("ct", Guid.NewGuid(), Guid.NewGuid(), null, default), Is.InstanceOf<NotFoundResult>());

        _handler.SupportsVersionHistory.Returns(true);
        AsRoles("Viewer");
        Assert.That(await _controller.VersionDelete("ct", Guid.NewGuid(), Guid.NewGuid(), null, default), Is.InstanceOf<ForbidResult>());

        AsRoles("Admin");
        Assert.That(await _controller.VersionDelete("ct", Guid.NewGuid(), Guid.NewGuid(), null, default), Is.InstanceOf<RedirectToActionResult>());
    }

    [Test]
    public async Task VersionDelete_Refused_ShowsTheReason()
    {
        _handler.SupportsVersionHistory.Returns(true);
        _handler.TryDeleteVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((false, "in history"));

        var result = await _controller.VersionDelete("ct", Guid.NewGuid(), Guid.NewGuid(), null, default);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.InstanceOf<RedirectToActionResult>());
            Assert.That(_controller.TempData["Error"], Is.EqualTo("in history"));
        });
    }

    // --- Preview ---

    [Test]
    public async Task Preview_HandlerMissing_ReturnsNotFound()
    {
        Assert.That(await _controller.Preview("missing", Guid.NewGuid(), default), Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task Preview_SupportsPreviewFalse_ReturnsNotFound()
    {
        _handler.SupportsPreview.Returns(false);

        Assert.That(await _controller.Preview("ct", Guid.NewGuid(), default), Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public async Task Preview_SupportsPreviewTrue_RedirectsToPreviewPath()
    {
        _handler.SupportsPreview.Returns(true);
        var nodeId = Guid.NewGuid();

        var result = await _controller.Preview("ct", nodeId, default) as RedirectResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Url, Is.EqualTo($"/_preview/{nodeId}"));
    }

    [Test]
    public async Task Preview_SupportsPreviewTrue_DoesNotSetCookie()
    {
        _handler.SupportsPreview.Returns(true);

        await _controller.Preview("ct", Guid.NewGuid(), default);

        Assert.That(_controller.Response.Headers.ContainsKey("Set-Cookie"), Is.False);
    }

    // --- Containment: preselect, children panel, reorder ---

    [Test]
    public async Task Create_QueryNamesARelation_PreselectsIt()
    {
        var parent = Guid.NewGuid();
        _handler.GetUpsertViewModelAsync(null, Arg.Any<Microsoft.AspNetCore.Http.IQueryCollection>(), Arg.Any<CancellationToken>())
            .Returns(new FakeChildUpsert());
        _harness.Configure(_controller, new[] { "Admin" },
            new Microsoft.AspNetCore.Http.QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["parentid"] = parent.ToString() }));

        var view = (ViewResult)await _controller.Create("ct", default);

        Assert.That(((FakeChildUpsert)view.Model!).ParentId, Is.EqualTo(parent));
    }

    private void WireChildren()
    {
        var hidden = Substitute.For<IAdminCrudHandler>();
        hidden.ContentType.Returns("hidden");
        hidden.CreateEmptyUpsertViewModel().Returns(_ => new FakeHiddenChildUpsert());
        _child.ContentType.Returns("kids");
        _child.DisplayName.Returns("Kid");
        _child.WriteRoles.Returns(new[] { "Editor" });
        _child.CreateEmptyUpsertViewModel().Returns(_ => new FakeChildUpsert());
        _registry.GetHandler("kids").Returns(_child);
        _registry.All.Returns([_handler, _child, hidden]);
        _controller = new AdminContentController(_registry, _widgetRegistry, new ContentRelationRegistry(_registry));
        _harness.Configure(_controller, new[] { "Admin" });
    }

    [Test]
    public async Task Edit_ListsTheChildrenOfEachShownContainment()
    {
        WireChildren();
        AsRoles("Editor");
        var parent = Guid.NewGuid();
        var kid = new RelatedChild(Guid.NewGuid(), "Kid 1", Data.Models.ContentVersionState.Draft, 0);
        _handler.GetUpsertViewModelAsync(parent, Arg.Any<Microsoft.AspNetCore.Http.IQueryCollection>(), Arg.Any<CancellationToken>())
            .Returns(new FakeUpsert());
        _child.GetRelatedChildrenAsync(nameof(FakeChildUpsert.ParentId), parent, Arg.Any<CancellationToken>())
            .Returns([kid]);

        var view = (ViewResult)await _controller.Edit("ct", parent, default);

        var panels = (IReadOnlyList<RelationChildrenPanelViewModel>)view.ViewData[RelationChildrenPanelViewModel.ViewDataKey]!;
        Assert.Multiple(() =>
        {
            Assert.That(panels, Has.Count.EqualTo(1), "the ShowInParentAdmin = false containment is left out");
            Assert.That(panels[0].ChildContentType, Is.EqualTo("kids"));
            Assert.That(panels[0].ChildDisplayName, Is.EqualTo("Kid"));
            Assert.That(panels[0].RelationName, Is.EqualTo(nameof(FakeChildUpsert.ParentId)));
            Assert.That(panels[0].ParentNodeId, Is.EqualTo(parent));
            Assert.That(panels[0].Ordered, Is.True);
            Assert.That(panels[0].CanWrite, Is.True);
            Assert.That(panels[0].Children, Is.EqualTo(new[] { kid }));
        });
    }

    [Test]
    public async Task Edit_ChildTypeWithoutHandlerOrAdminOnlyWrites_HandledPerViewer()
    {
        WireChildren();
        _child.WriteRoles.Returns((string[]?)null);
        AsRoles("Editor");
        _handler.GetUpsertViewModelAsync(Arg.Any<Guid?>(), Arg.Any<Microsoft.AspNetCore.Http.IQueryCollection>(), Arg.Any<CancellationToken>())
            .Returns(new FakeUpsert());

        // The controller's ViewData is shared by both results, so each is read before the next call.
        var editorView = (ViewResult)await _controller.Edit("ct", Guid.NewGuid(), default);
        var editorPanels = (IReadOnlyList<RelationChildrenPanelViewModel>)editorView.ViewData[RelationChildrenPanelViewModel.ViewDataKey]!;
        _registry.GetHandler("kids").Returns((IAdminCrudHandler?)null);
        var orphanView = (ViewResult)await _controller.Edit("ct", Guid.NewGuid(), default);
        var orphanPanels = (IReadOnlyList<RelationChildrenPanelViewModel>)orphanView.ViewData[RelationChildrenPanelViewModel.ViewDataKey]!;

        Assert.Multiple(() =>
        {
            Assert.That(editorPanels[0].CanWrite, Is.False);
            Assert.That(orphanPanels, Is.Empty);
        });
    }

    [Test]
    public async Task Edit_InTheModal_ListsNoChildren()
    {
        WireChildren();
        _handler.GetUpsertViewModelAsync(Arg.Any<Guid?>(), Arg.Any<Microsoft.AspNetCore.Http.IQueryCollection>(), Arg.Any<CancellationToken>())
            .Returns(new FakeUpsert());
        _harness.Configure(_controller, new[] { "Admin" },
            new Microsoft.AspNetCore.Http.QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["modal"] = "1" }));

        var view = (ViewResult)await _controller.Edit("ct", Guid.NewGuid(), default);

        Assert.That(view.ViewData.ContainsKey(RelationChildrenPanelViewModel.ViewDataKey), Is.False);
    }

    [Test]
    public async Task ReorderRelated_NotFound_Forbidden_FailureAndSuccess()
    {
        var parent = Guid.NewGuid();
        var order = new List<Guid> { Guid.NewGuid() };
        Assert.That(await _controller.ReorderRelated("missing", "R", parent, order, default), Is.InstanceOf<NotFoundObjectResult>());

        AsRoles("Viewer");
        Assert.That(await _controller.ReorderRelated("ct", "R", parent, order, default), Is.InstanceOf<ForbidResult>());

        AsRoles("Admin");
        _handler.ReorderRelatedAsync("R", parent, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns((true, (string?)null), (false, "no"), (false, (string?)null));
        Assert.That(await _controller.ReorderRelated("ct", "R", parent, order, default), Is.InstanceOf<OkResult>());
        Assert.That(await _controller.ReorderRelated("ct", "R", parent, order, default), Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(await _controller.ReorderRelated("ct", "R", parent, null!, default), Is.InstanceOf<BadRequestObjectResult>());
        await _handler.Received(1).ReorderRelatedAsync("R", parent, Arg.Is<IReadOnlyList<Guid>>(l => l.Count == 0), Arg.Any<CancellationToken>());
    }

    // --- Return URLs and the expected version on the top-level history routes ---

    [TestCase("/wadmin/contentzones/page/abc", "/wadmin/contentzones/page/abc")]
    [TestCase("https://evil.example/", null)]
    [TestCase("", null)]
    public async Task VersionHistory_ReturnUrl_ReplacesBackLinkOnlyWhenLocal(string returnUrl, string? expected)
    {
        _controller.Url = new Microsoft.AspNetCore.Mvc.Routing.UrlHelper(_controller.ControllerContext);
        _handler.SupportsVersionHistory.Returns(true);
        _handler.GetVersionHistoryViewModelAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new VersionHistoryViewModel { BackUrl = "/wadmin/ct" });

        var view = (ViewResult)await _controller.VersionHistory("ct", Guid.NewGuid(), returnUrl, default);

        var vm = (VersionHistoryViewModel)view.Model!;
        Assert.Multiple(() =>
        {
            Assert.That(vm.ReturnUrl, Is.EqualTo(expected));
            Assert.That(vm.BackUrl, Is.EqualTo(expected ?? "/wadmin/ct"));
        });
    }

    [Test]
    public async Task RestoreVersion_NotFound_Unsupported_Forbidden_SuccessAndFailure()
    {
        Assert.That(await _controller.RestoreVersion("missing", Guid.NewGuid(), Guid.NewGuid(), null, null, default), Is.InstanceOf<NotFoundObjectResult>());

        _controller.Url = new Microsoft.AspNetCore.Mvc.Routing.UrlHelper(_controller.ControllerContext);
        _handler.SupportsVersionHistory.Returns(false);
        Assert.That(await _controller.RestoreVersion("ct", Guid.NewGuid(), Guid.NewGuid(), null, null, default), Is.InstanceOf<NotFoundResult>());

        _handler.SupportsVersionHistory.Returns(true);
        AsRoles("Viewer");
        Assert.That(await _controller.RestoreVersion("ct", Guid.NewGuid(), Guid.NewGuid(), null, null, default), Is.InstanceOf<ForbidResult>());

        AsRoles("Admin");
        _controller.Url = new Microsoft.AspNetCore.Mvc.Routing.UrlHelper(_controller.ControllerContext);
        var versionId = Guid.NewGuid();
        _handler.RestoreVersionAsync(versionId, 3, Arg.Any<CancellationToken>())
            .Returns(new AdminSaveResult(true), new AdminSaveResult(false, "stale"));

        var ok = (RedirectToActionResult)await _controller.RestoreVersion("ct", Guid.NewGuid(), versionId, 3, "/wadmin/x", default);
        var okMessage = _controller.TempData["Success"];
        var failed = (RedirectToActionResult)await _controller.RestoreVersion("ct", Guid.NewGuid(), versionId, 3, "https://evil.example/", default);

        Assert.Multiple(() =>
        {
            Assert.That(ok.ActionName, Is.EqualTo(nameof(AdminContentController.VersionHistory)));
            Assert.That(ok.RouteValues!["returnUrl"], Is.EqualTo("/wadmin/x"));
            Assert.That(okMessage, Is.Not.Null);
            Assert.That(failed.RouteValues!["returnUrl"], Is.Null);
            Assert.That(_controller.TempData["Error"], Is.EqualTo("stale"));
        });
    }

    [Test]
    public async Task VersionDelete_CarriesALocalReturnUrl()
    {
        _controller.Url = new Microsoft.AspNetCore.Mvc.Routing.UrlHelper(_controller.ControllerContext);
        _handler.SupportsVersionHistory.Returns(true);
        _handler.TryDeleteVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((true, (string?)null));

        var result = (RedirectToActionResult)await _controller.VersionDelete("ct", Guid.NewGuid(), Guid.NewGuid(), "/wadmin/x", default);

        Assert.That(result.RouteValues!["returnUrl"], Is.EqualTo("/wadmin/x"));
    }

    [Test]
    public async Task DeleteAndEditPost_LocalReturnUrl_RedirectsBackThere()
    {
        _controller.Url = new Microsoft.AspNetCore.Mvc.Routing.UrlHelper(_controller.ControllerContext);
        _handler.TryDeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((true, (string?)null, 0));
        _handler.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(new AdminSaveResult(true));

        var deleted = await _controller.Delete("ct", Guid.NewGuid(), "/wadmin/zone-items", default);
        var saved = await _controller.EditPost("ct", null, default, returnUrl: "/wadmin/zone-items");
        var foreign = await _controller.EditPost("ct", null, default, returnUrl: "https://evil.example/");

        Assert.Multiple(() =>
        {
            Assert.That(((LocalRedirectResult)deleted).Url, Is.EqualTo("/wadmin/zone-items"));
            Assert.That(((LocalRedirectResult)saved).Url, Is.EqualTo("/wadmin/zone-items"));
            Assert.That(foreign, Is.InstanceOf<RedirectToActionResult>());
        });
    }
}

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

