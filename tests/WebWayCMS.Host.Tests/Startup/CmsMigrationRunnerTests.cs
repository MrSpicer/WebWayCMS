using NUnit.Framework;

using WebWayCMS.Startup;

namespace WebWayCMS.Host.Tests.Startup;

[TestFixture]
public class CmsMigrationRunnerTests
{
    [Test]
    public void IsTransientDbStartupException_SocketExceptionInner_ReturnsTrue()
    {
        var ex = new Exception("outer", new System.Net.Sockets.SocketException());
        Assert.That(CmsMigrationRunner.IsTransientDbStartupException(ex), Is.True);
    }

    [Test]
    public void IsTransientDbStartupException_DeepSocketExceptionInner_ReturnsTrue()
    {
        var ex = new Exception("outer", new InvalidOperationException("mid",
            new System.Net.Sockets.SocketException()));
        Assert.That(CmsMigrationRunner.IsTransientDbStartupException(ex), Is.True);
    }

    [Test]
    public void IsTransientDbStartupException_NoSocketException_ReturnsFalse()
    {
        var ex = new Exception("outer", new InvalidOperationException("inner"));
        Assert.That(CmsMigrationRunner.IsTransientDbStartupException(ex), Is.False);
    }

    [Test]
    public void IsTransientDbStartupException_NoInnerException_ReturnsFalse()
    {
        var ex = new Exception("just a message");
        Assert.That(CmsMigrationRunner.IsTransientDbStartupException(ex), Is.False);
    }
}
