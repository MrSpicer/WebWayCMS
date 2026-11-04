using NUnit.Framework;

using WebWayCMS.Models;

namespace WebWayCMS.Core.Tests;

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
