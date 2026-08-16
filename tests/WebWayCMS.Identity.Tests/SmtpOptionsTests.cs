using NUnit.Framework;

using WebWayCMS.Identity;

namespace WebWayCMS.Identity.Tests;

[TestFixture]
public class SmtpOptionsTests
{
    [Test]
    public void Defaults_PortIs587AndSslEnabled()
    {
        var options = new SmtpOptions();

        Assert.Multiple(() =>
        {
            Assert.That(options.Host, Is.Null);
            Assert.That(options.Port, Is.EqualTo(587));
            Assert.That(options.EnableSsl, Is.True);
            Assert.That(options.UserName, Is.Null);
            Assert.That(options.Password, Is.Null);
        });
    }

    [Test]
    public void SectionName_IsSmtp()
    {
        Assert.That(SmtpOptions.SectionName, Is.EqualTo("Smtp"));
    }
}
