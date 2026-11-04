using System.Text;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using NUnit.Framework;

using WebWayCMS.Users;

namespace WebWayCMS.Core.Tests.Users;

[TestFixture]
public class UserAccountEmailsTests
{
    private UserManager<IdentityUser> _users = null!;
    private IEmailSender _sender = null!;
    private IUserAccountLinks _links = null!;
    private UserAccountEmails _emails = null!;
    private readonly IdentityUser _user = new() { Id = "u1", Email = "u@example.com" };

    [SetUp]
    public void SetUp()
    {
        _users = IdentityTestDoubles.UserManager();
        _sender = Substitute.For<IEmailSender>();
        _links = Substitute.For<IUserAccountLinks>();
        _emails = new UserAccountEmails(_users, _sender, _links, Substitute.For<ILogger<UserAccountEmails>>());
    }

    private static string Encoded(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    [Test]
    public async Task SendInvitationAsync_UsesTheInvitationTokenAndLink()
    {
        _users.GenerateUserTokenAsync(_user, InvitationTokenProvider.ProviderName, InvitationTokenProvider.Purpose).Returns("tok");
        _links.AcceptInvitation("u1", Encoded("tok")).Returns("https://x/accept?a=1&b=2");

        var sent = await _emails.SendInvitationAsync(_user);

        Assert.That(sent, Is.True);
        await _sender.Received(1).SendEmailAsync("u@example.com", UserAccountEmails.InvitationSubject,
            Arg.Is<string>(h => h.Contains("https://x/accept?a=1&amp;b=2")));
    }

    [Test]
    public async Task SendConfirmationAsync_UsesTheConfirmationTokenAndLink()
    {
        _users.GenerateEmailConfirmationTokenAsync(_user).Returns("tok");
        _links.ConfirmEmail("u1", Encoded("tok")).Returns("https://x/confirm");

        await _emails.SendConfirmationAsync(_user);

        await _sender.Received(1).SendEmailAsync("u@example.com", UserAccountEmails.ConfirmationSubject,
            Arg.Is<string>(h => h.Contains("https://x/confirm")));
    }

    [Test]
    public async Task SendPasswordResetAsync_UsesTheResetTokenAndLink()
    {
        _users.GeneratePasswordResetTokenAsync(_user).Returns("tok");
        _links.ResetPassword(Encoded("tok")).Returns("https://x/reset");

        await _emails.SendPasswordResetAsync(_user);

        await _sender.Received(1).SendEmailAsync("u@example.com", UserAccountEmails.ResetSubject,
            Arg.Is<string>(h => h.Contains("https://x/reset")));
    }

    [Test]
    public async Task SendEmailChangedNoticeAsync_GoesToThePreviousAddressAndEncodesTheNewOne()
    {
        await _emails.SendEmailChangedNoticeAsync("old@example.com", "<new>@example.com");

        await _sender.Received(1).SendEmailAsync("old@example.com", UserAccountEmails.EmailChangedSubject,
            Arg.Is<string>(h => h.Contains("&lt;new&gt;@example.com")));
    }

    [Test]
    public async Task SendFailure_ReturnsFalse()
    {
        _sender.SendEmailAsync(default!, default!, default!).ThrowsAsyncForAnyArgs(new InvalidOperationException("down"));

        Assert.That(await _emails.SendEmailChangedNoticeAsync("old@example.com", "new@example.com"), Is.False);
    }

    [Test]
    public void Constructor_NullDependency_Throws()
    {
        var logger = Substitute.For<ILogger<UserAccountEmails>>();
        Assert.Multiple(() =>
        {
            Assert.That(() => new UserAccountEmails(null!, _sender, _links, logger), Throws.ArgumentNullException);
            Assert.That(() => new UserAccountEmails(_users, null!, _links, logger), Throws.ArgumentNullException);
            Assert.That(() => new UserAccountEmails(_users, _sender, null!, logger), Throws.ArgumentNullException);
            Assert.That(() => new UserAccountEmails(_users, _sender, _links, null!), Throws.ArgumentNullException);
        });
    }
}
