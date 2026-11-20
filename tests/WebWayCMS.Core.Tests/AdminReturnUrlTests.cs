using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class AdminReturnUrlTests
{
    private IUrlHelper _url = null!;

    [SetUp]
    public void SetUp()
    {
        _url = Substitute.For<IUrlHelper>();
        _url.IsLocalUrl(Arg.Any<string?>()).Returns(call => ((string?)call[0])?.StartsWith('/') == true);
    }

    private static HttpRequest Request(string? query = null, string? form = null)
    {
        var context = new DefaultHttpContext();
        if (query != null)
            context.Request.QueryString = QueryString.Create(AdminReturnUrl.FieldName, query);
        if (form != null)
        {
            context.Request.ContentType = "application/x-www-form-urlencoded";
            context.Request.Form = new FormCollection(new Dictionary<string, StringValues> { [AdminReturnUrl.FieldName] = form });
        }
        return context.Request;
    }

    [Test]
    public void Resolve_LocalQueryValue_ReturnsIt()
        => Assert.That(AdminReturnUrl.Resolve(Request(query: "/wadmin/pages"), _url), Is.EqualTo("/wadmin/pages"));

    [Test]
    public void Resolve_QueryWinsOverForm()
        => Assert.That(AdminReturnUrl.Resolve(Request(query: "/a", form: "/b"), _url), Is.EqualTo("/a"));

    [Test]
    public void Resolve_PostedForm_ReturnsFormValue()
        => Assert.That(AdminReturnUrl.Resolve(Request(form: "/wadmin/images"), _url), Is.EqualTo("/wadmin/images"));

    [Test]
    public void Resolve_ExternalUrl_ReturnsNull()
        => Assert.That(AdminReturnUrl.Resolve(Request(query: "https://evil.example/"), _url), Is.Null);

    [Test]
    public void Resolve_NoValue_ReturnsNull()
        => Assert.That(AdminReturnUrl.Resolve(Request(), _url), Is.Null);

    [Test]
    public void Resolve_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => AdminReturnUrl.Resolve(null!, _url), Throws.ArgumentNullException);
            Assert.That(() => AdminReturnUrl.Resolve(Request(), null!), Throws.ArgumentNullException);
        });
    }
}
