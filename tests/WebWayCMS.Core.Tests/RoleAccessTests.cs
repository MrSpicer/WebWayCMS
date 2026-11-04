using System.Security.Claims;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;

namespace WebWayCMS.Core.Tests;

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
