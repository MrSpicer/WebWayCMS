using System.Net;

using Microsoft.AspNetCore.Http;

using NUnit.Framework;

using WebWayCMS;

namespace WebWayCMS.Host.Tests;

[TestFixture]
public class AuthRateLimitingTests
{
    [TestCase("/Identity/Account/Login", true)]
    [TestCase("/identity/account/register", true)]
    [TestCase("/Identity/Account/ForgotPassword", true)]
    [TestCase("/Identity/Account/ResetPassword", true)]
    [TestCase("/Identity/Account/ResendEmailConfirmation", true)]
    [TestCase("/Identity/Account/ExternalLogin", true)]
    [TestCase("/Identity/Account/PasskeyAssertion", true)]
    [TestCase("/Identity/Account/PasskeyRequestOptions", true)]
    [TestCase("/", false)]
    [TestCase("/wadmin/pages", false)]
    public void IsRateLimitedPath_MatchesAuthEndpointsOnly(string path, bool expected)
    {
        Assert.That(AuthRateLimiting.IsRateLimitedPath(path), Is.EqualTo(expected));
    }

    [TestCase("/Identity/Account/Login", "/Identity/Account/Login")]
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

        Assert.That(() => AuthRateLimiting.GetPartition(context), Throws.Nothing);
    }

    [Test]
    public void GetPartition_AuthPathWithIp_DoesNotThrow()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/Identity/Account/Login";
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        Assert.That(() => AuthRateLimiting.GetPartition(context), Throws.Nothing);
    }

    [Test]
    public void GetPartition_AuthPathWithoutIp_FallsBackToUnknown()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/Identity/Account/Login";
        // RemoteIpAddress is null by default — exercises the "unknown" fallback branch.

        Assert.That(() => AuthRateLimiting.GetPartition(context), Throws.Nothing);
    }

    [Test]
    public void GetPartition_KeysOnIpAndEndpointFamily()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        context.Request.Path = "/Identity/Account/Login";
        var login = AuthRateLimiting.GetPartition(context);

        context.Request.Path = "/Identity/Account/ExternalLogin";
        var externalLogin = AuthRateLimiting.GetPartition(context);

        context.Request.Path = "/Identity/Account/PasskeyRequestOptions";
        var passkeyOptions = AuthRateLimiting.GetPartition(context);

        Assert.Multiple(() =>
        {
            Assert.That(login.PartitionKey, Is.EqualTo("203.0.113.7|/Identity/Account/Login"));
            Assert.That(externalLogin.PartitionKey, Is.EqualTo("203.0.113.7|/Identity/Account/ExternalLogin"));
            Assert.That(passkeyOptions.PartitionKey, Is.EqualTo("203.0.113.7|/Identity/Account/PasskeyRequestOptions"));
        });
    }
}
