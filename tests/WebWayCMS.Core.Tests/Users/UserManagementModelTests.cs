using Microsoft.AspNetCore.Identity;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Services.Users;
using WebWayCMS.Users;

namespace WebWayCMS.Core.Tests.Users;

[TestFixture]
public class UserManagementModelTests
{
    private const string Password = "pw";
    private const string NewEmail = "new.example.invalid";
    private const string OldEmail = "old.example.invalid";

    private IUserAdminService _service = null!;
    private IUserAccountEmails _emails = null!;
    private UserManager<IdentityUser> _users = null!;
    private SignInManager<IdentityUser> _signIn = null!;
    private UserManagementModel _model = null!;
    private IdentityUser _actor = null!;
    private IdentityUser _target = null!;

    private static readonly UserActor Ui = new("actor", Password);
    private static readonly UserActor Token = new("actor", RequirePassword: false);

    [SetUp]
    public void SetUp()
    {
        _service = Substitute.For<IUserAdminService>();
        _emails = Substitute.For<IUserAccountEmails>();
        _users = IdentityTestDoubles.UserManager();
        _signIn = IdentityTestDoubles.SignInManager(_users);
        _model = new UserManagementModel(_service, _emails, _users, _signIn);

        _actor = new IdentityUser { Id = "actor", Email = "actor@example.com" };
        _target = new IdentityUser { Id = "u1", Email = "u@example.com" };
        _users.FindByIdAsync("actor").Returns(_actor);
        _users.FindByIdAsync("u1").Returns(_target);
        _users.FindByIdAsync("ghost").Returns((IdentityUser?)null);
        _users.HasPasswordAsync(_actor).Returns(true);
        _signIn.CheckPasswordSignInAsync(_actor, Password, true).Returns(SignInResult.Success);
        _signIn.CheckPasswordSignInAsync(_actor, Arg.Is<string>(p => p != Password), true).Returns(SignInResult.Failed);
        _emails.SendInvitationAsync(Arg.Any<IdentityUser>()).Returns(true);
        _emails.SendConfirmationAsync(Arg.Any<IdentityUser>()).Returns(true);
        _emails.SendPasswordResetAsync(Arg.Any<IdentityUser>()).Returns(true);
        _emails.SendEmailChangedNoticeAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
    }

    private static UserDetail Detail(UserStatus status = UserStatus.Active, bool invitePending = false, bool confirmed = true) => new(
        "u1", "u@example.com", confirmed, null, null, null, null, [], status, invitePending, true, false, 0, [], 0,
        null, 0, null, null, null);

