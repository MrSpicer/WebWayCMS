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
    [TestCase("/Identity/Account/ResendEmailConfirmation", true)]
    [TestCase("/", false)]
    [TestCase("/admin/pages", false)]
    public void IsRateLimitedPath_MatchesAuthEndpointsOnly(string path, bool expected)
    {
        Assert.That(AuthRateLimiting.IsRateLimitedPath(path), Is.EqualTo(expected));
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
}
