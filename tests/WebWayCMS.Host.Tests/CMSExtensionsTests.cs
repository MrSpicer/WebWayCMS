using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

using WebWayCMS;
using WebWayCMS.Data.DbContexts;

namespace WebWayCMS.Host.Tests;

/// <summary>
/// Covers the public EnsureCMS / EnsureCmsRendering / EnsureCmsAdmin entry points and the
/// middleware pipeline wiring. Migration and Identity/page seeding are excluded from coverage
/// (they require a live database) and are skipped here via the WEBWAYCMS_SKIP_* switches so
/// the pipeline can be exercised in isolation.
/// </summary>
[TestFixture]
public class CMSExtensionsTests
{
    private readonly string[] _skipVars = { "WEBWAYCMS_SKIP_MIGRATIONS", "WEBWAYCMS_SKIP_ROLESEED", "WEBWAYCMS_SKIP_DEFAULTPAGE" };
    private readonly string[] _skipVarsExtended = { "WEBWAYCMS_SKIP_MIGRATIONS", "WEBWAYCMS_SKIP_ROLESEED", "WEBWAYCMS_SKIP_DEFAULTPAGE",
        "WEBWAYCMS_SKIP_DEFAULTWIDGETS", "WEBWAYCMS_SKIP_DEFAULTPAGECONTROLLERS", "WEBWAYCMS_SKIP_CODEBASEDROUTES" };
    private Dictionary<string, string?> _previous = new();

    [SetUp]
    public void SetUp()
    {
        _previous = _skipVars.ToDictionary(v => v, Environment.GetEnvironmentVariable);
        foreach (var v in _skipVars)
            Environment.SetEnvironmentVariable(v, "true");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var (k, v) in _previous)
            Environment.SetEnvironmentVariable(k, v);
    }

    private void SetUpExtendedSkips()
    {
        foreach (var v in _skipVarsExtended)
            Environment.SetEnvironmentVariable(v, "true");
    }

    private static WebApplication BuildApp()
    {
        var builder = WebApplication.CreateBuilder();

        var db = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<CmsDbContext>(o => o.UseInMemoryDatabase(db));

        builder.Services.AddWebWayCms();
        return builder.Build();
    }

    [Test]
    public void EnsureCMS_WiresMiddlewarePipeline_AndReturnsApp()
    {
        using var app = BuildApp();

        var result = app.EnsureCMS();

        Assert.That(result, Is.SameAs(app));
    }

    [Test]
    public void EnsureCMS_CanBeInvokedWithThrowOnErrorFalse()
    {
        using var app = BuildApp();

        Assert.That(() => app.EnsureCMS(throwOnError: false), Throws.Nothing);
    }

    [Test]
    public void EnsureCmsRendering_WiresMiddlewarePipeline_AndReturnsApp()
    {
        SetUpExtendedSkips();
        using var app = BuildRenderingApp();

        var result = app.EnsureCmsRendering();

        Assert.That(result, Is.SameAs(app));
    }

    [Test]
    public void EnsureCmsRendering_CanBeInvokedWithThrowOnErrorFalse()
    {
        SetUpExtendedSkips();
        using var app = BuildRenderingApp();

        Assert.That(() => app.EnsureCmsRendering(throwOnError: false), Throws.Nothing);
    }

    [Test]
    public void AddWebWayCmsRendering_RegistersServicesForRenderingPipeline()
    {
        SetUpExtendedSkips();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;Username=test;Password=test" }
            })
            .Build();

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddWebWayCmsRendering(config);

        var app = builder.Build();

        Assert.That(() => app.EnsureCmsRendering(throwOnError: false), Throws.Nothing);
    }

    [Test]
    public void AddWebWayCms_WithConfiguration_DelegatesToAddWebWayCmsAdmin()
    {
        SetUpExtendedSkips();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;Username=test;Password=test" }
            })
            .Build();

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddWebWayCms(config);

        var app = builder.Build();

        Assert.That(() => app.EnsureCMS(throwOnError: false), Throws.Nothing);
    }

    private static WebApplication BuildRenderingApp()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;Username=test;Password=test" }
            })
            .Build();

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddWebWayCmsRendering(config);
        return builder.Build();
    }

    [TestCase("/test", "/test")]
    [TestCase("/", "/")]
    [TestCase("", "/")]
    [TestCase("  ", "/")]
    [TestCase("/test/", "/test")]
    [TestCase("test", "/test")]
    [TestCase("TEST", "/test")]
    public void NormalizeRoutePattern_NormalizesCorrectly(string input, string expected)
    {
        Assert.That(CMSExtensions.NormalizeRoutePattern(input), Is.EqualTo(expected));
    }

    [Test]
    public void GetWidgetComponentName_StripsViewComponentSuffix()
    {
        Assert.That(CMSExtensions.GetWidgetComponentName(typeof(WidgetTestViewComponent)), Is.EqualTo("WidgetTest"));
    }

    [Test]
    public void GetWidgetComponentName_NoSuffix_ReturnsFullName()
    {
        Assert.That(CMSExtensions.GetWidgetComponentName(typeof(PlainComponent)), Is.EqualTo("PlainComponent"));
    }

    [Test]
    public void GetControllerName_StripsControllerSuffix()
    {
        Assert.That(CMSExtensions.GetControllerName(typeof(TestController)), Is.EqualTo("Test"));
    }

    [Test]
    public void GetControllerName_NoSuffix_ReturnsFullName()
    {
        Assert.That(CMSExtensions.GetControllerName(typeof(PlainService)), Is.EqualTo("PlainService"));
    }

    [Test]
    public void IsTransientDbStartupException_SocketExceptionInner_ReturnsTrue()
    {
        var ex = new Exception("outer", new System.Net.Sockets.SocketException());
        Assert.That(CMSExtensions.IsTransientDbStartupException(ex), Is.True);
    }

    [Test]
    public void IsTransientDbStartupException_DeepSocketExceptionInner_ReturnsTrue()
    {
        var ex = new Exception("outer", new InvalidOperationException("mid",
            new System.Net.Sockets.SocketException()));
        Assert.That(CMSExtensions.IsTransientDbStartupException(ex), Is.True);
    }

    [Test]
    public void IsTransientDbStartupException_NoSocketException_ReturnsFalse()
    {
        var ex = new Exception("outer", new InvalidOperationException("inner"));
        Assert.That(CMSExtensions.IsTransientDbStartupException(ex), Is.False);
    }

    [Test]
    public void IsTransientDbStartupException_NoInnerException_ReturnsFalse()
    {
        var ex = new Exception("just a message");
        Assert.That(CMSExtensions.IsTransientDbStartupException(ex), Is.False);
    }
}

public class WidgetTestViewComponent { }
public class PlainComponent { }
public class TestController { }
public class PlainService { }