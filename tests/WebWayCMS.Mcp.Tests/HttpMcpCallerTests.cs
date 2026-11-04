using System.Security.Claims;

using Microsoft.AspNetCore.Http;

using NSubstitute;

using NUnit.Framework;

namespace WebWayCMS.Mcp.Tests;

[TestFixture]
public class HttpMcpCallerTests
{
    [Test]
    public void Constructor_NullAccessor_Throws() =>
        Assert.That(() => new HttpMcpCaller(null!), Throws.ArgumentNullException);

    [Test]
    public void User_IsTheRequestUser_OrNullOutsideARequest()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var caller = new HttpMcpCaller(accessor);
        var outside = caller.User;

        var principal = new ClaimsPrincipal(new ClaimsIdentity("Test"));
        accessor.HttpContext.Returns(new DefaultHttpContext { User = principal });

        Assert.Multiple(() =>
        {
            Assert.That(outside, Is.Null);
            Assert.That(caller.User, Is.SameAs(principal));
        });
    }
}
