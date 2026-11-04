using System.Security.Claims;

using ModelContextProtocol;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Services.Users;
using WebWayCMS.Users;

namespace WebWayCMS.Mcp.Tests;

[TestFixture]
public class UserToolsetTests
{
    private const string ActorId = "actor-1";
    private static readonly UserManagementResult Ok = new(true, "Done.", "u1");

    private IUserAdminService _users = null!;
    private IUserManagementModel _model = null!;
    private UserToolset _tools = null!;

    private sealed class AdminWithId : IMcpCaller
    {
        public ClaimsPrincipal? User { get; } = new(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "Admin"), new Claim(ClaimTypes.NameIdentifier, ActorId)], "Test"));
    }

    private static readonly Func<UserActor, bool> IsTokenActor = a => a.UserId == ActorId && !a.RequirePassword && a.Password == null;

    [SetUp]
    public void SetUp()
    {
        _users = Substitute.For<IUserAdminService>();
        _model = Substitute.For<IUserManagementModel>();
        _tools = new UserToolset(_users, _model, new AdminWithId());
    }

    private static UserDetail Detail(string email = "a@example.com") => new(
        "u1", email, true, "555", "Display", "First", "Last", ["User"], UserStatus.Active, false, true, false, 0, [], 0,
        null, 0, null, null, null);

    [Test]
    public void Constructor_NullDependency_Throws()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new UserToolset(null!, _model, TestCaller.Admin), Throws.ArgumentNullException);
            Assert.That(() => new UserToolset(_users, null!, TestCaller.Admin), Throws.ArgumentNullException);
            Assert.That(() => new UserToolset(_users, _model, null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public async Task ListUsers_PassesFiltersAndParsesStatus()
    {
        var page = new UserListPage([], 0, 5, 10);
        _users.ListAsync(Arg.Any<UserListQuery>(), Arg.Any<CancellationToken>()).Returns(page);

        Assert.That(await _tools.ListUsers("Editor", "Locked", 5, 10), Is.SameAs(page));
        await _tools.ListUsers();

        await _users.Received(1).ListAsync(new UserListQuery("Editor", UserStatus.Locked, 5, 10), Arg.Any<CancellationToken>());
        await _users.Received(1).ListAsync(new UserListQuery(null, null, 0, UserListQuery.DefaultTake), Arg.Any<CancellationToken>());
    }

    [TestCase("bogus")]
    [TestCase("9")]
    public void ListUsers_UnknownStatus_Throws(string status)
    {
        Assert.That(async () => await _tools.ListUsers(status: status), Throws.TypeOf<McpException>().With.Message.Contains("Unknown status"));
    }

    [Test]
    public async Task GetUser_ReturnsDetailOrThrows()
    {
        var detail = Detail();
        _users.GetAsync("u1", Arg.Any<CancellationToken>()).Returns(detail);

        Assert.That(await _tools.GetUser("u1"), Is.SameAs(detail));
        Assert.That(async () => await _tools.GetUser("missing"), Throws.TypeOf<McpException>());
    }

    [Test]
    public async Task ListRoles_ReturnsRoles()
    {
        IReadOnlyList<RoleSummary> roles = [new RoleSummary("Admin", 1, true)];
        _users.ListRolesAsync(Arg.Any<CancellationToken>()).Returns(roles);

        Assert.That(await _tools.ListRoles(), Is.SameAs(roles));
    }

    [Test]
    public async Task Reads_RequireAdmin()
    {
        var tools = new UserToolset(_users, _model, TestCaller.Editor);

        Assert.Multiple(() =>
        {
            Assert.That(async () => await tools.ListUsers(), Throws.TypeOf<McpException>());
            Assert.That(async () => await tools.GetUser("u1"), Throws.TypeOf<McpException>());
            Assert.That(async () => await tools.ListRoles(), Throws.TypeOf<McpException>());
            Assert.That(async () => await tools.LockUser("u1"), Throws.TypeOf<McpException>());
        });
        await _model.DidNotReceiveWithAnyArgs().LockAsync(default!, default!);
    }

    [Test]
    public async Task InviteUser_BuildsInviteAndActsWithoutPassword()
    {
        _model.InviteAsync(Arg.Any<UserInvite>(), Arg.Any<UserActor>(), Arg.Any<CancellationToken>()).Returns(Ok);

        var result = await _tools.InviteUser("n@example.com", ["User"], "N", "F", "L");
        await _tools.InviteUser("m@example.com");

        Assert.That(result, Is.EqualTo(new McpUserActionResult("Done.", "u1")));
        await _model.Received(1).InviteAsync(
            Arg.Is<UserInvite>(i => i.Email == "n@example.com" && i.Roles.SequenceEqual(new[] { "User" })
                && i.Profile == new UserProfileInput("N", "F", "L")),
            Arg.Is<UserActor>(a => IsTokenActor(a)), Arg.Any<CancellationToken>());
        await _model.Received(1).InviteAsync(Arg.Is<UserInvite>(i => i.Roles.Count == 0), Arg.Any<UserActor>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateUser_OverlaysSuppliedFieldsOnTheCurrentUser()
    {
        _users.GetAsync("u1", Arg.Any<CancellationToken>()).Returns(Detail());
        _model.UpdateAsync("u1", Arg.Any<UserUpdate>(), Arg.Any<UserActor>(), Arg.Any<CancellationToken>()).Returns(Ok);

        await _tools.UpdateUser("u1", phoneNumber: "", displayName: "New");
        await _tools.UpdateUser("u1", email: "b@example.com", firstName: "F2", lastName: "L2");

        await _model.Received(1).UpdateAsync("u1",
            new UserUpdate("a@example.com", "", new UserProfileInput("New", "First", "Last")),
            Arg.Is<UserActor>(a => IsTokenActor(a)), Arg.Any<CancellationToken>());
        await _model.Received(1).UpdateAsync("u1",
            new UserUpdate("b@example.com", "555", new UserProfileInput("Display", "F2", "L2")),
            Arg.Any<UserActor>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void UpdateUser_UnknownUser_Throws()
    {
        Assert.That(async () => await _tools.UpdateUser("missing"), Throws.TypeOf<McpException>());
    }

    [Test]
    public async Task SetUserRoles_NullRolesMeansNone()
    {
        _model.SetRolesAsync("u1", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<UserActor>(), Arg.Any<CancellationToken>()).Returns(Ok);

        await _tools.SetUserRoles("u1", ["Editor"]);
        await _tools.SetUserRoles("u1", null!);

        await _model.Received(1).SetRolesAsync("u1", Arg.Is<IReadOnlyCollection<string>>(r => r.SequenceEqual(new[] { "Editor" })), Arg.Any<UserActor>(), Arg.Any<CancellationToken>());
        await _model.Received(1).SetRolesAsync("u1", Arg.Is<IReadOnlyCollection<string>>(r => r.Count == 0), Arg.Any<UserActor>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SimpleActions_DelegateToTheModel()
    {
        var actor = Arg.Any<UserActor>();
        _model.SendPasswordResetAsync("u1", actor, Arg.Any<CancellationToken>()).Returns(Ok);
        _model.SendConfirmationAsync("u1", Arg.Any<UserActor>(), Arg.Any<CancellationToken>()).Returns(Ok);
        _model.ResendInvitationAsync("u1", Arg.Any<UserActor>(), Arg.Any<CancellationToken>()).Returns(Ok);
        _model.ConfirmEmailAsync("u1", Arg.Any<UserActor>(), Arg.Any<CancellationToken>()).Returns(Ok);
        _model.LockAsync("u1", Arg.Any<UserActor>(), Arg.Any<CancellationToken>()).Returns(Ok);
        _model.UnlockAsync("u1", Arg.Any<UserActor>(), Arg.Any<CancellationToken>()).Returns(Ok);
        _model.SignOutEverywhereAsync("u1", Arg.Any<UserActor>(), Arg.Any<CancellationToken>()).Returns(Ok);
        _model.ResetTwoFactorAsync("u1", Arg.Any<UserActor>(), Arg.Any<CancellationToken>()).Returns(Ok);
        _model.RevokeApiTokensAsync("u1", Arg.Any<UserActor>(), Arg.Any<CancellationToken>()).Returns(Ok);
        _model.RemovePasskeysAsync("u1", Arg.Any<UserActor>(), Arg.Any<CancellationToken>()).Returns(Ok);
        _model.RemoveExternalLoginAsync("u1", "GitHub", "k", Arg.Any<UserActor>(), Arg.Any<CancellationToken>()).Returns(Ok);
        _model.DeleteAsync("u1", Arg.Any<UserActor>(), Arg.Any<CancellationToken>()).Returns(Ok);
        _model.RestoreAsync("u1", Arg.Any<UserActor>(), Arg.Any<CancellationToken>()).Returns(Ok);
        _model.PurgeAsync("u1", Arg.Any<UserActor>(), Arg.Any<CancellationToken>()).Returns(Ok);
        _model.CreateRoleAsync("R", Arg.Any<UserActor>(), Arg.Any<CancellationToken>()).Returns(Ok);
        _model.RenameRoleAsync("R", "S", Arg.Any<UserActor>(), Arg.Any<CancellationToken>()).Returns(Ok);
        _model.DeleteRoleAsync("S", Arg.Any<UserActor>(), Arg.Any<CancellationToken>()).Returns(Ok);

        var results = new[]
        {
            await _tools.SendPasswordReset("u1"),
            await _tools.SendConfirmationEmail("u1"),
            await _tools.ResendInvitation("u1"),
            await _tools.ConfirmUserEmail("u1"),
            await _tools.LockUser("u1"),
            await _tools.UnlockUser("u1"),
            await _tools.SignOutUserEverywhere("u1"),
            await _tools.ResetUserTwoFactor("u1"),
            await _tools.RevokeUserApiTokens("u1"),
            await _tools.RemoveUserPasskeys("u1"),
            await _tools.RemoveUserExternalLogin("u1", "GitHub", "k"),
            await _tools.DeleteUser("u1"),
            await _tools.RestoreUser("u1"),
            await _tools.PurgeUser("u1"),
            await _tools.CreateRole("R"),
            await _tools.RenameRole("R", "S"),
            await _tools.DeleteRole("S"),
        };

        Assert.That(results, Has.All.EqualTo(new McpUserActionResult("Done.", "u1")));
        await _model.Received(1).PurgeAsync("u1", Arg.Is<UserActor>(a => IsTokenActor(a)), Arg.Any<CancellationToken>());
    }

    [Test]
    public void RefusedAction_ThrowsWithTheReason()
    {
        _model.LockAsync("u1", Arg.Any<UserActor>(), Arg.Any<CancellationToken>())
            .Returns(new UserManagementResult(false, "You can't lock your own account."));

        Assert.That(async () => await _tools.LockUser("u1"),
            Throws.TypeOf<McpException>().With.Message.EqualTo("You can't lock your own account."));
    }

    [Test]
    public async Task Actor_WithoutNameIdentifier_UsesEmptyId()
    {
        var tools = new UserToolset(_users, _model, TestCaller.Admin);
        _model.LockAsync("u1", Arg.Any<UserActor>(), Arg.Any<CancellationToken>()).Returns(Ok);

        await tools.LockUser("u1");

        await _model.Received(1).LockAsync("u1", Arg.Is<UserActor>(a => a.UserId == string.Empty), Arg.Any<CancellationToken>());
    }
}
