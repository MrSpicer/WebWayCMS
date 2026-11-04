using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using NUnit.Framework;

using WebWayCMS.Identity;

namespace WebWayCMS.Identity.Tests;

[TestFixture]
public class EmailSenderExtensionsTests
{
    [Test]
    public async Task TrySendEmailAsync_SenderSucceeds_ReturnsTrueAndLogsNothing()
    {
        var sender = Substitute.For<IEmailSender>();
        var logger = Substitute.For<ILogger>();

        var sent = await sender.TrySendEmailAsync("a@example.com", "Subject", "<p>body</p>", logger);

        Assert.That(sent, Is.True);
        await sender.Received(1).SendEmailAsync("a@example.com", "Subject", "<p>body</p>");
        Assert.That(logger.ReceivedCalls(), Is.Empty);
    }

    [Test]
    public async Task TrySendEmailAsync_SenderThrows_ReturnsFalseAndLogsError()
    {
        var sender = Substitute.For<IEmailSender>();
        var failure = new InvalidOperationException("SMTP down");
        sender.SendEmailAsync(default!, default!, default!).ThrowsAsyncForAnyArgs(failure);
        var logger = Substitute.For<ILogger>();

        var sent = await sender.TrySendEmailAsync("a@example.com", "Subject", "<p>secret-token</p>", logger);

        Assert.That(sent, Is.False);
        var log = logger.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(ILogger.Log));
        var args = log.GetArguments();
        Assert.Multiple(() =>
        {
            Assert.That(args[0], Is.EqualTo(LogLevel.Error));
            Assert.That(args[3], Is.SameAs(failure));
            Assert.That(args[2]!.ToString(), Does.Contain("a@example.com").And.Contain("Subject"));
            Assert.That(args[2]!.ToString(), Does.Not.Contain("secret-token"));
        });
    }

    [Test]
    public void TrySendEmailAsync_NullSender_Throws()
    {
        IEmailSender sender = null!;

        Assert.That(() => sender.TrySendEmailAsync("a@example.com", "s", "b", Substitute.For<ILogger>()),
            Throws.ArgumentNullException);
    }

    [Test]
    public void TrySendEmailAsync_NullLogger_Throws()
    {
        var sender = Substitute.For<IEmailSender>();

        Assert.That(() => sender.TrySendEmailAsync("a@example.com", "s", "b", null!),
            Throws.ArgumentNullException);
    }
}
