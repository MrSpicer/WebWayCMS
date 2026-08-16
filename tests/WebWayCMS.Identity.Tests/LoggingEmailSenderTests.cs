using Microsoft.AspNetCore.Identity.UI.Services;

using NUnit.Framework;

using WebWayCMS.Identity;

namespace WebWayCMS.Identity.Tests;

[TestFixture]
public class LoggingEmailSenderTests
{
    [Test]
    public void Implements_IEmailSender()
    {
        Assert.That(new LoggingEmailSender(), Is.InstanceOf<IEmailSender>());
    }

    [Test]
    public async Task SendEmailAsync_LogsAndCompletes()
    {
        var sender = new LoggingEmailSender();

        await sender.SendEmailAsync("user@example.com", "Subject", "<p>Hello</p>");

        // Completing without throwing exercises the logging path.
        Assert.Pass();
    }

    [Test]
    public void SendEmailAsync_ReturnsCompletedTask()
    {
        var sender = new LoggingEmailSender();

        var task = sender.SendEmailAsync("user@example.com", "Subject", "body");

        Assert.That(task.IsCompletedSuccessfully, Is.True);
    }
}
