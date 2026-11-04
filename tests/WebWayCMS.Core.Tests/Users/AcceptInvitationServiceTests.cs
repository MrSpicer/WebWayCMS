using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Core.Tests.Users;
using WebWayCMS.Users;

namespace WebWayCMS.Core.Tests.Users;

[TestFixture]
public class AcceptInvitationServiceTests
{
    private readonly IdentityUser _user = new() { Id = "u1", Email = "invited@example.com" };
    private UserManager<IdentityUser> _users;
    private AcceptInvitationService _service;

    [SetUp]
    public void SetUp()
    {
        _users = IdentityTestDoubles.UserManager();
        _service = new AcceptInvitationService(_users);
        _users.FindByIdAsync("u1").Returns(_user);
        _users.HasPasswordAsync(_user).Returns(false);
        _users.IsLockedOutAsync(_user).Returns(false);
        _users.VerifyUserTokenAsync(
            _user, InvitationTokenProvider.ProviderName, InvitationTokenProvider.Purpose, "token")
            .Returns(true);
        _users.AddPasswordAsync(Arg.Any<IdentityUser>(), Arg.Any<string>())
            .Returns(IdentityResult.Success);
        _users.UpdateAsync(Arg.Any<IdentityUser>())
            .Returns(IdentityResult.Success);
    }

    private static string Link() => WebEncoders.Base64UrlEncode("token"u8);

    [Test]
    public void Constructor_NullUsers_Throws()
    {
        Assert.That(() => new AcceptInvitationService(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task PreviewAsync_NullUserId_ReturnsNull()
    {
        var preview = await _service.PreviewAsync(null, "code");

        Assert.That(preview, Is.Null);
    }

    [Test]
    public async Task PreviewAsync_EmptyCode_ReturnsNull()
    {
        var preview = await _service.PreviewAsync("u1", "");

        Assert.That(preview, Is.Null);
    }

    [Test]
    public async Task PreviewAsync_UnknownUser_ReturnsNull()
    {
        _users.FindByIdAsync("u1").Returns((IdentityUser)null!);

        var preview = await _service.PreviewAsync("u1", "code");

        Assert.That(preview, Is.Null);
    }

    [Test]
    public async Task PreviewAsync_UserAlreadyHasPassword_ReturnsNull()
    {
        _users.HasPasswordAsync(_user).Returns(true);

        var preview = await _service.PreviewAsync("u1", "code");

        Assert.That(preview, Is.Null);
    }

    [Test]
    public async Task PreviewAsync_UserLockedOut_ReturnsNull()
    {
        _users.IsLockedOutAsync(_user).Returns(true);

        var preview = await _service.PreviewAsync("u1", "code");

        Assert.That(preview, Is.Null);
    }

    [Test]
    public async Task PreviewAsync_MalformedCode_ReturnsNull()
    {
        var preview = await _service.PreviewAsync("u1", "!!not-base64url!!");

        Assert.That(preview, Is.Null);
    }

    [Test]
    public async Task PreviewAsync_RejectedToken_ReturnsNull()
    {
        _users.VerifyUserTokenAsync(
            _user, InvitationTokenProvider.ProviderName, InvitationTokenProvider.Purpose, "token")
            .Returns(false);

        var preview = await _service.PreviewAsync("u1", Link());

        Assert.That(preview, Is.Null);
    }

    [Test]
    public async Task PreviewAsync_ValidLink_ReturnsEmail()
    {
        var preview = await _service.PreviewAsync("u1", Link());

        Assert.That(preview, Is.Not.Null);
        Assert.That(preview!.Email, Is.EqualTo("invited@example.com"));
    }

    [Test]
    public async Task AcceptAsync_RejectedToken_ReportsLinkInvalid()
    {
        _users.VerifyUserTokenAsync(
            _user, InvitationTokenProvider.ProviderName, InvitationTokenProvider.Purpose, "token")
            .Returns(false);

        var result = await _service.AcceptAsync("u1", Link(), "Valid1Password!");

        Assert.That(result.LinkInvalid, Is.True);
        await _users.DidNotReceiveWithAnyArgs().AddPasswordAsync(default!, default!);
    }

    [Test]
    public async Task AcceptAsync_PasswordRejected_ReportsIdentityErrors()
    {
        var failure = IdentityResult.Failed(new IdentityError { Description = "Password too short." });
        _users.AddPasswordAsync(_user, "bad").Returns(failure);

        var result = await _service.AcceptAsync("u1", Link(), "bad");

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.PasswordSaved, Is.False);
        Assert.That(result.ErrorMessage, Is.Null);
        Assert.That(result.Email, Is.EqualTo("invited@example.com"));
        Assert.That(result.Errors, Is.EqualTo(new[] { "Password too short." }));
    }

    [Test]
    public async Task AcceptAsync_ConfirmationWriteFails_ReportsConfirmFailed()
    {
        var confirmed = IdentityResult.Failed(new IdentityError { Description = "Store write failed." });
        _users.UpdateAsync(_user).Returns(confirmed);

        var result = await _service.AcceptAsync("u1", Link(), "Valid1Password!");

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.PasswordSaved, Is.True);
        Assert.That(result.ErrorMessage, Is.EqualTo(AcceptInvitationService.ConfirmationFailedMessage));
        Assert.That(result.Email, Is.EqualTo("invited@example.com"));
    }

    [Test]
    public async Task AcceptAsync_Saved_SucceedsAndConfirmsEmail()
    {
        var result = await _service.AcceptAsync("u1", Link(), "Valid1Password!");

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Email, Is.EqualTo("invited@example.com"));
        Assert.That(_user.EmailConfirmed, Is.True);
    }
}
