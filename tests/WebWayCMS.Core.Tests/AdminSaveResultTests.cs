using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;

namespace WebWayCMS.Core.Tests;

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
