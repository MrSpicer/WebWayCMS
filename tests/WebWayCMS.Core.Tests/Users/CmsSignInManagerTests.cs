using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using NUnit.Framework;

using WebWayCMS.Data.Services.Users;
using WebWayCMS.Users;

namespace WebWayCMS.Core.Tests.Users;

[TestFixture]
public class CmsSignInManagerTests
{
    private IUserAdminService _userAdmin = null!;
    private IAuthenticationService _auth = null!;
    private CmsSignInManager _signIn = null!;
    private readonly IdentityUser _user = new() { Id = "u1", UserName = "u@example.com" };

    [SetUp]
    public void SetUp()
    {
        _userAdmin = Substitute.For<IUserAdminService>();
        _auth = Substitute.For<IAuthenticationService>();

        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(_auth).BuildServiceProvider(),
        };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(context);
        var claims = Substitute.For<IUserClaimsPrincipalFactory<IdentityUser>>();
        claims.CreateAsync(_user).Returns(new ClaimsPrincipal(new ClaimsIdentity("Identity.Application")));

        _signIn = new CmsSignInManager(
            IdentityTestDoubles.UserManager(),
            accessor,
            claims,
            Options.Create(new IdentityOptions()),
            Substitute.For<ILogger<SignInManager<IdentityUser>>>(),
            Substitute.For<IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<IdentityUser>>(),
            _userAdmin);
    }

    [Test]
    public async Task SignInWithClaimsAsync_SignsInAndRecordsTheLogin()
    {
        await _signIn.SignInWithClaimsAsync(_user, null, []);

        await _auth.Received(1).SignInAsync(Arg.Any<HttpContext>(), IdentityConstants.ApplicationScheme, Arg.Any<ClaimsPrincipal>(), Arg.Any<AuthenticationProperties?>());
        await _userAdmin.Received(1).RecordLoginAsync("u1", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SignInWithClaimsAsync_RecordFailure_DoesNotFailTheSignIn()
    {
        _userAdmin.RecordLoginAsync("u1", Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db down"));

        await _signIn.SignInWithClaimsAsync(_user, new AuthenticationProperties(), []);

        await _auth.ReceivedWithAnyArgs(1).SignInAsync(default!, default, default!, default);
    }

    [Test]
    public async Task RefreshSignInAsync_ReissuesTheCookieWithoutRecordingALogin_ThenSignInsRecordAgain()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "Identity.Application"));
        _auth.AuthenticateAsync(Arg.Any<HttpContext>(), IdentityConstants.ApplicationScheme)
            .Returns(AuthenticateResult.Success(new AuthenticationTicket(principal, IdentityConstants.ApplicationScheme)));
        _signIn.UserManager.GetUserId(Arg.Any<ClaimsPrincipal>()).Returns("u1");
        _signIn.UserManager.GetUserIdAsync(_user).Returns("u1");

        await _signIn.RefreshSignInAsync(_user);
        await _auth.ReceivedWithAnyArgs(1).SignInAsync(default!, default, default!, default);
        await _userAdmin.DidNotReceiveWithAnyArgs().RecordLoginAsync(default!, default);

        await _signIn.SignInWithClaimsAsync(_user, null, []);
        await _userAdmin.Received(1).RecordLoginAsync("u1", Arg.Any<CancellationToken>());
    }

    [Test]
    public void Constructor_NullUserAdmin_Throws()
    {
        Assert.That(() => new CmsSignInManager(
            IdentityTestDoubles.UserManager(),
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<IdentityUser>>(),
            Options.Create(new IdentityOptions()),
            Substitute.For<ILogger<SignInManager<IdentityUser>>>(),
            Substitute.For<IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<IdentityUser>>(),
            null!), Throws.ArgumentNullException);
    }
}
