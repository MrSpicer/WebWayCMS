using System.Security.Claims;

using Microsoft.AspNetCore.Mvc;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class AdminApiTokensControllerTests
{
    private IApiTokenService _tokens = null!;
    private AdminApiTokensController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _tokens = Substitute.For<IApiTokenService>();
        _controller = new AdminApiTokensController(_tokens);
        new MvcHarness().Configure(_controller, ["Admin"]);
    }

    [Test]
    public void Constructor_Null_Throws() =>
        Assert.That(() => new AdminApiTokensController(null!), Throws.ArgumentNullException);

    [Test]
    public async Task Index_RendersEveryUsersTokens()
    {
        IReadOnlyList<ApiTokenListItem> items = [new(new ApiTokenDTO { Name = "Laptop" }, "a@example.com")];
        _tokens.ListAllAsync(Arg.Any<CancellationToken>()).Returns(items);

        var result = (ViewResult)await _controller.Index(default);

        Assert.Multiple(() =>
        {
            Assert.That(result.ViewName, Does.EndWith("AdminApiTokens/Index.cshtml"));
            Assert.That(result.Model, Is.SameAs(items));
        });
    }

    [Test]
    public async Task Revoke_AsAdmin_FlashesSuccess_AndRedirects()
    {
        var id = Guid.NewGuid();
        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "admin-1"), new Claim(ClaimTypes.Role, "Admin")], "Test"));
        _tokens.RevokeAsync(id, "admin-1", true, Arg.Any<CancellationToken>()).Returns((true, (string?)null));

        var result = (RedirectToActionResult)await _controller.Revoke(id, default);

        Assert.Multiple(() =>
        {
            Assert.That(result.ActionName, Is.EqualTo(nameof(AdminApiTokensController.Index)));
            Assert.That(_controller.TempData["Success"], Is.EqualTo("The token was revoked."));
        });
    }

    [Test]
    public async Task Revoke_Refused_FlashesTheError()
    {
        _tokens.RevokeAsync(Arg.Any<Guid>(), string.Empty, true, Arg.Any<CancellationToken>()).Returns((false, (string?)"Token not found."));

        await _controller.Revoke(Guid.NewGuid(), default);

        Assert.That(_controller.TempData["Error"], Is.EqualTo("Token not found."));
    }
}

[TestFixture]
public class RoleAccessTests
{
    private static ClaimsPrincipal User(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "Test"));

    [Test]
    public void Allows_NullRoles_MeansAdminOnly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RoleAccess.Allows(null, User("Admin")), Is.True);
            Assert.That(RoleAccess.Allows(null, User("Editor")), Is.False);
        });
    }

    [Test]
    public void Allows_ListedRoles_AnyMatchGrants()
    {
        string[] roles = ["Admin", "Editor"];

        Assert.Multiple(() =>
        {
            Assert.That(RoleAccess.Allows(roles, User("Editor")), Is.True);
            Assert.That(RoleAccess.Allows(roles, User("User")), Is.False);
            Assert.That(RoleAccess.Allows([], User("Admin")), Is.False, "an empty list grants no one");
        });
    }

    [Test]
    public void Allows_NoUser_IsFalse() =>
        Assert.That(RoleAccess.Allows(["Admin"], null), Is.False);
}
