using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Services.Users;
using WebWayCMS.Models.Users;
using WebWayCMS.Users;

namespace WebWayCMS.Core.Tests.Users;

[TestFixture]
public class UserManagementSettingsTests
{
    [Test]
    public void UserManagementOptions_DefaultsAndValidation()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UserManagementOptions.SectionName, Is.EqualTo("UserManagement"));
            Assert.That(new UserManagementOptions().InvitationLifetimeDays, Is.EqualTo(7));
            Assert.That(new UserManagementOptions().IsValid(), Is.True);
            Assert.That(new UserManagementOptions { InvitationLifetimeDays = 0 }.IsValid(), Is.False);
            Assert.That(new UserManagementOptions { InvitationLifetimeDays = 90 }.IsValid(), Is.True);
            Assert.That(new UserManagementOptions { InvitationLifetimeDays = 91 }.IsValid(), Is.False);
            Assert.That(new UserManagementOptions { InvitationLifetimeDays = 20_000_000 }.IsValid(), Is.False);
            Assert.That(UserManagementOptions.ValidationMessage, Does.Contain("between 1 and 90"));
        });
    }

    [Test]
    public void InvitationTokenProvider_HasItsOwnProtectorAndSevenDayDefault()
    {
        var options = new InvitationTokenProviderOptions();
        var provider = new InvitationTokenProvider(
            new EphemeralDataProtectionProvider(),
            Options.Create(options),
            Substitute.For<ILogger<DataProtectorTokenProvider<IdentityUser>>>());

        Assert.Multiple(() =>
        {
            Assert.That(options.Name, Is.EqualTo("InvitationProtector"));
            Assert.That(options.TokenLifespan, Is.EqualTo(TimeSpan.FromDays(7)));
            Assert.That(provider.Name, Is.EqualTo("InvitationProtector"));
            Assert.That(InvitationTokenProvider.ProviderName, Is.EqualTo("Invitation"));
            Assert.That(InvitationTokenProvider.Purpose, Is.EqualTo("AcceptInvitation"));
        });
    }

    [TestCase(null, 25)]
    [TestCase(0, 25)]
    [TestCase(-3, 25)]
    [TestCase(10, 10)]
    [TestCase(1000, 200)]
    public void UsersWidgetConfiguration_EffectivePageSize(int? pageSize, int expected)
    {
        Assert.That(new UsersWidgetConfiguration { PageSize = pageSize }.EffectivePageSize, Is.EqualTo(expected));
    }

    [Test]
    public void UsersWidgetConfiguration_Defaults()
    {
        var config = new UsersWidgetConfiguration();

        Assert.Multiple(() =>
        {
            Assert.That((config.ShowRoles, config.ShowStatus, config.ShowLastLogin, config.ShowInvite), Is.EqualTo((true, true, true, true)));
            Assert.That(config.UserId, Is.Null);
            Assert.That(System.Text.RegularExpressions.Regex.IsMatch(Guid.NewGuid().ToString(), $"^{UsersWidgetConfiguration.GuidPattern}$"), Is.True);
        });
    }

    [Test]
    public void UserManagementRoutes_BuildUrls()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UserManagementRoutes.Action("u1/lock"), Is.EqualTo("/wadmin/usermanagement/u1/lock"));
            Assert.That(UserManagementRoutes.DetailUrl("/wadmin/users/", "u1"), Is.EqualTo("/wadmin/users/edit/u1"));
            Assert.That(UserManagementRoutes.ListUrl("/wadmin/users", null, null, 0), Is.EqualTo("/wadmin/users"));
            Assert.That(UserManagementRoutes.ListUrl("/wadmin/users", "Editor", UserStatus.Locked, 25),
                Is.EqualTo("/wadmin/users?role=Editor&status=locked&skip=25"));
            Assert.That(UserManagementRoutes.ListUrl("", " ", null, 0), Is.EqualTo("/"));
        });
    }

    [Test]
    public void UserListViewModel_Pager()
    {
        UserListItem Item() => new("u", "e", null, null, null, [], UserStatus.Active, false, null);

        var first = new UserListViewModel { ListUrl = "/u", Page = new UserListPage([Item(), Item()], 5, 0, 2) };
        var middle = new UserListViewModel { ListUrl = "/u", RoleFilter = "R", Page = new UserListPage([Item(), Item()], 5, 2, 2) };
        var last = new UserListViewModel { ListUrl = "/u", Page = new UserListPage([Item()], 5, 4, 2) };
        var odd = new UserListViewModel { ListUrl = "/u", Page = new UserListPage([Item()], 5, 1, 2) };

        Assert.Multiple(() =>
        {
            Assert.That(first.PreviousPageUrl, Is.Null);
            Assert.That(first.NextPageUrl, Is.EqualTo("/u?skip=2"));
            Assert.That(middle.PreviousPageUrl, Is.EqualTo("/u?role=R"));
            Assert.That(middle.NextPageUrl, Is.EqualTo("/u?role=R&skip=4"));
            Assert.That(last.NextPageUrl, Is.Null);
            Assert.That(odd.PreviousPageUrl, Is.EqualTo("/u"));
            Assert.That(first.DetailUrl("x"), Is.EqualTo("/u/edit/x"));
            Assert.That(new UserListViewModel().Page.Items, Is.Empty);
            Assert.That(new UserDetailViewModel().Roles, Is.Empty);
            Assert.That(new UserRolesViewModel().Roles, Is.Empty);
        });
    }
}
