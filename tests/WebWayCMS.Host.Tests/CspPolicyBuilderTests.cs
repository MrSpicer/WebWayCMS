using NUnit.Framework;

using WebWayCMS;

namespace WebWayCMS.Host.Tests;

[TestFixture]
public class CspPolicyBuilderTests
{
    [Test]
    public void HeaderName_DefaultsToEnforcingHeader()
    {
        Assert.That(CspPolicyBuilder.HeaderName(new CspOptions()), Is.EqualTo("Content-Security-Policy"));
    }

    [Test]
    public void HeaderName_ReportOnly_UsesReportOnlyHeader()
    {
        var options = new CspOptions { ReportOnly = true };

        Assert.That(CspPolicyBuilder.HeaderName(options), Is.EqualTo("Content-Security-Policy-Report-Only"));
    }

    [Test]
    public void Build_Disabled_ReturnsEmpty()
    {
        var options = new CspOptions { Enabled = false };

        Assert.That(CspPolicyBuilder.Build(options), Is.Empty);
    }

    [Test]
    public void Build_Defaults_IncludeSecureBaseline()
    {
        var policy = CspPolicyBuilder.Build(new CspOptions());

        Assert.That(policy, Does.Contain("default-src 'self'"));
        Assert.That(policy, Does.Contain("script-src 'self' https://cdn.ckeditor.com"));
        Assert.That(policy, Does.Contain("object-src 'none'"));
        Assert.That(policy, Does.Contain("frame-ancestors 'none'"));
        // Directives are separated by "; ".
        Assert.That(policy, Does.Contain("; "));
    }

    [Test]
    public void Build_HostOverride_ReplacesSingleDirectiveKeepingOthers()
    {
        var options = new CspOptions
        {
            Directives = { ["script-src"] = "'self' https://my-cdn.example" },
        };

        var policy = CspPolicyBuilder.Build(options);

        Assert.That(policy, Does.Contain("script-src 'self' https://my-cdn.example"));
        Assert.That(policy, Does.Not.Contain("script-src 'self' https://cdn.ckeditor.com"));
        // Unspecified directives keep their CMS defaults.
        Assert.That(policy, Does.Contain("default-src 'self'"));
    }

    [Test]
    public void Build_HostAddsNewDirective_AppendsIt()
    {
        var options = new CspOptions
        {
            Directives = { ["form-action"] = "'self'" },
        };

        var policy = CspPolicyBuilder.Build(options);

        Assert.That(policy, Does.Contain("form-action 'self'"));
    }

    [Test]
    public void Build_HostEmptiesDirective_DropsIt()
    {
        var options = new CspOptions
        {
            Directives = { ["object-src"] = "" },
        };

        var policy = CspPolicyBuilder.Build(options);

        Assert.That(policy, Does.Not.Contain("object-src"));
        // Other defaults remain.
        Assert.That(policy, Does.Contain("default-src 'self'"));
    }

    [Test]
    public void BuildForPreview_Defaults_AllowsSameOriginFraming()
    {
        var policy = CspPolicyBuilder.BuildForPreview(new CspOptions());

        Assert.Multiple(() =>
        {
            Assert.That(policy, Does.Contain("frame-ancestors 'self'"));
            Assert.That(policy, Does.Not.Contain("frame-ancestors 'none'"));
            Assert.That(policy, Does.Contain("default-src 'self'"));
        });
    }

    [Test]
    public void BuildForPreview_HostFrameAncestors_Wins()
    {
        var options = new CspOptions
        {
            Directives = { ["frame-ancestors"] = "'none'" },
        };

        Assert.That(CspPolicyBuilder.BuildForPreview(options), Does.Contain("frame-ancestors 'none'"));
    }

    [Test]
    public void BuildForPreview_Disabled_ReturnsEmpty()
    {
        Assert.That(CspPolicyBuilder.BuildForPreview(new CspOptions { Enabled = false }), Is.Empty);
    }

    [Test]
    public void Build_Defaults_StillDenyFraming()
    {
        Assert.That(CspPolicyBuilder.Build(new CspOptions()), Does.Contain("frame-ancestors 'none'"));
    }
}
