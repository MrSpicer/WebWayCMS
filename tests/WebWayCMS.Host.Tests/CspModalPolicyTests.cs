using NUnit.Framework;

namespace WebWayCMS.Host.Tests;

[TestFixture]
public class CspModalPolicyTests
{
    [Test]
    public void BuildForModal_AllowsSameOriginFraming()
    {
        var policy = CspPolicyBuilder.BuildForModal(new CspOptions());

        Assert.That(policy, Does.Contain("frame-ancestors 'self'"));
    }

    [Test]
    public void BuildForModal_HostOverrideStillWins()
    {
        var options = new CspOptions();
        options.Directives["frame-ancestors"] = "'none'";

        var policy = CspPolicyBuilder.BuildForModal(options);

        Assert.That(policy, Does.Contain("frame-ancestors 'none'"));
    }
}
