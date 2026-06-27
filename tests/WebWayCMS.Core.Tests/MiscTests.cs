using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Models;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class AdminHandlerRegistryTests
{
    [Test]
    public void GetHandler_ReturnsRegisteredHandlerCaseInsensitively()
    {
        var handler = Substitute.For<IAdminCrudHandler>();
        handler.ContentType.Returns("pages");
        var registry = new AdminHandlerRegistry(new[] { handler });

        Assert.Multiple(() =>
        {
            Assert.That(registry.GetHandler("pages"), Is.SameAs(handler));
            Assert.That(registry.GetHandler("PAGES"), Is.SameAs(handler));
            Assert.That(registry.GetHandler("missing"), Is.Null);
        });
    }
}

[TestFixture]
public class ErrorViewModelTests
{
    [Test]
    public void ShowRequestId_ReflectsRequestIdPresence()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new ErrorViewModel { RequestId = "abc" }.ShowRequestId, Is.True);
            Assert.That(new ErrorViewModel { RequestId = null }.ShowRequestId, Is.False);
            Assert.That(new ErrorViewModel { RequestId = "" }.ShowRequestId, Is.False);
        });
    }
}

[TestFixture]
public class AdminSaveResultTests
{
    [Test]
    public void Record_CarriesSuccessAndError()
    {
        var ok = new AdminSaveResult(true);
        var fail = new AdminSaveResult(false, "msg", "field");

        Assert.Multiple(() =>
        {
            Assert.That(ok.Success, Is.True);
            Assert.That(fail.ErrorMessage, Is.EqualTo("msg"));
            Assert.That(fail.ErrorField, Is.EqualTo("field"));
        });
    }
}