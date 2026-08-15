using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

using WebWayCMS;
using WebWayCMS.Data.DbContexts;
using WebWayCMS.Logging;

namespace WebWayCMS.Host.Tests;

/// <summary>
/// Covers the public UseWebWayCms / UseWebWayCmsRendering / UseWebWayCmsAdmin entry points and the
/// middleware pipeline wiring. Migration and Identity/page seeding are excluded from coverage
/// (they require a live database) and are skipped here via the WEBWAYCMS_SKIP_* switches so
/// the pipeline can be exercised in isolation.
/// </summary>
[TestFixture]
public class WebWayCmsApplicationBuilderExtensionsTests
{
    private readonly string[] _skipVars = { "WEBWAYCMS_SKIP_MIGRATIONS", "WEBWAYCMS_SKIP_ROLESEED", "WEBWAYCMS_SKIP_DEFAULTPAGE" };
    private readonly string[] _skipVarsExtended = { "WEBWAYCMS_SKIP_MIGRATIONS", "WEBWAYCMS_SKIP_ROLESEED", "WEBWAYCMS_SKIP_DEFAULTPAGE",
        "WEBWAYCMS_SKIP_DEFAULTWIDGETS", "WEBWAYCMS_SKIP_DEFAULTPAGECONTROLLERS", "WEBWAYCMS_SKIP_CODEBASEDROUTES",
        "WEBWAYCMS_SKIP_DEFAULTFORMCOMPONENTS" };
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
    public void UseWebWayCms_WiresMiddlewarePipeline_AndReturnsApp()
    {
        using var app = BuildApp();

        var result = app.UseWebWayCms();

        Assert.That(result, Is.SameAs(app));
    }

    [Test]
    public void UseWebWayCms_CanBeInvokedWithThrowOnErrorFalse()
    {
        using var app = BuildApp();

        Assert.That(() => app.UseWebWayCms(throwOnError: false), Throws.Nothing);
    }

    [Test]
    public void UseWebWayCmsRendering_WiresMiddlewarePipeline_AndReturnsApp()
    {
        SetUpExtendedSkips();
        using var app = BuildRenderingApp();

        var result = app.UseWebWayCmsRendering();

        Assert.That(result, Is.SameAs(app));
    }

    [Test]
    public void UseWebWayCmsRendering_CanBeInvokedWithThrowOnErrorFalse()
    {
        SetUpExtendedSkips();
        using var app = BuildRenderingApp();

        Assert.That(() => app.UseWebWayCmsRendering(throwOnError: false), Throws.Nothing);
    }

    [Test]
    public void UseWebWayCms_WithSerilogConfigured_WiresRequestLoggingMiddleware()
    {
        var previous = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
        Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", "true");
        try
        {
            var builder = WebApplication.CreateBuilder();

            var db = Guid.NewGuid().ToString();
            builder.Services.AddDbContext<CmsDbContext>(o => o.UseInMemoryDatabase(db));
            builder.Services.AddWebWayCms();
            builder.Host.UseCmsSerilog();

            using var app = builder.Build();

            Assert.That(() => app.UseWebWayCms(throwOnError: false), Throws.Nothing);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", previous);
        }
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

        Assert.That(() => app.UseWebWayCmsRendering(throwOnError: false), Throws.Nothing);
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

        Assert.That(() => app.UseWebWayCms(throwOnError: false), Throws.Nothing);
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
}
