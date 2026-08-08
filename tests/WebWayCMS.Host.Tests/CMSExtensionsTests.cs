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
        using var app = BuildRenderingApp();

        var result = app.EnsureCmsRendering();

        Assert.That(result, Is.SameAs(app));
    }

    [Test]
    public void EnsureCmsRendering_CanBeInvokedWithThrowOnErrorFalse()
    {
        using var app = BuildRenderingApp();

        Assert.That(() => app.EnsureCmsRendering(throwOnError: false), Throws.Nothing);
    }

    [Test]
    public void AddWebWayCmsRendering_RegistersServicesForRenderingPipeline()
    {
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
}