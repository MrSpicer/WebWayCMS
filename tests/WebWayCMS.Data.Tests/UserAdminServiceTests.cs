using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services.Users;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class UserAdminServiceTests
{
    private const string Password = "Test-Pass-123!";

    private ServiceProvider _provider = null!;
    private IServiceScope _scope = null!;
    private UserManager<IdentityUser> _users = null!;
    private UserAdminService _service = null!;
    private CmsDbContext _context = null!;
    private string _actorId = null!;

    [SetUp]
    public async Task SetUp()
    {
        var db = TestContexts.NewDb();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        // The passkey table exists only in a Version3 model. EF caches a context type's model, and the other
        // suites build CmsDbContext without Identity options, so this one gets its own cache key: the CMS
        // key factory plus a marker extension.
        services.AddSingleton<ICmsModelExtension, PasskeyModelMarker>();
        services.AddDbContext<CmsDbContext>(o => o
            .UseInMemoryDatabase(db)
            .ReplaceService<IModelCacheKeyFactory, CmsModelCacheKeyFactory>()
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddIdentityCore<IdentityUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<CmsDbContext>()
            .AddDefaultTokenProviders();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        _users = _scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roles = _scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        _context = _scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        _service = new UserAdminService(_context, _users, roles);

        foreach (var role in UserProfileRules.BuiltInRoles)
            await roles.CreateAsync(new IdentityRole(role));

        _actorId = (await CreateUserAsync("actor@example.com", UserProfileRules.AdminRole)).Id;
    }

    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    private async Task<IdentityUser> CreateUserAsync(string email, params string[] roles)
    {
        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        Assert.That((await _users.CreateAsync(user, Password)).Succeeded, Is.True);
        if (roles.Length > 0)
            await _users.AddToRolesAsync(user, roles);
        return user;
    }

    private async Task<UserProfileDTO?> ProfileAsync(string userId)
    {
        _context.ChangeTracker.Clear();
        return await _context.Set<UserProfileDTO>().AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);
    }

    private async Task<IdentityUser> ReloadAsync(string userId)
    {
        _context.ChangeTracker.Clear();
        return (await _users.FindByIdAsync(userId))!;
    }

    // ----- constructor ---------------------------------------------------------------------------------

    [Test]
    public void Constructor_NullDependency_Throws()
    {
        var roles = _scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        Assert.Multiple(() =>
        {
            Assert.That(() => new UserAdminService(null!, _users, roles), Throws.ArgumentNullException);
            Assert.That(() => new UserAdminService(_context, null!, roles), Throws.ArgumentNullException);
            Assert.That(() => new UserAdminService(_context, _users, null!), Throws.ArgumentNullException);
        });
    }

    // ----- list ------------------------------------------------------------------------------------------

    [Test]
    public async Task ListAsync_DefaultQuery_ListsNonDeletedUsersByEmailWithRolesAndStatus()
    {
        var bob = await CreateUserAsync("bob@example.com", "Editor", "User");
        var gone = await CreateUserAsync("gone@example.com");
        await _service.SoftDeleteAsync(gone.Id, _actorId);
        await _service.UpdateAsync(bob.Id, new UserUpdate("bob@example.com", null, new UserProfileInput("Bob", "Bob", "Builder")), _actorId);
        await _service.RecordLoginAsync(bob.Id);

        var page = await _service.ListAsync(new UserListQuery());

        Assert.Multiple(() =>
        {
            Assert.That(page.Total, Is.EqualTo(2));
            Assert.That(page.Items.Select(i => i.Email), Is.EqualTo(new[] { "actor@example.com", "bob@example.com" }));
            var item = page.Items[1];
            Assert.That(item.Roles, Is.EqualTo(new[] { "Editor", "User" }));
            Assert.That(item.Status, Is.EqualTo(UserStatus.Active));
            Assert.That(item.DisplayName, Is.EqualTo("Bob"));
            Assert.That(item.FirstName, Is.EqualTo("Bob"));
            Assert.That(item.LastName, Is.EqualTo("Builder"));
            Assert.That(item.LastLoginUtc, Is.Not.Null);
            Assert.That(item.IsInvitePending, Is.False);
            Assert.That(page.Items[0].Roles, Is.EqualTo(new[] { "Admin" }));
        });
    }

    [Test]
    public void ListAsync_NullQuery_Throws()
    {
        Assert.That(() => _service.ListAsync(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task ListAsync_RoleFilter_ListsOnlyMembers()
    {
        await CreateUserAsync("editor@example.com", "Editor");
        await CreateUserAsync("plain@example.com");

        var editors = await _service.ListAsync(new UserListQuery(Role: "editor"));
        var unknown = await _service.ListAsync(new UserListQuery(Role: "Nope"));

        Assert.Multiple(() =>
        {
            Assert.That(editors.Items.Select(i => i.Email), Is.EqualTo(new[] { "editor@example.com" }));
            Assert.That(unknown.Total, Is.Zero);
            Assert.That(unknown.Items, Is.Empty);
        });
    }

    [Test]
    public async Task ListAsync_StatusFilters_SplitUsersByStatus()
    {
        var unconfirmed = new IdentityUser { UserName = "new@example.com", Email = "new@example.com" };
        await _users.CreateAsync(unconfirmed, Password);
        var locked = await CreateUserAsync("locked@example.com");
        await _service.LockAsync(locked.Id, _actorId);
        var deleted = await CreateUserAsync("deleted@example.com");
        await _service.SoftDeleteAsync(deleted.Id, _actorId);

        async Task<string[]> Emails(UserStatus status) =>
            (await _service.ListAsync(new UserListQuery(Status: status))).Items.Select(i => i.Email).ToArray();

        Assert.Multiple(async () =>
        {
            Assert.That(await Emails(UserStatus.Active), Is.EqualTo(new[] { "actor@example.com" }));
            Assert.That(await Emails(UserStatus.Unconfirmed), Is.EqualTo(new[] { "new@example.com" }));
            Assert.That(await Emails(UserStatus.Locked), Is.EqualTo(new[] { "locked@example.com" }));
            Assert.That(await Emails(UserStatus.Deleted), Is.EqualTo(new[] { "deleted@example.com" }));
        });
    }

    [Test]
    public async Task ListAsync_Paging_ClampsAndSkips()
    {
        for (var i = 0; i < 3; i++)
            await CreateUserAsync($"u{i}@example.com");

        var second = await _service.ListAsync(new UserListQuery(Skip: 1, Take: 2));
        var clamped = await _service.ListAsync(new UserListQuery(Skip: -5, Take: 0));
        var capped = await _service.ListAsync(new UserListQuery(Take: 1000));

        Assert.Multiple(() =>
        {
            Assert.That(second.Total, Is.EqualTo(4));
            Assert.That(second.Items.Select(i => i.Email), Is.EqualTo(new[] { "u0@example.com", "u1@example.com" }));
            Assert.That((clamped.Skip, clamped.Take, clamped.Items.Count), Is.EqualTo((0, 1, 1)));
            Assert.That(capped.Take, Is.EqualTo(UserListQuery.MaxTake));
        });
    }

    // ----- get ---------------------------------------------------------------------------------------------

    [Test]
    public async Task GetAsync_UnknownOrBlank_ReturnsNull()
    {
        Assert.Multiple(async () =>
        {
            Assert.That(await _service.GetAsync("missing"), Is.Null);
            Assert.That(await _service.GetAsync(" "), Is.Null);
        });
    }

    [Test]
    public async Task GetAsync_ReturnsAccountSecurityAndProfile()
    {
        var user = await CreateUserAsync("detail@example.com", "User");
        await _users.AddLoginAsync(user, new UserLoginInfo("GitHub", "gh-1", "GitHub"));
        await _users.SetPhoneNumberAsync(user, "555-0100");
        _context.Set<ApiTokenDTO>().AddRange(
            new ApiTokenDTO { Id = Guid.NewGuid(), UserId = user.Id, Name = "live", TokenHash = new string('a', 64), Prefix = "wwc_a" },
            new ApiTokenDTO { Id = Guid.NewGuid(), UserId = user.Id, Name = "old", TokenHash = new string('b', 64), Prefix = "wwc_b", RevokedUtc = DateTime.UtcNow },
            new ApiTokenDTO { Id = Guid.NewGuid(), UserId = user.Id, Name = "expired", TokenHash = new string('c', 64), Prefix = "wwc_c", ExpiresUtc = DateTime.UtcNow.AddDays(-1) });
        await _context.SaveChangesAsync();

        var detail = await _service.GetAsync(user.Id);

        Assert.Multiple(() =>
        {
            Assert.That(detail!.Email, Is.EqualTo("detail@example.com"));
            Assert.That(detail.EmailConfirmed, Is.True);
            Assert.That(detail.PhoneNumber, Is.EqualTo("555-0100"));
            Assert.That(detail.Roles, Is.EqualTo(new[] { "User" }));
            Assert.That(detail.Status, Is.EqualTo(UserStatus.Active));
            Assert.That(detail.HasPassword, Is.True);
            Assert.That(detail.TwoFactorEnabled, Is.False);
            Assert.That(detail.PasskeyCount, Is.Zero);
            Assert.That(detail.ExternalLogins.Single(), Is.EqualTo(new UserExternalLogin("GitHub", "gh-1", "GitHub")));
            Assert.That(detail.ActiveApiTokenCount, Is.EqualTo(1));
            Assert.That(detail.DisplayName, Is.Null);
            Assert.That(detail.IsInvitePending, Is.False);
        });
    }

    // ----- invite ------------------------------------------------------------------------------------------

    [Test]
    public async Task InviteAsync_CreatesPasswordlessUnconfirmedUserWithRolesAndProfile()
    {
        var result = await _service.InviteAsync(
            new UserInvite(" new@example.com ", new UserProfileInput(" New ", "", null), ["user", "Editor"]), _actorId);

        var user = await ReloadAsync(result.UserId!);
        var profile = await ProfileAsync(user.Id);
        var detail = await _service.GetAsync(user.Id);
        Assert.Multiple(async () =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(user.Email, Is.EqualTo("new@example.com"));
            Assert.That(user.UserName, Is.EqualTo("new@example.com"));
            Assert.That(user.EmailConfirmed, Is.False);
            Assert.That(user.PasswordHash, Is.Null);
            Assert.That(await _users.GetRolesAsync(user), Is.EquivalentTo(new[] { "User", "Editor" }));
            Assert.That(profile!.DisplayName, Is.EqualTo("New"));
            Assert.That(profile.FirstName, Is.Null);
            Assert.That(profile.InvitedBy, Is.EqualTo(_actorId));
            Assert.That(profile.InvitedUtc, Is.Not.Null);
            Assert.That(detail!.IsInvitePending, Is.True);
            Assert.That(detail.Status, Is.EqualTo(UserStatus.Unconfirmed));
        });
    }

    [TestCase("")]
    [TestCase("not-an-email")]
    public async Task InviteAsync_InvalidEmail_Fails(string email)
    {
        var result = await _service.InviteAsync(new UserInvite(email, new UserProfileInput(null, null, null), []), _actorId);

        Assert.That(result.ErrorMessage, Is.EqualTo("Enter a valid email address."));
    }

    [Test]
    public async Task InviteAsync_NullEmail_Fails()
    {
        var result = await _service.InviteAsync(new UserInvite(null!, new UserProfileInput(null, null, null), []), _actorId);

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public void InviteAsync_NullInvite_Throws()
    {
        Assert.That(() => _service.InviteAsync(null!, _actorId), Throws.ArgumentNullException);
    }

    [Test]
    public async Task InviteAsync_NameTooLong_Fails()
    {
        var result = await _service.InviteAsync(
            new UserInvite("x@example.com", new UserProfileInput(null, null, new string('a', 257)), []), _actorId);

        Assert.That(result.ErrorMessage, Does.Contain("at most 256"));
    }

    [Test]
    public async Task InviteAsync_ExistingEmail_FailsAndNamesDeletedUser()
    {
        var gone = await CreateUserAsync("gone@example.com");
        await _service.SoftDeleteAsync(gone.Id, _actorId);

        var live = await _service.InviteAsync(new UserInvite("ACTOR@example.com", new UserProfileInput(null, null, null), []), _actorId);
        var deleted = await _service.InviteAsync(new UserInvite("gone@example.com", new UserProfileInput(null, null, null), []), _actorId);

        Assert.Multiple(() =>
        {
            Assert.That(live.ErrorMessage, Is.EqualTo("A user with this email already exists."));
            Assert.That(deleted.ErrorMessage, Does.StartWith("A deleted user has this email."));
        });
    }

    [Test]
    public async Task InviteAsync_UnknownRole_FailsWithoutCreatingUser()
    {
        var result = await _service.InviteAsync(new UserInvite("r@example.com", new UserProfileInput(null, null, null), ["Ghost"]), _actorId);

        Assert.Multiple(async () =>
        {
            Assert.That(result.ErrorMessage, Is.EqualTo("Role 'Ghost' does not exist."));
            Assert.That(await _users.FindByEmailAsync("r@example.com"), Is.Null);
        });
    }

    [Test]
    public async Task InviteAsync_NullProfile_CreatesEmptyProfile()
    {
        var result = await _service.InviteAsync(new UserInvite("np@example.com", null!, []), _actorId);

        var profile = await ProfileAsync(result.UserId!);
        Assert.That((profile!.DisplayName, profile.FirstName, profile.LastName), Is.EqualTo(((string?)null, (string?)null, (string?)null)));
    }

    [Test]
    public async Task InviteAsync_IdentityRejectsUserName_ReportsIdentityErrors()
    {
        // A valid address whose '!' is not an allowed username character: Identity's own validation fails.
        var result = await _service.InviteAsync(new UserInvite("bang!@example.com", new UserProfileInput(null, null, null), []), _actorId);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("bang!@example.com"));
        });
    }

    [Test]
    public async Task InviteAsync_NoRoles_CreatesUserWithoutRoles()
    {
        var result = await _service.InviteAsync(new UserInvite("none@example.com", new UserProfileInput(null, null, null), [" ", ""]), _actorId);

        Assert.That(await _users.GetRolesAsync(await ReloadAsync(result.UserId!)), Is.Empty);
    }

    // ----- update ------------------------------------------------------------------------------------------

    [Test]
    public async Task UpdateAsync_EmailChange_SetsUsernameUnconfirmsAndReportsPrevious()
    {
        var user = await CreateUserAsync("old@example.com");
        var stamp = user.SecurityStamp;

        var result = await _service.UpdateAsync(user.Id, new UserUpdate("new@example.com", " 555-0101 ", new UserProfileInput("D", "F", "L")), _actorId);

        var reloaded = await ReloadAsync(user.Id);
        var profile = await ProfileAsync(user.Id);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.PreviousEmail, Is.EqualTo("old@example.com"));
            Assert.That(reloaded.Email, Is.EqualTo("new@example.com"));
            Assert.That(reloaded.UserName, Is.EqualTo("new@example.com"));
            Assert.That(reloaded.EmailConfirmed, Is.False);
            Assert.That(reloaded.SecurityStamp, Is.Not.EqualTo(stamp));
            Assert.That(reloaded.PhoneNumber, Is.EqualTo("555-0101"));
            Assert.That((profile!.DisplayName, profile.FirstName, profile.LastName), Is.EqualTo(("D", "F", "L")));
        });
    }

    [Test]
    public async Task UpdateAsync_SameEmailDifferentCase_KeepsConfirmationAndUpdatesExistingProfile()
    {
        var user = await CreateUserAsync("same@example.com");
        await _service.UpdateAsync(user.Id, new UserUpdate("same@example.com", "555-0102", new UserProfileInput("One", null, null)), _actorId);

        var result = await _service.UpdateAsync(user.Id, new UserUpdate("SAME@example.com", "", new UserProfileInput("Two", null, null)), _actorId);

        var reloaded = await ReloadAsync(user.Id);
        Assert.Multiple(async () =>
        {
            Assert.That(result.PreviousEmail, Is.Null);
            Assert.That(reloaded.EmailConfirmed, Is.True);
            Assert.That(reloaded.PhoneNumber, Is.Null);
            Assert.That((await ProfileAsync(user.Id))!.DisplayName, Is.EqualTo("Two"));
        });
    }

    [Test]
    public async Task UpdateAsync_Refusals()
    {
        var user = await CreateUserAsync("u@example.com");
        var none = new UserProfileInput(null, null, null);

        Assert.Multiple(async () =>
        {
            Assert.That((await _service.UpdateAsync("missing", new UserUpdate("a@example.com", null, none), _actorId)).ErrorMessage,
                Is.EqualTo(UserAdminService.NotFoundMessage));
            Assert.That((await _service.UpdateAsync(user.Id, new UserUpdate(null!, null, none), _actorId)).ErrorMessage,
                Is.EqualTo("Enter a valid email address."));
            Assert.That((await _service.UpdateAsync(user.Id, new UserUpdate("u@example.com", null, new UserProfileInput(new string('a', 300), null, null)), _actorId)).Success,
                Is.False);
            Assert.That((await _service.UpdateAsync(user.Id, new UserUpdate("u@example.com", "not a phone!", none), _actorId)).ErrorMessage,
                Is.EqualTo("Enter a valid phone number."));
            Assert.That((await _service.UpdateAsync(user.Id, new UserUpdate("actor@example.com", null, none), _actorId)).ErrorMessage,
                Is.EqualTo("Another user already has this email."));
        });
        Assert.That(() => _service.UpdateAsync(user.Id, null!, _actorId), Throws.ArgumentNullException);
    }

    [Test]
    public async Task UpdateAsync_OwnEmailChange_RefusedButOtherFieldsSave()
    {
        var none = new UserProfileInput("Me", null, null);

        var changed = await _service.UpdateAsync(_actorId, new UserUpdate("me-new@example.com", null, none), _actorId);
        var same = await _service.UpdateAsync(_actorId, new UserUpdate("ACTOR@example.com", null, none), _actorId);

        var reloaded = await ReloadAsync(_actorId);
        Assert.Multiple(() =>
        {
            Assert.That(changed.ErrorMessage, Is.EqualTo(UserManagementGuards.SelfEmailMessage));
            Assert.That(same.Success, Is.True);
            Assert.That(reloaded.Email, Is.EqualTo("actor@example.com"));
            Assert.That(reloaded.EmailConfirmed, Is.True);
        });
    }

    [Test]
    public async Task UpdateAsync_EmailChangeOfLastActiveAdmin_Refused()
    {
        // MCP and the UI always act as an active Admin, so the lone-Admin case is reached when the actor's
        // own account no longer counts (here: it is locked behind the service's back).
        var other = await CreateUserAsync("other-admin@example.com", UserProfileRules.AdminRole);
        await _users.SetLockoutEndDateAsync(await ReloadAsync(_actorId), UserAdminService.IndefiniteLockoutEnd);

        var result = await _service.UpdateAsync(other.Id, new UserUpdate("moved@example.com", null, new UserProfileInput(null, null, null)), _actorId);

        Assert.Multiple(async () =>
        {
            Assert.That(result.ErrorMessage, Is.EqualTo(UserManagementGuards.LastAdminMessage));
            Assert.That((await ReloadAsync(other.Id)).EmailConfirmed, Is.True);
        });
    }

    [Test]
    public async Task Guards_UnconfirmedAdminDoesNotCountAsActive()
    {
        var pending = new UserInvite("pending-admin@example.com", new UserProfileInput(null, null, null), [UserProfileRules.AdminRole]);
        Assert.That((await _service.InviteAsync(pending, _actorId)).Success, Is.True);
        var other = await CreateUserAsync("other@example.com");

        var locked = await _service.LockAsync(_actorId, other.Id);
        var deleted = await _service.SoftDeleteAsync(_actorId, other.Id);
        var demoted = await _service.SetRolesAsync(_actorId, [], other.Id);

        Assert.Multiple(() =>
        {
            Assert.That(locked.ErrorMessage, Is.EqualTo(UserManagementGuards.LastAdminMessage));
            Assert.That(deleted.ErrorMessage, Is.EqualTo(UserManagementGuards.LastAdminMessage));
            Assert.That(demoted.ErrorMessage, Is.EqualTo(UserManagementGuards.LastAdminMessage));
        });
    }

    [Test]
    public async Task Guards_TakingAnUnconfirmedAdminOutOfService_IsNotGuarded()
    {
        var invited = await _service.InviteAsync(new UserInvite("pending-admin@example.com", new UserProfileInput(null, null, null), [UserProfileRules.AdminRole]), _actorId);

        var result = await _service.LockAsync(invited.UserId!, _actorId);

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task UpdateAsync_DeletedUser_Refused()
    {
        var user = await CreateUserAsync("d@example.com");
        await _service.SoftDeleteAsync(user.Id, _actorId);

        var result = await _service.UpdateAsync(user.Id, new UserUpdate("d@example.com", null, new UserProfileInput(null, null, null)), _actorId);

        Assert.That(result.ErrorMessage, Is.EqualTo(UserAdminService.DeletedMessage));
    }

    // ----- roles on a user ---------------------------------------------------------------------------------

    [Test]
    public async Task SetRolesAsync_AddsAndRemoves()
    {
        var user = await CreateUserAsync("r@example.com", "User");

        var result = await _service.SetRolesAsync(user.Id, ["editor", "Admin"], _actorId);

        Assert.Multiple(async () =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(await _users.GetRolesAsync(await ReloadAsync(user.Id)), Is.EquivalentTo(new[] { "Editor", "Admin" }));
        });
    }

    [Test]
    public async Task SetRolesAsync_Guards()
    {
        var user = await CreateUserAsync("r@example.com");

        Assert.Multiple(async () =>
        {
            Assert.That((await _service.SetRolesAsync(user.Id, ["Ghost"], _actorId)).ErrorMessage, Is.EqualTo("Role 'Ghost' does not exist."));
            Assert.That((await _service.SetRolesAsync("missing", [], _actorId)).ErrorMessage, Is.EqualTo(UserAdminService.NotFoundMessage));
            Assert.That((await _service.SetRolesAsync(_actorId, [], _actorId)).ErrorMessage,
                Is.EqualTo("You can't remove the Admin role from your own account."));
        });
        Assert.That(() => _service.SetRolesAsync(user.Id, null!, _actorId), Throws.ArgumentNullException);
    }

    [Test]
    public async Task SetRolesAsync_RemovingLastActiveAdmin_Refused_UnlessAnotherAdminIsActive()
    {
        var other = await CreateUserAsync("other@example.com");
        var lone = await _service.SetRolesAsync(_actorId, [], other.Id);

        await _users.AddToRoleAsync(other, UserProfileRules.AdminRole);
        var withSecond = await _service.SetRolesAsync(_actorId, [], other.Id);

        Assert.Multiple(() =>
        {
            Assert.That(lone.ErrorMessage, Is.EqualTo(UserManagementGuards.LastAdminMessage));
            Assert.That(withSecond.Success, Is.True);
        });
    }

    [Test]
    public async Task SetRolesAsync_RemovingAdminFromLockedAdmin_IsNotGuarded()
    {
        var locked = await CreateUserAsync("locked-admin@example.com", UserProfileRules.AdminRole);
        await _service.LockAsync(locked.Id, _actorId);

        var result = await _service.SetRolesAsync(locked.Id, [], _actorId);

        Assert.That(result.Success, Is.True);
    }

    // ----- confirm / security ------------------------------------------------------------------------------

    [Test]
    public async Task ConfirmEmailAsync_ConfirmsOnceAndIsIdempotent()
    {
        var user = new IdentityUser { UserName = "c@example.com", Email = "c@example.com" };
        await _users.CreateAsync(user, Password);

        var first = await _service.ConfirmEmailAsync(user.Id, _actorId);
        var second = await _service.ConfirmEmailAsync(user.Id, _actorId);

        Assert.Multiple(async () =>
        {
            Assert.That(first.Success && second.Success, Is.True);
            Assert.That((await ReloadAsync(user.Id)).EmailConfirmed, Is.True);
            Assert.That((await _service.ConfirmEmailAsync("missing", _actorId)).Success, Is.False);
        });
    }

    [Test]
    public async Task LockAsync_LocksIndefinitelyAndRotatesStamp_UnlockClears()
    {
        var user = await CreateUserAsync("l@example.com");
        var stamp = user.SecurityStamp;

        var locked = await _service.LockAsync(user.Id, _actorId);
        var afterLock = await ReloadAsync(user.Id);
        var (lockedUntil, lockedStamp) = (afterLock.LockoutEnd, afterLock.SecurityStamp);
        await _users.AccessFailedAsync(afterLock);
        var unlocked = await _service.UnlockAsync(user.Id, _actorId);
        var afterUnlock = await ReloadAsync(user.Id);

        Assert.Multiple(() =>
        {
            Assert.That(locked.Success && unlocked.Success, Is.True);
            Assert.That(lockedUntil, Is.EqualTo(UserAdminService.IndefiniteLockoutEnd));
            Assert.That(lockedStamp, Is.Not.EqualTo(stamp));
            Assert.That(afterUnlock.LockoutEnd, Is.Null);
            Assert.That(afterUnlock.AccessFailedCount, Is.Zero);
        });
    }

    [Test]
    public async Task LockAsync_Guards()
    {
        var other = await CreateUserAsync("other@example.com");

        Assert.Multiple(async () =>
        {
            Assert.That((await _service.LockAsync(_actorId, _actorId)).ErrorMessage, Is.EqualTo("You can't lock your own account."));
            Assert.That((await _service.LockAsync(_actorId, other.Id)).ErrorMessage, Is.EqualTo(UserManagementGuards.LastAdminMessage));
            Assert.That((await _service.LockAsync("missing", _actorId)).Success, Is.False);
            Assert.That((await _service.UnlockAsync("missing", _actorId)).Success, Is.False);
        });
    }

    [Test]
    public async Task SignOutEverywhereAsync_RotatesStamp()
    {
        var user = await CreateUserAsync("s@example.com");
        var stamp = user.SecurityStamp;

        var result = await _service.SignOutEverywhereAsync(user.Id, _actorId);

        Assert.Multiple(async () =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That((await ReloadAsync(user.Id)).SecurityStamp, Is.Not.EqualTo(stamp));
            Assert.That((await _service.SignOutEverywhereAsync("missing", _actorId)).Success, Is.False);
        });
    }

    [Test]
    public async Task ResetTwoFactorAsync_DisablesAndReplacesKey()
    {
        var user = await CreateUserAsync("t@example.com");
        await _users.ResetAuthenticatorKeyAsync(user);
        var key = await _users.GetAuthenticatorKeyAsync(user);
        await _users.SetTwoFactorEnabledAsync(user, true);
        await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, 5);

        var result = await _service.ResetTwoFactorAsync(user.Id, _actorId);

        var reloaded = await ReloadAsync(user.Id);
        Assert.Multiple(async () =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(reloaded.TwoFactorEnabled, Is.False);
            Assert.That(await _users.GetAuthenticatorKeyAsync(reloaded), Is.Not.EqualTo(key));
            Assert.That(await _users.CountRecoveryCodesAsync(reloaded), Is.Zero);
            Assert.That((await _service.ResetTwoFactorAsync("missing", _actorId)).Success, Is.False);
        });
    }

    [Test]
    public async Task RevokeApiTokensAsync_RevokesLiveTokensOnly()
    {
        var user = await CreateUserAsync("k@example.com");
        var earlier = DateTime.UtcNow.AddDays(-2);
        _context.Set<ApiTokenDTO>().AddRange(
            new ApiTokenDTO { Id = Guid.NewGuid(), UserId = user.Id, Name = "a", TokenHash = new string('a', 64), Prefix = "p" },
            new ApiTokenDTO { Id = Guid.NewGuid(), UserId = user.Id, Name = "b", TokenHash = new string('b', 64), Prefix = "p", RevokedUtc = earlier });
        await _context.SaveChangesAsync();

        var result = await _service.RevokeApiTokensAsync(user.Id, _actorId);

        _context.ChangeTracker.Clear();
        var tokens = await _context.Set<ApiTokenDTO>().Where(t => t.UserId == user.Id).ToListAsync();
        Assert.Multiple(async () =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(tokens.All(t => t.RevokedUtc != null), Is.True);
            Assert.That(tokens.Single(t => t.Name == "b").RevokedUtc, Is.EqualTo(earlier));
            Assert.That((await _service.RevokeApiTokensAsync("missing", _actorId)).Success, Is.False);
        });
    }

    [Test]
    public async Task RemovePasskeysAsync_RemovesEveryPasskey()
    {
        var user = await CreateUserAsync("p@example.com");
        await _users.AddOrUpdatePasskeyAsync(user, new UserPasskeyInfo(
            [1, 2, 3], [4, 5], DateTimeOffset.UtcNow, 0, null, false, false, false, [6], [7]));

        var result = await _service.RemovePasskeysAsync(user.Id, _actorId);

        Assert.Multiple(async () =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(await _users.GetPasskeysAsync(await ReloadAsync(user.Id)), Is.Empty);
            Assert.That((await _service.RemovePasskeysAsync("missing", _actorId)).Success, Is.False);
        });
    }

    [Test]
    public async Task RemoveExternalLoginAsync_RemovesLinkedLoginOnly()
    {
        var user = await CreateUserAsync("e@example.com");
        await _users.AddLoginAsync(user, new UserLoginInfo("GitHub", "gh-1", "GitHub"));

        var unknown = await _service.RemoveExternalLoginAsync(user.Id, "GitHub", "other", _actorId);
        var removed = await _service.RemoveExternalLoginAsync(user.Id, "GitHub", "gh-1", _actorId);

        Assert.Multiple(async () =>
        {
            Assert.That(unknown.ErrorMessage, Is.EqualTo("That external login is not linked to this user."));
            Assert.That(removed.Success, Is.True);
            Assert.That(await _users.GetLoginsAsync(await ReloadAsync(user.Id)), Is.Empty);
            Assert.That((await _service.RemoveExternalLoginAsync("missing", "a", "b", _actorId)).Success, Is.False);
        });
    }

    // ----- soft delete / restore / purge -------------------------------------------------------------------

    [Test]
    public async Task SoftDeleteAsync_FlagsLocksAndRevokes_RestoreUndoes()
    {
        var user = await CreateUserAsync("sd@example.com");
        _context.Set<ApiTokenDTO>().Add(new ApiTokenDTO { Id = Guid.NewGuid(), UserId = user.Id, Name = "a", TokenHash = new string('a', 64), Prefix = "p" });
        await _context.SaveChangesAsync();

        var deleted = await _service.SoftDeleteAsync(user.Id, _actorId);
        var profile = await ProfileAsync(user.Id);
        var deletedLockoutEnd = (await ReloadAsync(user.Id)).LockoutEnd;
        var token = await _context.Set<ApiTokenDTO>().AsNoTracking().SingleAsync(t => t.UserId == user.Id);
        var again = await _service.SoftDeleteAsync(user.Id, _actorId);

        var restored = await _service.RestoreAsync(user.Id, _actorId);
        var restoredProfile = await ProfileAsync(user.Id);
        var afterRestore = await ReloadAsync(user.Id);

        Assert.Multiple(() =>
        {
            Assert.That(deleted.Success, Is.True);
            Assert.That(profile!.IsDeleted, Is.True);
            Assert.That(profile.DeletedBy, Is.EqualTo(_actorId));
            Assert.That(profile.DeletedUtc, Is.Not.Null);
            Assert.That(deletedLockoutEnd, Is.EqualTo(UserAdminService.IndefiniteLockoutEnd));
            Assert.That(token.RevokedUtc, Is.Not.Null);
            Assert.That(again.ErrorMessage, Is.EqualTo(UserAdminService.DeletedMessage));
            Assert.That(restored.Success, Is.True);
            Assert.That(restoredProfile!.IsDeleted, Is.False);
            Assert.That(restoredProfile.DeletedUtc, Is.Null);
            Assert.That(restoredProfile.DeletedBy, Is.Null);
            Assert.That(afterRestore.LockoutEnd, Is.Null);
        });
    }

    [Test]
    public async Task SoftDeleteAsync_Guards()
    {
        var other = await CreateUserAsync("other@example.com");

        Assert.Multiple(async () =>
        {
            Assert.That((await _service.SoftDeleteAsync(_actorId, _actorId)).ErrorMessage, Is.EqualTo("You can't delete your own account."));
            Assert.That((await _service.SoftDeleteAsync(_actorId, other.Id)).ErrorMessage, Is.EqualTo(UserManagementGuards.LastAdminMessage));
            Assert.That((await _service.SoftDeleteAsync("missing", _actorId)).Success, Is.False);
        });
    }

    [Test]
    public async Task SoftDeleteAsync_UserWithProfile_KeepsProfileFields()
    {
        var user = await CreateUserAsync("keep@example.com");
        await _service.UpdateAsync(user.Id, new UserUpdate("keep@example.com", null, new UserProfileInput("Keep", null, null)), _actorId);

        await _service.SoftDeleteAsync(user.Id, _actorId);

        Assert.That((await ProfileAsync(user.Id))!.DisplayName, Is.EqualTo("Keep"));
    }

    [Test]
    public async Task RestoreAsync_Refusals()
    {
        var live = await CreateUserAsync("live@example.com");

        Assert.Multiple(async () =>
        {
            Assert.That((await _service.RestoreAsync("missing", _actorId)).ErrorMessage, Is.EqualTo(UserAdminService.NotFoundMessage));
            Assert.That((await _service.RestoreAsync(live.Id, _actorId)).ErrorMessage, Is.EqualTo("This user is not deleted."));
        });
    }

    [Test]
    public async Task PurgeAsync_DeletedUser_RemovesUserAndProfile()
    {
        var user = await CreateUserAsync("purge@example.com", "User");
        await _service.SoftDeleteAsync(user.Id, _actorId);

        var result = await _service.PurgeAsync(user.Id, _actorId);

        Assert.Multiple(async () =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(await ReloadAsync(user.Id), Is.Null);
            Assert.That(await ProfileAsync(user.Id), Is.Null);
        });
    }

    [Test]
    public async Task PurgeAsync_Refusals()
    {
        var live = await CreateUserAsync("live@example.com");

        Assert.Multiple(async () =>
        {
            Assert.That((await _service.PurgeAsync("missing", _actorId)).ErrorMessage, Is.EqualTo(UserAdminService.NotFoundMessage));
            Assert.That((await _service.PurgeAsync(_actorId, _actorId)).ErrorMessage, Is.EqualTo("You can't delete your own account."));
            Assert.That((await _service.PurgeAsync(live.Id, _actorId)).ErrorMessage, Does.StartWith("Delete the user first"));
        });
    }

    // ----- role management -------------------------------------------------------------------------------

    [Test]
    public async Task ListRolesAsync_BuiltInsFirstWithCounts()
    {
        await CreateUserAsync("e@example.com", "Editor");
        await _service.CreateRoleAsync("Authors", _actorId);

        var roles = await _service.ListRolesAsync();

        Assert.That(roles, Is.EqualTo(new[]
        {
            new RoleSummary("Admin", 1, true),
            new RoleSummary("Editor", 1, true),
            new RoleSummary("User", 0, true),
            new RoleSummary("Authors", 0, false),
        }));
    }

    [Test]
    public async Task CreateRoleAsync_ValidatesName()
    {
        Assert.Multiple(async () =>
        {
            Assert.That((await _service.CreateRoleAsync(" Authors ", _actorId)).Success, Is.True);
            Assert.That((await _service.CreateRoleAsync("authors", _actorId)).ErrorMessage, Is.EqualTo("A role named 'authors' already exists."));
            Assert.That((await _service.CreateRoleAsync(" ", _actorId)).ErrorMessage, Is.EqualTo("Enter a role name."));
            Assert.That((await _service.CreateRoleAsync(null!, _actorId)).ErrorMessage, Is.EqualTo("Enter a role name."));
            Assert.That((await _service.CreateRoleAsync(new string('r', 257), _actorId)).ErrorMessage, Does.Contain("at most 256"));
        });
    }

    [Test]
    public async Task RenameRoleAsync_RenamesCustomRoleOnly()
    {
        await _service.CreateRoleAsync("Authors", _actorId);
        await _service.CreateRoleAsync("Writers", _actorId);

        Assert.Multiple(async () =>
        {
            Assert.That((await _service.RenameRoleAsync("Ghost", "X", _actorId)).ErrorMessage, Is.EqualTo("Role not found."));
            Assert.That((await _service.RenameRoleAsync(null!, "X", _actorId)).ErrorMessage, Is.EqualTo("Role not found."));
            Assert.That((await _service.RenameRoleAsync("Editor", "X", _actorId)).ErrorMessage, Is.EqualTo("'Editor' is a built-in role and can't be renamed."));
            Assert.That((await _service.RenameRoleAsync("Authors", null!, _actorId)).ErrorMessage, Is.EqualTo("Enter a role name."));
            Assert.That((await _service.RenameRoleAsync("Authors", "writers", _actorId)).ErrorMessage, Is.EqualTo("A role named 'writers' already exists."));
            Assert.That((await _service.RenameRoleAsync("Authors", "authors", _actorId)).Success, Is.True);
            Assert.That((await _service.RenameRoleAsync("authors", "Contributors", _actorId)).Success, Is.True);
        });
        Assert.That((await _service.ListRolesAsync()).Select(r => r.Name), Does.Contain("Contributors").And.Not.Contain("authors"));
    }

    [Test]
    public async Task DeleteRoleAsync_RefusesBuiltInAndNonEmpty()
    {
        await _service.CreateRoleAsync("Authors", _actorId);
        await _service.CreateRoleAsync("Empty", _actorId);
        await CreateUserAsync("a@example.com", "Authors");

        Assert.Multiple(async () =>
        {
            Assert.That((await _service.DeleteRoleAsync("Ghost", _actorId)).ErrorMessage, Is.EqualTo("Role not found."));
            Assert.That((await _service.DeleteRoleAsync(null!, _actorId)).ErrorMessage, Is.EqualTo("Role not found."));
            Assert.That((await _service.DeleteRoleAsync("User", _actorId)).ErrorMessage, Is.EqualTo("'User' is a built-in role and can't be deleted."));
            Assert.That((await _service.DeleteRoleAsync("Authors", _actorId)).ErrorMessage, Does.Contain("still has 1 member(s)"));
            Assert.That((await _service.DeleteRoleAsync("empty", _actorId)).Success, Is.True);
        });
    }

    // ----- last login / status -----------------------------------------------------------------------------

    [Test]
    public async Task RecordLoginAsync_CreatesThenUpdatesProfile()
    {
        var user = await CreateUserAsync("ll@example.com");

        await _service.RecordLoginAsync(user.Id);
        var first = (await ProfileAsync(user.Id))!.LastLoginUtc;
        await _service.RecordLoginAsync(user.Id);
        var second = (await ProfileAsync(user.Id))!.LastLoginUtc;

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.GreaterThanOrEqualTo(first!.Value));
        });
    }

    [Test]
    public async Task RecordLoginAsync_RacedFirstInsert_DetachesTheFailedRowSoLaterSavesWork()
    {
        var user = await CreateUserAsync("race@example.com");
        // The losing side of two concurrent first sign-ins: its insert fails with a duplicate key.
        EventHandler<SavingChangesEventArgs> duplicate = (_, _) => throw new DbUpdateException("duplicate key");
        _context.SavingChanges += duplicate;
        await _service.RecordLoginAsync(user.Id);
        _context.SavingChanges -= duplicate;

        Assert.Multiple(() =>
        {
            Assert.That(_context.ChangeTracker.Entries<UserProfileDTO>().Any(), Is.False);
            Assert.That(async () => await _context.SaveChangesAsync(), Throws.Nothing);
        });
    }

    [Test]
    public async Task IsDeletedAsync_TrueOnlyForASoftDeletedUser()
    {
        var live = await CreateUserAsync("live@example.com");
        var gone = await CreateUserAsync("gone@example.com");
        await _service.SoftDeleteAsync(gone.Id, _actorId);

        Assert.Multiple(async () =>
        {
            Assert.That(await _service.IsDeletedAsync(gone.Id), Is.True);
            Assert.That(await _service.IsDeletedAsync(live.Id), Is.False);
            Assert.That(await _service.IsDeletedAsync("missing"), Is.False);
            Assert.That(await _service.IsDeletedAsync(" "), Is.False);
        });
    }

    [Test]
    public void StatusOf_DecidesInPrecedenceOrder()
    {
        var now = DateTimeOffset.UtcNow;
        var confirmed = new IdentityUser { EmailConfirmed = true };
        var locked = new IdentityUser { EmailConfirmed = true, LockoutEnd = now.AddMinutes(5) };
        var expired = new IdentityUser { EmailConfirmed = true, LockoutEnd = now.AddMinutes(-5) };

        Assert.Multiple(() =>
        {
            Assert.That(UserAdminService.StatusOf(locked, new UserProfileDTO { IsDeleted = true }, now), Is.EqualTo(UserStatus.Deleted));
            Assert.That(UserAdminService.StatusOf(locked, null, now), Is.EqualTo(UserStatus.Locked));
            Assert.That(UserAdminService.StatusOf(expired, null, now), Is.EqualTo(UserStatus.Active));
            Assert.That(UserAdminService.StatusOf(confirmed, new UserProfileDTO(), now), Is.EqualTo(UserStatus.Active));
            Assert.That(UserAdminService.StatusOf(new IdentityUser(), null, now), Is.EqualTo(UserStatus.Unconfirmed));
        });
    }
}
