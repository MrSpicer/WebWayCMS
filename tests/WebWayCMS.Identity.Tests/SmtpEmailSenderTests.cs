using System.Net;
using System.Net.Mail;

using Microsoft.Extensions.Options;

using NUnit.Framework;

using WebWayCMS.Identity;

namespace WebWayCMS.Identity.Tests;

[TestFixture]
public class SmtpEmailSenderTests
{
    private static SmtpEmailSender Sender(SmtpOptions options, Func<SmtpClient>? factory = null)
    {
        var wrapped = Options.Create(options);
        return factory is null ? new SmtpEmailSender(wrapped) : new SmtpEmailSender(wrapped, factory);
    }

    // DeliveryMethod = SpecifiedPickupDirectory makes SendMailAsync write a .eml file to disk instead
    // of opening a socket — no network involved, and no dependence on the OS refusing a port.
    private static SmtpClient PickupClient(string tempDir) => new()
    {
        DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
        PickupDirectoryLocation = tempDir,
    };

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "WebWayCMS-Smtp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static SmtpOptions ConfiguredOptions(string? userName = null) => new()
    {
        Host = "127.0.0.1",
        Port = 587,
        EnableSsl = false,
        FromAddress = "noreply@example.com",
        FromName = "WebWayCMS",
        UserName = userName,
        Password = "secret",
    };

    [Test]
    public void Constructor_NullClientFactory_Throws()
    {
        Assert.That(() => new SmtpEmailSender(Options.Create(new SmtpOptions()), null!), Throws.ArgumentNullException);
    }

    [Test]
    public void SendEmailAsync_MissingHost_ThrowsInvalidOperationException()
    {
        var sender = Sender(new SmtpOptions { FromAddress = "noreply@example.com" });

        Assert.That(
            async () => await sender.SendEmailAsync("user@example.com", "Subject", "<p>Body</p>"),
            Throws.InvalidOperationException);
    }

    [Test]
    public void SendEmailAsync_WhitespaceHost_ThrowsInvalidOperationException()
    {
        var sender = Sender(new SmtpOptions { Host = "   ", FromAddress = "noreply@example.com" });

        Assert.That(
            async () => await sender.SendEmailAsync("user@example.com", "Subject", "<p>Body</p>"),
            Throws.InvalidOperationException);
    }

    [Test]
    public void SendEmailAsync_MissingFromAddress_ThrowsInvalidOperationException()
    {
        var sender = Sender(new SmtpOptions { Host = "127.0.0.1" });

        Assert.That(
            async () => await sender.SendEmailAsync("user@example.com", "Subject", "<p>Body</p>"),
            Throws.InvalidOperationException);
    }

    [Test]
    public void SendEmailAsync_WhitespaceFromAddress_ThrowsInvalidOperationException()
    {
        var sender = Sender(new SmtpOptions { Host = "127.0.0.1", FromAddress = "   " });

        Assert.That(
            async () => await sender.SendEmailAsync("user@example.com", "Subject", "<p>Body</p>"),
            Throws.InvalidOperationException);
    }

    [Test]
    public async Task SendEmailAsync_WithoutCredentials_WritesEmlAndConfiguresClient()
    {
        var tempDir = NewTempDir();
        try
        {
            var client = PickupClient(tempDir);
            var sender = Sender(ConfiguredOptions(), () => client);

            await sender.SendEmailAsync("user@example.com", "Subject", "<p>Body</p>");

            Assert.Multiple(() =>
            {
                Assert.That(client.Host, Is.EqualTo("127.0.0.1"));
                Assert.That(client.EnableSsl, Is.False);
                Assert.That(client.Credentials, Is.Null);
            });

            var emlFiles = Directory.GetFiles(tempDir, "*.eml");
            Assert.That(emlFiles, Has.Length.EqualTo(1));
            var content = await File.ReadAllTextAsync(emlFiles[0]);
            Assert.Multiple(() =>
            {
                Assert.That(content, Does.Contain("To: user@example.com"));
                Assert.That(content, Does.Contain("Subject: Subject"));
                Assert.That(content, Does.Contain("<p>Body</p>"));
            });
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public async Task SendEmailAsync_WithCredentials_SetsNetworkCredential()
    {
        var tempDir = NewTempDir();
        try
        {
            var client = PickupClient(tempDir);
            var options = ConfiguredOptions(userName: "smtp-user");
            var sender = Sender(options, () => client);

            await sender.SendEmailAsync("user@example.com", "Subject", "<p>Body</p>");

            Assert.That(client.Credentials, Is.InstanceOf<NetworkCredential>());
            var credentials = (NetworkCredential)client.Credentials!;
            Assert.Multiple(() =>
            {
                Assert.That(credentials.UserName, Is.EqualTo("smtp-user"));
                Assert.That(credentials.Password, Is.EqualTo("secret"));
            });

            var emlFiles = Directory.GetFiles(tempDir, "*.eml");
            Assert.That(emlFiles, Has.Length.EqualTo(1));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
