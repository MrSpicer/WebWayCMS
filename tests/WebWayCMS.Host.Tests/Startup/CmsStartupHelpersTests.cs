using System.Reflection;

using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

using WebWayCMS.Startup;

namespace WebWayCMS.Host.Tests.Startup;

[TestFixture]
public class CmsStartupHelpersTests
{
    private static Assembly CoreAsm => typeof(CmsStartupHelpers).Assembly;
    private static Assembly HostAsm => typeof(NUnit.Framework.Assert).Assembly;

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

    [Test]
    public void CombineAssemblies_NullEntryAndNullHost_ReturnsCore()
    {
        var result = CmsStartupHelpers.CombineAssemblies(new[] { CoreAsm }, null, null);

        Assert.That(result, Is.EqualTo(new[] { CoreAsm }));
    }

    [Test]
    public void CombineAssemblies_EntryAndHost_AppendsBoth()
    {
        var result = CmsStartupHelpers.CombineAssemblies(new[] { CoreAsm }, HostAsm, new[] { typeof(TestController).Assembly });

        Assert.That(result, Is.EqualTo(new[] { CoreAsm, HostAsm, typeof(TestController).Assembly }));
    }

    [Test]
    public void CombineAssemblies_NullEntryWithHost_AppendsHostOnly()
    {
        var result = CmsStartupHelpers.CombineAssemblies(new[] { CoreAsm }, null, new[] { HostAsm });

        Assert.That(result, Is.EqualTo(new[] { CoreAsm, HostAsm }));
    }

    [Test]
    public void CombineAssemblies_EntryWithNullHost_AppendsEntryOnly()
    {
        var result = CmsStartupHelpers.CombineAssemblies(new[] { CoreAsm }, HostAsm, null);

        Assert.That(result, Is.EqualTo(new[] { CoreAsm, HostAsm }));
    }

    [Test]
    public void CombineAssemblies_Duplicates_AreDistinct()
    {
        var result = CmsStartupHelpers.CombineAssemblies(new[] { CoreAsm }, CoreAsm, new[] { CoreAsm });

        Assert.That(result, Is.EqualTo(new[] { CoreAsm }));
    }

    [Test]
    public void SeedAssemblies_NoCatalog_ReturnsCoreThenEntry()
    {
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();

        var result = CmsStartupHelpers.SeedAssemblies(provider, CoreAsm).ToArray();

        Assert.That(result, Is.EqualTo(new[] { CoreAsm, Assembly.GetEntryAssembly()! }));
    }

    [Test]
    public void SeedAssemblies_WithCatalog_ReturnsCoreEntryThenHost()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new CmsAssemblyCatalog(new[] { HostAsm }));
        using var provider = services.BuildServiceProvider();

        var result = CmsStartupHelpers.SeedAssemblies(provider, CoreAsm).ToArray();

        Assert.That(result, Is.EqualTo(new[] { CoreAsm, Assembly.GetEntryAssembly()!, HostAsm }));
    }
}
