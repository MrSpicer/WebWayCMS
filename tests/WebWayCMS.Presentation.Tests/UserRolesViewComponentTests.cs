using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewComponents;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services.Users;
using WebWayCMS.Models.Users;
using WebWayCMS.ViewComponents;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class UserRolesViewComponentTests
{
    private IUserAdminService _users = null!;
    private UserRolesViewComponent _component = null!;

    [SetUp]
    public void SetUp()
    {
        _users = Substitute.For<IUserAdminService>();
        _users.ListRolesAsync(Arg.Any<CancellationToken>()).Returns([new RoleSummary("Authors", 2, false)]);
        _component = new UserRolesViewComponent(_users);
        var widget = WidgetComponentHarness.Attach(_component);
        widget.Hit("UserRoles", WidgetRenderMode.Admin, "default", "admin.default");
        widget.Hit("UserRoles", WidgetRenderMode.Edit, "default", "edit.default");
        Request("/wadmin/users", "Admin");
    }

    private void Request(string path, string role)
    {
        _component.HttpContext.Request.Path = path;
        _component.HttpContext.Request.QueryString = new QueryString("?a=1");
        _component.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "Test"));
    }

    [Test]
    public void Constructor_NullService_Throws()
    {
        Assert.That(() => new UserRolesViewComponent(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task AdminPage_RendersRoles()
    {
        var result = await _component.InvokeAsync();

        var vm = (UserRolesViewModel)ViewComponentHarness.Model(result)!;
        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("admin.default"));
            Assert.That(vm.Roles.Single().Name, Is.EqualTo("Authors"));
            Assert.That(vm.CurrentUrl, Is.EqualTo("/wadmin/users?a=1"));
        });
    }

    [Test]
    public async Task WidgetRoute_StepsAside()
    {
        _component.HttpContext.Items["CMS:RouteData"] = new CMSRouteDTO { DataTokensJson = "{\"ParentPageNodeId\":\"x\"}" };

        var result = await _component.InvokeAsync();

        Assert.That(result, Is.InstanceOf<ContentViewComponentResult>());
    }

    [Test]
    public async Task EditMode_RendersTheCard()
    {
        _component.ViewComponentContext.ViewData["ContentZone:RenderMode"] = WidgetRenderMode.Edit;

        var result = await _component.InvokeAsync();

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("edit.default"));
        await _users.DidNotReceiveWithAnyArgs().ListRolesAsync(default);
    }

    [Test]
    public async Task PublicPageOrNonAdmin_RendersNothing()
    {
        Request("/members", "Admin");
        var publicPage = await _component.InvokeAsync();
        Request("/wadmin/users", "Editor");
        var editor = await _component.InvokeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(publicPage, Is.InstanceOf<ContentViewComponentResult>());
            Assert.That(editor, Is.InstanceOf<ContentViewComponentResult>());
        });
    }
}
