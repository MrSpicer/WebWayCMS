using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewComponents;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services.Users;
using WebWayCMS.Interfaces;
using WebWayCMS.Models.Users;
using WebWayCMS.ViewComponents;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class UsersViewComponentTests
{
    private const string WidgetRouteTokens = "{\"ParentPageNodeId\":\"x\"}";

    private IUserAdminService _users = null!;
    private UsersViewComponent _component = null!;
    private Guid _itemId;
    private readonly UserListPage _page = new([], 0, 0, 25);
    private readonly IReadOnlyList<RoleSummary> _roles = [new RoleSummary("Admin", 1, true)];

    [SetUp]
    public void SetUp()
    {
        _users = Substitute.For<IUserAdminService>();
        _users.ListAsync(Arg.Any<UserListQuery>(), Arg.Any<CancellationToken>()).Returns(_page);
        _users.ListRolesAsync(Arg.Any<CancellationToken>()).Returns(_roles);
        _component = new UsersViewComponent(_users);
        var widget = WidgetComponentHarness.Attach(_component);
        widget.Hit("Users", WidgetRenderMode.Admin, "default", "admin.default");
        widget.Hit("Users", WidgetRenderMode.AdminList, "default", "adminlist.default");
        widget.Hit("Users", WidgetRenderMode.Edit, "default", "edit.default");
        _itemId = Guid.NewGuid();
        AdminRequest("/wadmin/users");
    }

    private void AdminRequest(string path, string query = "", string? userId = "actor", params string[] roles)
    {
        _component.HttpContext.Request.Path = path;
        _component.HttpContext.Request.QueryString = new QueryString(query);
        var claims = new List<Claim>();
        if (userId != null)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        claims.AddRange((roles.Length == 0 ? ["Admin"] : roles).Select(r => new Claim(ClaimTypes.Role, r)));
        _component.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private UsersWidgetConfiguration List(int? pageSize = null) => new()
    {
        PageSize = pageSize,
        ZoneItemNodeId = _itemId,
        Metadata = new WidgetMetadata { DisplayMode = WidgetDisplayMode.List, AdminLabel = "L" },
    };

    private UsersWidgetConfiguration Single(string? userId = null) => new() { UserId = userId, ZoneItemNodeId = _itemId };

    private void MatchedRoute(Guid? owner, string pattern, string dataTokens = "{}", string? routeUserId = null)
    {
        _component.HttpContext.Items["CMS:RouteData"] = new CMSRouteDTO { OwningContentNodeId = owner, Pattern = pattern, DataTokensJson = dataTokens };
        if (routeUserId != null)
            _component.ViewContext.RouteData.Values["userid"] = routeUserId;
    }

    private static UserDetail Detail(string id) => new(
        id, "u@example.com", true, null, null, null, null, [], UserStatus.Active, false, true, false, 0, [], 0, null, 0, null, null, null);

    [Test]
    public void Constructor_NullService_Throws()
    {
        Assert.That(() => new UsersViewComponent(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task List_RendersTheFilteredPageWithUrls()
    {
        MatchedRoute(Guid.NewGuid(), "/wadmin/users");
        AdminRequest("/wadmin/users", "?role=Editor&status=LOCKED&skip=-4");

        var result = await _component.InvokeAsync(List(10));

        var vm = (UserListViewModel)ViewComponentHarness.Model(result)!;
        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminlist.default"));
            Assert.That(vm.Page, Is.SameAs(_page));
            Assert.That(vm.Roles, Is.SameAs(_roles));
            Assert.That((vm.RoleFilter, vm.StatusFilter), Is.EqualTo(("Editor", (UserStatus?)UserStatus.Locked)));
            Assert.That(vm.ListUrl, Is.EqualTo("/wadmin/users"));
            Assert.That(vm.CurrentUrl, Is.EqualTo("/wadmin/users?role=Editor&status=LOCKED&skip=-4"));
            Assert.That((vm.ShowRoles, vm.ShowStatus, vm.ShowLastLogin, vm.ShowInvite), Is.EqualTo((true, true, true, true)));
        });
        await _users.Received(1).ListAsync(new UserListQuery("Editor", UserStatus.Locked, 0, 10), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task List_NoQueryOrRoute_UsesDefaults()
    {
        AdminRequest("/wadmin/users", "?status=bogus&skip=x");

        var result = await _component.InvokeAsync(List());

        var vm = (UserListViewModel)ViewComponentHarness.Model(result)!;
        Assert.That(vm.ListUrl, Is.EqualTo(string.Empty));
        await _users.Received(1).ListAsync(new UserListQuery(null, null, 0, UserListQuery.DefaultTake), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task List_OwnRoute_RendersTheUserWithTheListUrl()
    {
        var detail = Detail("u1");
        _users.GetAsync("u1", Arg.Any<CancellationToken>()).Returns(detail);
        MatchedRoute(_itemId, "/wadmin/users/edit/{userid:guid}", WidgetRouteTokens, "u1");
        AdminRequest("/wadmin/users/edit/u1");

        var result = await _component.InvokeAsync(List());

        var vm = (UserDetailViewModel)ViewComponentHarness.Model(result)!;
        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("admin.default"));
            Assert.That(vm.User, Is.SameAs(detail));
            Assert.That(vm.ListUrl, Is.EqualTo("/wadmin/users"));
            Assert.That(vm.IsSelf, Is.False);
        });
    }

    [Test]
    public async Task List_OwnedRouteWithoutUserId_ListsUsers()
    {
        MatchedRoute(_itemId, "/wadmin/users");
        _component.ViewContext.RouteData.Values["userid"] = "";
        var empty = await _component.InvokeAsync(List());
        _component.ViewContext.RouteData.Values["userid"] = null;
        var nullValue = await _component.InvokeAsync(List());

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(empty), Is.EqualTo("adminlist.default"));
            Assert.That(ViewComponentHarness.ViewName(nullValue), Is.EqualTo("adminlist.default"));
        });
    }

    [Test]
    public async Task List_AnotherItemsRoute_Yields()
    {
        MatchedRoute(Guid.NewGuid(), "/wadmin/users/edit/{userid:guid}", WidgetRouteTokens, "u1");

        var result = await _component.InvokeAsync(List());

        Assert.That(result, Is.InstanceOf<ContentViewComponentResult>());
    }

    [Test]
    public async Task Single_OwnRoute_RendersThatUser_SelfFlagged()
    {
        _users.GetAsync("actor", Arg.Any<CancellationToken>()).Returns(Detail("actor"));
        MatchedRoute(_itemId, "/wadmin/me/edit/{userid:guid}", WidgetRouteTokens, "ACTOR");

        var result = await _component.InvokeAsync(Single());

        var vm = (UserDetailViewModel)ViewComponentHarness.Model(result)!;
        Assert.Multiple(() =>
        {
            Assert.That(vm.IsSelf, Is.True);
            Assert.That(vm.ListUrl, Is.EqualTo(string.Empty));
        });
        await _users.Received(1).GetAsync("ACTOR", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Single_ConfiguredUser_RendersIt_ElseNothing()
    {
        var configured = await _component.InvokeAsync(Single(" u2 "));
        var none = await _component.InvokeAsync(Single(" "));

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(configured), Is.EqualTo("admin.default"));
            Assert.That(none, Is.InstanceOf<ContentViewComponentResult>());
        });
        await _users.Received(1).GetAsync("u2", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Single_OwnRouteListPrefixWithoutEditSegment_KeepsThePrefix()
    {
        MatchedRoute(_itemId, "/wadmin/users", WidgetRouteTokens, "u1");

        var result = await _component.InvokeAsync(List());

        Assert.That(((UserDetailViewModel)ViewComponentHarness.Model(result)!).ListUrl, Is.EqualTo("/wadmin/users"));
    }

    [Test]
    public async Task EditMode_RendersTheConfigurationCard()
    {
        _component.ViewComponentContext.ViewData["ContentZone:RenderMode"] = WidgetRenderMode.Edit;
        var config = List();

        var result = await _component.InvokeAsync(config);
        var nullConfig = await _component.InvokeAsync(null!);

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.Model(result), Is.SameAs(config));
            Assert.That(ViewComponentHarness.ViewName(nullConfig), Is.EqualTo("edit.default"));
        });
        await _users.DidNotReceiveWithAnyArgs().ListAsync(default!, default);
    }

    [Test]
    public async Task PublicPageOrNonAdmin_RendersNothing()
    {
        AdminRequest("/members");
        var publicList = await _component.InvokeAsync(List());
        var publicSingle = await _component.InvokeAsync(Single("u1"));
        AdminRequest("/wadmin/users", userId: "editor", roles: "Editor");
        var editor = await _component.InvokeAsync(List());

        Assert.Multiple(() =>
        {
            Assert.That(publicList, Is.InstanceOf<ContentViewComponentResult>());
            Assert.That(publicSingle, Is.InstanceOf<ContentViewComponentResult>());
            Assert.That(editor, Is.InstanceOf<ContentViewComponentResult>());
        });
        await _users.DidNotReceiveWithAnyArgs().ListAsync(default!, default);
    }

    [Test]
    public async Task GenerateRoutesAsync_OwnsTheEditRoute()
    {
        var item = Guid.NewGuid();
        IRoutableViewComponent routable = _component;

        var routes = await routable.GenerateRoutesAsync("/wadmin/users", item, WidgetDisplayMode.List, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(routable.ComponentName, Is.EqualTo("Users"));
            Assert.That(routes.Single().Pattern, Is.EqualTo("edit/{userId:guid}"));
            Assert.That(routes.Single().OwningContentNodeId, Is.EqualTo(item));
            Assert.That(routes.Single().OwningContentType, Is.EqualTo("UsersWidget"));
        });
    }
}
