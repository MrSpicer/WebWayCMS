using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services.Users;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class UserManagementGuardsTests
{
    [Test]
    public void SelfAction_SameUser_Refuses()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UserManagementGuards.SelfAction("a", "a", "lock"), Is.EqualTo("You can't lock your own account."));
            Assert.That(UserManagementGuards.SelfAction("a", "b", "lock"), Is.Null);
        });
    }

    [TestCase(true, 1, true)]
    [TestCase(true, 0, true)]
    [TestCase(true, 2, false)]
    [TestCase(false, 1, false)]
    public void LastActiveAdmin_RefusesOnlyTheLastOne(bool targetIsActiveAdmin, int count, bool refused)
    {
        Assert.That(UserManagementGuards.LastActiveAdmin(targetIsActiveAdmin, count) != null, Is.EqualTo(refused));
    }

    [Test]
    public void UserProfileRules_BuiltInRoles()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UserProfileRules.IsBuiltInRole("admin"), Is.True);
            Assert.That(UserProfileRules.IsBuiltInRole("Authors"), Is.False);
            Assert.That(UserProfileRules.IsBuiltInRole(null), Is.False);
            Assert.That(UserProfileRules.DefaultRole, Is.EqualTo("User"));
        });
    }

    [Test]
    public void UserAdminResult_Factories()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UserAdminResult.Ok("u", "old@example.com"), Is.EqualTo(new UserAdminResult(true, null, "u", "old@example.com")));
            Assert.That(UserAdminResult.Fail("no"), Is.EqualTo(new UserAdminResult(false, "no")));
        });
    }
}
