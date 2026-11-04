using Microsoft.AspNetCore.Http;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Users;

namespace WebWayCMS.Core.Tests.Users;

[TestFixture]
public class UserAccountLinksTests
{
    private static UserAccountLinks Links(HttpContext? context)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(context);
        return new UserAccountLinks(accessor);
    }

    [Test]
    public void Links_UseTheRequestOriginAndEscapeValues()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("cms.example", 8443);
        context.Request.PathBase = "/site";
        var links = Links(context);

        Assert.Multiple(() =>
        {
            Assert.That(links.ConfirmEmail("u 1", "a+b="), Is.EqualTo("https://cms.example:8443/site/Identity/Account/ConfirmEmail?userId=u%201&code=a%2Bb%3D"));
            Assert.That(links.ResetPassword("c"), Is.EqualTo("https://cms.example:8443/site/Identity/Account/ResetPassword?code=c"));
            Assert.That(links.AcceptInvitation("u", "c"), Is.EqualTo("https://cms.example:8443/site/Identity/Account/AcceptInvitation?userId=u&code=c"));
        });
    }

    [Test]
    public void Links_OutsideARequest_Throw()
    {
        Assert.That(() => Links(null).ResetPassword("c"), Throws.InvalidOperationException);
    }

    [Test]
    public void Constructor_NullAccessor_Throws()
    {
        Assert.That(() => new UserAccountLinks(null!), Throws.ArgumentNullException);
    }
}
