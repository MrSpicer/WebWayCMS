using System.Net;

using Microsoft.AspNetCore.Http;

using NUnit.Framework;

using WebWayCMS;

namespace WebWayCMS.Host.Tests;

[TestFixture]
public class AuthRateLimitingTests
{
    private static readonly AuthRateLimitOptions Defaults = new();

    [TestCase("/Identity/Account/Login", true)]
    [TestCase("/Identity/Account/LoginWith2fa", true)]
    [TestCase("/Identity/Account/LoginWithRecoveryCode", true)]
    [TestCase("/identity/account/register", true)]
    [TestCase("/Identity/Account/ForgotPassword", true)]
    [TestCase("/Identity/Account/ResetPassword", true)]
    [TestCase("/Identity/Account/ResendEmailConfirmation", true)]
    [TestCase("/Identity/Account/AcceptInvitation", true)]
    [TestCase("/Identity/Account/ExternalLogin", true)]
    [TestCase("/Identity/Account/PasskeyAssertion", true)]
    [TestCase("/Identity/Account/PasskeyRequestOptions", true)]
    [TestCase("/Identity/Account/Manage/EnableAuthenticator", true)]
    [TestCase("/Identity/Account/Manage/TwoFactorAuthentication", true)]
    [TestCase("/", false)]
    [TestCase("/wadmin/pages", false)]
    public void IsRateLimitedPath_MatchesAuthEndpointsOnly(string path, bool expected)
    {
        Assert.That(AuthRateLimiting.IsRateLimitedPath(path), Is.EqualTo(expected));
    }

    [TestCase("/Identity/Account/Login", "/Identity/Account/Login")]
    [TestCase("/Identity/Account/LoginWith2fa", "/Identity/Account/LoginWith2fa")]
    [TestCase("/Identity/Account/ResetPassword", "/Identity/Account/ResetPassword")]
    [TestCase("/Identity/Account/PasskeyRequestOptions", "/Identity/Account/PasskeyRequestOptions")]
    [TestCase("/Identity/Account/Login/subpath", "/Identity/Account/Login")]
    [TestCase("/wadmin/pages", null)]
    public void MatchLimitedPath_ReturnsMatchedPrefixOrNull(string path, string? expected)
    {
        Assert.That(AuthRateLimiting.MatchLimitedPath(path), Is.EqualTo(expected));
    }

    [Test]
    public void GetPartition_NonAuthPath_DoesNotThrow()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/";

        Assert.That(() => AuthRateLimiting.GetPartition(context, Defaults), Throws.Nothing);
    }

    [Test]
    public void GetPartition_AuthPathWithIp_DoesNotThrow()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/Identity/Account/Login";
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        Assert.That(() => AuthRateLimiting.GetPartition(context, Defaults), Throws.Nothing);
    }

    [Test]
    public void GetPartition_AuthPathWithoutIp_FallsBackToUnknown()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/Identity/Account/Login";
        // RemoteIpAddress is null by default — exercises the "unknown" fallback branch.

        Assert.That(() => AuthRateLimiting.GetPartition(context, Defaults), Throws.Nothing);
    }

    [Test]
    public void GetPartition_KeysOnIpAndEndpointFamily()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        context.Request.Path = "/Identity/Account/Login";
        var login = AuthRateLimiting.GetPartition(context, Defaults);

        context.Request.Path = "/Identity/Account/ExternalLogin";
        var externalLogin = AuthRateLimiting.GetPartition(context, Defaults);

        context.Request.Path = "/Identity/Account/PasskeyRequestOptions";
        var passkeyOptions = AuthRateLimiting.GetPartition(context, Defaults);

        Assert.Multiple(() =>
        {
            Assert.That(login.PartitionKey, Is.EqualTo("203.0.113.7|/Identity/Account/Login"));
            Assert.That(externalLogin.PartitionKey, Is.EqualTo("203.0.113.7|/Identity/Account/ExternalLogin"));
            Assert.That(passkeyOptions.PartitionKey, Is.EqualTo("203.0.113.7|/Identity/Account/PasskeyRequestOptions"));
        });
    }

    [Test]
    public void GetPartition_NullOptions_Throws()
    {
        var context = new DefaultHttpContext();

        Assert.That(() => AuthRateLimiting.GetPartition(context, null!), Throws.ArgumentNullException);
    }

    [Test]
    public void Options_Defaults_AreFivePerMinute()
    {
        var options = new AuthRateLimitOptions();

        Assert.Multiple(() =>
        {
            Assert.That(AuthRateLimitOptions.SectionName, Is.EqualTo("AuthRateLimiting"));
            Assert.That(options.PermitLimit, Is.EqualTo(5));
            Assert.That(options.WindowSeconds, Is.EqualTo(60));
            Assert.That(options.IsValid(), Is.True);
        });
    }

    [TestCase(0, 60, false)]
    [TestCase(5, 0, false)]
    [TestCase(-1, -1, false)]
    [TestCase(1, 1, true)]
    public void Options_IsValid_RequiresAtLeastOne(int permitLimit, int windowSeconds, bool expected)
    {
        var options = new AuthRateLimitOptions { PermitLimit = permitLimit, WindowSeconds = windowSeconds };

        Assert.That(options.IsValid(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPartition_AuthPath_UsesConfiguredPermitLimit()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/Identity/Account/Login";
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        var partition = AuthRateLimiting.GetPartition(context, new AuthRateLimitOptions { PermitLimit = 2, WindowSeconds = 3600 });

        using var limiter = partition.Factory(partition.PartitionKey);
        var leases = Enumerable.Range(0, 3).Select(_ => limiter.AttemptAcquire()).ToList();

        Assert.That(leases.Select(l => l.IsAcquired), Is.EqualTo(new[] { true, true, false }));
        leases.ForEach(l => l.Dispose());
    }
}
