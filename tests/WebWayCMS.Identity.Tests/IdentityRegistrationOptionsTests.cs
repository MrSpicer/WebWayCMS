using NUnit.Framework;

namespace WebWayCMS.Identity.Tests;

[TestFixture]
public class IdentityRegistrationOptionsTests
{
    [Test]
    public void Defaults_AllowRegistrationFromTheIdentitySection()
    {
        Assert.Multiple(() =>
        {
            Assert.That(IdentityRegistrationOptions.SectionName, Is.EqualTo("Identity"));
            Assert.That(new IdentityRegistrationOptions().AllowRegistration, Is.True);
        });
    }
}
