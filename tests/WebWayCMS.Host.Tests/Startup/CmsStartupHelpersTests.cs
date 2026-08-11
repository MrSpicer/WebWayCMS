using NUnit.Framework;

using WebWayCMS.Startup;

namespace WebWayCMS.Host.Tests.Startup;

[TestFixture]
public class CmsStartupHelpersTests
{
    [Test]
    public void GetControllerName_StripsControllerSuffix()
    {
        Assert.That(CmsStartupHelpers.GetControllerName(typeof(TestController)), Is.EqualTo("Test"));
    }

    [Test]
    public void GetControllerName_NoSuffix_ReturnsFullName()
    {
        Assert.That(CmsStartupHelpers.GetControllerName(typeof(PlainService)), Is.EqualTo("PlainService"));
    }

    [Test]
    public void IsSkipped_TrueValue_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable("TEST_SKIP_TMP", "true");
        try
        {
            Assert.That(CmsStartupHelpers.IsSkipped("TEST_SKIP_TMP"), Is.True);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_SKIP_TMP", null);
        }
    }

    [Test]
    public void IsSkipped_TRUEInUpperCase_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable("TEST_SKIP_TMP", "TRUE");
        try
        {
            Assert.That(CmsStartupHelpers.IsSkipped("TEST_SKIP_TMP"), Is.True);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_SKIP_TMP", null);
        }
    }

    [Test]
    public void IsSkipped_FalseValue_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable("TEST_SKIP_TMP", "false");
        try
        {
            Assert.That(CmsStartupHelpers.IsSkipped("TEST_SKIP_TMP"), Is.False);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_SKIP_TMP", null);
        }
    }

    [Test]
    public void IsSkipped_EnvVarNotSet_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable("TEST_SKIP_TMP", null);
        Assert.That(CmsStartupHelpers.IsSkipped("TEST_SKIP_TMP"), Is.False);
    }
}