    [Test]
    public void Constructor_NullDependency_Throws()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new UserManagementModel(null!, _emails, _users, _signIn), Throws.ArgumentNullException);
            Assert.That(() => new UserManagementModel(_service, null!, _users, _signIn), Throws.ArgumentNullException);
            Assert.That(() => new UserManagementModel(_service, _emails, null!, _signIn), Throws.ArgumentNullException);
            Assert.That(() => new UserManagementModel(_service, _emails, _users, null!), Throws.ArgumentNullException);
        });
    }

    // ----- invite ------------------------------------------------------------------------------------------

    [Test]
    public async Task InviteAsync_CreatesAndEmails()
    {
        var invite = new UserInvite("u@example.com", new UserProfileInput(null, null, null), ["User"]);
        _service.InviteAsync(invite, "actor", Arg.Any<CancellationToken>()).Returns(UserAdminResult.Ok("u1"));

        var result = await _model.InviteAsync(invite, Ui);

        Assert.That(result, Is.EqualTo(new UserManagementResult(true, "Invited u@example.com.", "u1")));
        await _emails.Received(1).SendInvitationAsync(_target);
        await _signIn.DidNotReceiveWithAnyArgs().CheckPasswordSignInAsync(default!, default!, default);
    }

    [Test]
    public async Task InviteAsync_EmailFails_SucceedsWithWarning()
    {
        var invite = new UserInvite("u@example.com", new UserProfileInput(null, null, null), []);
        _service.InviteAsync(invite, "actor", Arg.Any<CancellationToken>()).Returns(UserAdminResult.Ok("u1"));
        _emails.SendInvitationAsync(_target).Returns(false);

        var result = await _model.InviteAsync(invite, Ui);

        Assert.That(result.Success && result.Message.EndsWith(UserManagementModel.EmailFailedSuffix), Is.True);
    }

    [Test]
    public async Task InviteAsync_ServiceRefuses_ReturnsTheReason()
    {
        var invite = new UserInvite("u@example.com", new UserProfileInput(null, null, null), []);
        _service.InviteAsync(invite, "actor", Arg.Any<CancellationToken>()).Returns(UserAdminResult.Fail("exists"));

        var result = await _model.InviteAsync(invite, Ui);

        Assert.That(result, Is.EqualTo(new UserManagementResult(false, "exists")));
        await _emails.DidNotReceiveWithAnyArgs().SendInvitationAsync(default!);
    }

    [Test]
    public async Task InviteAsync_GrantingAdmin_NeedsTheActorsPassword()
    {
        var invite = new UserInvite("u@example.com", new UserProfileInput(null, null, null), ["admin"]);
        _service.InviteAsync(invite, "actor", Arg.Any<CancellationToken>()).Returns(UserAdminResult.Ok("u1"));

        var wrong = await _model.InviteAsync(invite, Ui with { Password = "nope" });
        var right = await _model.InviteAsync(invite, Ui);
        var token = await _model.InviteAsync(invite, Token);

        Assert.Multiple(() =>
        {
            Assert.That(wrong, Is.EqualTo(new UserManagementResult(false, UserManagementModel.WrongPasswordMessage)));
            Assert.That(right.Success && token.Success, Is.True);
        });
        await _service.Received(2).InviteAsync(invite, "actor", Arg.Any<CancellationToken>());
    }

    [Test]
    public void InviteAsync_NullInvite_Throws()
    {
        Assert.That(() => _model.InviteAsync(null!, Ui), Throws.ArgumentNullException);
    }

    // ----- password re-entry -------------------------------------------------------------------------------

    [Test]
    public async Task DeleteAsync_PasswordRules()
    {
        _service.SoftDeleteAsync("u1", "actor", Arg.Any<CancellationToken>()).Returns(UserAdminResult.Ok("u1"));
        _signIn.CheckPasswordSignInAsync(_actor, "locked", true).Returns(SignInResult.LockedOut);

        var noPassword = await _model.DeleteAsync("u1", Ui with { Password = null });
        var lockedOut = await _model.DeleteAsync("u1", Ui with { Password = "locked" });
        var ok = await _model.DeleteAsync("u1", Ui);

        _users.HasPasswordAsync(_actor).Returns(false);
        var passwordless = await _model.DeleteAsync("u1", Ui);
        var unknownActor = await _model.DeleteAsync("u1", new UserActor("ghost", Password));

        Assert.Multiple(() =>
        {
            Assert.That(noPassword.Message, Is.EqualTo(UserManagementModel.WrongPasswordMessage));
            Assert.That(lockedOut.Message, Is.EqualTo(UserManagementModel.ActorLockedMessage));
            Assert.That(ok, Is.EqualTo(new UserManagementResult(true, "User deleted. Restore it, or delete it permanently.", "u1")));
            Assert.That(passwordless.Message, Is.EqualTo(UserManagementModel.NoPasswordMessage));
            Assert.That(unknownActor.Message, Is.EqualTo(UserManagementModel.NoPasswordMessage));
        });
        await _service.Received(1).SoftDeleteAsync("u1", "actor", Arg.Any<CancellationToken>());
    }

    [Test]
    public void VerifyPassword_NullActor_Throws()
    {
        Assert.That(() => _model.DeleteAsync("u1", null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task PurgeAsync_NeedsPassword_ThenDelegates()
    {
        _service.PurgeAsync("u1", "actor", Arg.Any<CancellationToken>()).Returns(UserAdminResult.Ok("u1"));

        var denied = await _model.PurgeAsync("u1", Ui with { Password = "x" });
        var done = await _model.PurgeAsync("u1", Token);

        Assert.Multiple(() =>
        {
            Assert.That(denied.Success, Is.False);
            Assert.That(done.Message, Is.EqualTo("User permanently deleted."));
        });
    }

    // ----- update ------------------------------------------------------------------------------------------

    [Test]
    public async Task UpdateAsync_NoEmailChange_SendsNothing()
    {
        var update = new UserUpdate("u@example.com", null, new UserProfileInput(null, null, null));
        _service.UpdateAsync("u1", update, "actor", Arg.Any<CancellationToken>()).Returns(UserAdminResult.Ok("u1"));

        var result = await _model.UpdateAsync("u1", update, Ui);

        Assert.That(result, Is.EqualTo(new UserManagementResult(true, "Saved.", "u1")));
        await _emails.DidNotReceiveWithAnyArgs().SendConfirmationAsync(default!);
    }

    [Test]
    public async Task UpdateAsync_EmailChange_ConfirmsNewAndNotifiesOld()
    {
        var update = new UserUpdate(NewEmail, null, new UserProfileInput(null, null, null));
        _service.UpdateAsync("u1", update, "actor", Arg.Any<CancellationToken>()).Returns(UserAdminResult.Ok("u1", OldEmail));

        var result = await _model.UpdateAsync("u1", update, Ui);
        _emails.SendEmailChangedNoticeAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        var noticeFailed = await _model.UpdateAsync("u1", update, Ui);
        _emails.SendConfirmationAsync(Arg.Any<IdentityUser>()).Returns(false);
        var confirmFailed = await _model.UpdateAsync("u1", update, Ui);

        Assert.Multiple(() =>
        {
            Assert.That(result.Message, Does.Contain("A confirmation email was sent.")
                .And.Contain("A notification was sent to").And.Not.EndWith(UserManagementModel.EmailFailedSuffix));
            Assert.That(noticeFailed.Message, Does.Contain("A confirmation email was sent.")
                .And.Contain("The notification to").And.Not.EndWith(UserManagementModel.EmailFailedSuffix));
            Assert.That(confirmFailed.Message, Does.Contain("The confirmation email could not be sent; check the mail settings and try again.")
                .And.Not.Contain("A confirmation email was sent."));
        });
        await _emails.Received(3).SendConfirmationAsync(_target);
    }

    [Test]
    public async Task UpdateAsync_EmailChangeOnPendingInvite_ReinvitesWithoutConfirmationOrNotice()
    {
        var update = new UserUpdate(NewEmail, null, new UserProfileInput(null, null, null));
        _service.UpdateAsync("u1", update, "actor", Arg.Any<CancellationToken>()).Returns(UserAdminResult.Ok("u1", OldEmail));
        _service.GetAsync("u1", Arg.Any<CancellationToken>()).Returns(Detail(UserStatus.Unconfirmed, invitePending: true, confirmed: false));

        var result = await _model.UpdateAsync("u1", update, Ui);
        _emails.SendInvitationAsync(Arg.Any<IdentityUser>()).Returns(false);
        var failed = await _model.UpdateAsync("u1", update, Ui);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new UserManagementResult(true, "Saved. A new invitation was sent to u@example.com.", "u1")));
            Assert.That(failed.Message, Does.EndWith(UserManagementModel.EmailFailedSuffix));
        });
        await _emails.Received(2).SendInvitationAsync(_target);
        await _emails.DidNotReceive().SendConfirmationAsync(Arg.Any<IdentityUser>());
        await _emails.DidNotReceive().SendEmailChangedNoticeAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task UpdateAsync_Refused_ReturnsTheReason()
    {
        var update = new UserUpdate("x", null, new UserProfileInput(null, null, null));
        _service.UpdateAsync("u1", update, "actor", Arg.Any<CancellationToken>()).Returns(new UserAdminResult(false, null));

        Assert.That(await _model.UpdateAsync("u1", update, Ui), Is.EqualTo(new UserManagementResult(false, "The action failed.")));
    }

    // ----- roles -------------------------------------------------------------------------------------------

    [Test]
    public async Task SetRolesAsync_GrantingAdminToANonAdmin_NeedsPassword()
    {
        _service.SetRolesAsync("u1", Arg.Any<IReadOnlyCollection<string>>(), "actor", Arg.Any<CancellationToken>()).Returns(UserAdminResult.Ok("u1"));
        _users.IsInRoleAsync(_target, "Admin").Returns(false);

        var denied = await _model.SetRolesAsync("u1", ["Admin"], Ui with { Password = "x" });
        var granted = await _model.SetRolesAsync("u1", ["Admin"], Ui);

        Assert.Multiple(() =>
        {
            Assert.That(denied.Success, Is.False);
            Assert.That(granted, Is.EqualTo(new UserManagementResult(true, "Roles saved.", "u1")));
        });
    }

    [Test]
    public async Task SetRolesAsync_KeepingAdminOrNoAdmin_NeedsNoPassword()
    {
        _service.SetRolesAsync(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>(), "actor", Arg.Any<CancellationToken>()).Returns(UserAdminResult.Ok("u1"));
        _users.IsInRoleAsync(_target, "Admin").Returns(true);

        var kept = await _model.SetRolesAsync("u1", ["Admin"], Ui with { Password = "x" });
        var none = await _model.SetRolesAsync("u1", ["User"], Ui with { Password = "x" });
        var unknown = await _model.SetRolesAsync("ghost", ["Admin"], Ui with { Password = "x" });

        Assert.That(kept.Success && none.Success && unknown.Success, Is.True);
        await _signIn.DidNotReceiveWithAnyArgs().CheckPasswordSignInAsync(default!, default!, default);
    }

    [Test]
    public void SetRolesAsync_NullRoles_Throws()
    {
        Assert.That(() => _model.SetRolesAsync("u1", null!, Ui), Throws.ArgumentNullException);
    }

    // ----- emails ------------------------------------------------------------------------------------------

    [Test]
    public async Task SendPasswordResetAsync_Rules()
    {
        _service.GetAsync("u1", Arg.Any<CancellationToken>()).Returns(Detail());
        var sent = await _model.SendPasswordResetAsync("u1", Ui);

        _service.GetAsync("u1", Arg.Any<CancellationToken>()).Returns(Detail(UserStatus.Deleted));
        var deleted = await _model.SendPasswordResetAsync("u1", Ui);

        _service.GetAsync("u1", Arg.Any<CancellationToken>()).Returns(Detail(UserStatus.Unconfirmed, invitePending: true, confirmed: false));
        var invited = await _model.SendPasswordResetAsync("u1", Ui);

        var missing = await _model.SendPasswordResetAsync("ghost", Ui);

        Assert.Multiple(() =>
        {
            Assert.That(sent, Is.EqualTo(new UserManagementResult(true, "Sent a password reset email to u@example.com.", "u1")));
            Assert.That(deleted.Message, Is.EqualTo(UserAdminService.DeletedMessage));
            Assert.That(invited.Message, Does.Contain("Resend the invitation"));
            Assert.That(missing.Message, Is.EqualTo(UserAdminService.NotFoundMessage));
        });
    }

    [Test]
    public async Task SendConfirmationAsync_Rules()
    {
        _service.GetAsync("u1", Arg.Any<CancellationToken>()).Returns(Detail(UserStatus.Unconfirmed, confirmed: false));
        var sent = await _model.SendConfirmationAsync("u1", Ui);

        _service.GetAsync("u1", Arg.Any<CancellationToken>()).Returns(Detail());
        var confirmed = await _model.SendConfirmationAsync("u1", Ui);

        _service.GetAsync("u1", Arg.Any<CancellationToken>()).Returns(Detail(UserStatus.Deleted, confirmed: false));
        var deleted = await _model.SendConfirmationAsync("u1", Ui);

        var missing = await _model.SendConfirmationAsync("ghost", Ui);

        Assert.Multiple(() =>
        {
            Assert.That(sent.Message, Is.EqualTo("Sent a confirmation email to u@example.com."));
            Assert.That(confirmed.Message, Is.EqualTo("This email is already confirmed."));
            Assert.That(deleted.Message, Is.EqualTo(UserAdminService.DeletedMessage));
            Assert.That(missing.Message, Is.EqualTo(UserAdminService.NotFoundMessage));
        });
    }

    [Test]
    public async Task ResendInvitationAsync_Rules()
    {
        _service.GetAsync("u1", Arg.Any<CancellationToken>()).Returns(Detail(UserStatus.Unconfirmed, invitePending: true, confirmed: false));
        var sent = await _model.ResendInvitationAsync("u1", Ui);

        _service.GetAsync("u1", Arg.Any<CancellationToken>()).Returns(Detail());
        var accepted = await _model.ResendInvitationAsync("u1", Ui);

        _service.GetAsync("u1", Arg.Any<CancellationToken>()).Returns(Detail(UserStatus.Deleted, invitePending: true, confirmed: false));
        var deleted = await _model.ResendInvitationAsync("u1", Ui);

        var missing = await _model.ResendInvitationAsync("ghost", Ui);

        Assert.Multiple(() =>
        {
            Assert.That(sent.Message, Is.EqualTo("Sent a new invitation to u@example.com."));
            Assert.That(accepted.Message, Is.EqualTo("This user has no pending invitation."));
            Assert.That(deleted.Message, Is.EqualTo("This user has no pending invitation."));
            Assert.That(missing.Message, Is.EqualTo(UserAdminService.NotFoundMessage));
        });
    }

    // ----- pass-through actions --------------------------------------------------------------------------

    [Test]
    public async Task PassThroughActions_MapTheServiceResult()
    {
        var ok = UserAdminResult.Ok("u1");
        _service.ConfirmEmailAsync("u1", "actor", Arg.Any<CancellationToken>()).Returns(ok);
        _service.LockAsync("u1", "actor", Arg.Any<CancellationToken>()).Returns(ok);
        _service.UnlockAsync("u1", "actor", Arg.Any<CancellationToken>()).Returns(ok);
        _service.SignOutEverywhereAsync("u1", "actor", Arg.Any<CancellationToken>()).Returns(ok);
        _service.ResetTwoFactorAsync("u1", "actor", Arg.Any<CancellationToken>()).Returns(ok);
        _service.RevokeApiTokensAsync("u1", "actor", Arg.Any<CancellationToken>()).Returns(ok);
        _service.RemovePasskeysAsync("u1", "actor", Arg.Any<CancellationToken>()).Returns(ok);
        _service.RemoveExternalLoginAsync("u1", "GitHub", "k", "actor", Arg.Any<CancellationToken>()).Returns(ok);
        _service.RestoreAsync("u1", "actor", Arg.Any<CancellationToken>()).Returns(ok);
        _service.CreateRoleAsync(" R ", "actor", Arg.Any<CancellationToken>()).Returns(UserAdminResult.Ok());
        _service.RenameRoleAsync("R", "S ", "actor", Arg.Any<CancellationToken>()).Returns(UserAdminResult.Ok());
        _service.DeleteRoleAsync("S", "actor", Arg.Any<CancellationToken>()).Returns(UserAdminResult.Fail("members"));

        Assert.Multiple(async () =>
        {
            Assert.That((await _model.ConfirmEmailAsync("u1", Ui)).Message, Is.EqualTo("Email marked as confirmed."));
            Assert.That((await _model.LockAsync("u1", Ui)).Message, Does.StartWith("Account locked."));
            Assert.That((await _model.UnlockAsync("u1", Ui)).Message, Is.EqualTo("Account unlocked."));
            Assert.That((await _model.SignOutEverywhereAsync("u1", Ui)).Message, Does.Contain("signed out"));
            Assert.That((await _model.ResetTwoFactorAsync("u1", Ui)).Message, Is.EqualTo("Two-factor authentication was reset."));
            Assert.That((await _model.RevokeApiTokensAsync("u1", Ui)).Message, Is.EqualTo("API tokens revoked."));
            Assert.That((await _model.RemovePasskeysAsync("u1", Ui)).Message, Is.EqualTo("Passkeys removed."));
            Assert.That((await _model.RemoveExternalLoginAsync("u1", "GitHub", "k", Ui)).Message, Is.EqualTo("GitHub login removed."));
            Assert.That((await _model.RestoreAsync("u1", Ui)).Message, Is.EqualTo("User restored."));
            Assert.That((await _model.CreateRoleAsync(" R ", Ui)).Message, Is.EqualTo("Role 'R' created."));
            Assert.That((await _model.RenameRoleAsync("R", "S ", Ui)).Message, Is.EqualTo("Role renamed to 'S'."));
            Assert.That(await _model.DeleteRoleAsync("S", Ui), Is.EqualTo(new UserManagementResult(false, "members")));
        });
        _service.DeleteRoleAsync("S", "actor", Arg.Any<CancellationToken>()).Returns(UserAdminResult.Ok());
        Assert.That((await _model.DeleteRoleAsync("S", Ui)).Message, Is.EqualTo("Role 'S' deleted."));
    }

    [Test]
    public async Task RoleMessages_NullNames_StillFormat()
    {
        _service.CreateRoleAsync(null!, "actor", Arg.Any<CancellationToken>()).Returns(UserAdminResult.Ok());
        _service.RenameRoleAsync("R", null!, "actor", Arg.Any<CancellationToken>()).Returns(UserAdminResult.Ok());

        Assert.Multiple(async () =>
        {
            Assert.That((await _model.CreateRoleAsync(null!, Ui)).Message, Is.EqualTo("Role '' created."));
            Assert.That((await _model.RenameRoleAsync("R", null!, Ui)).Message, Is.EqualTo("Role renamed to ''."));
        });
    }
}
