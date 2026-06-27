using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

using WebWayCMS;
using WebWayCMS.Data.DbContexts;

namespace WebWayCMS.Host.Tests;

/// <summary>
/// Covers the public EnsureCMS entry point and the middleware pipeline wiring (ConfigureMiddleware).
/// Migration and Identity/page seeding are excluded from coverage (they require a live database) and
/// are skipped here via the WEBWAYCMS_SKIP_* switches so the pipeline can be exercised in isolation.
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
        builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase("app-" + db));
        builder.Services.AddDbContext<ArticleContext>(o => o.UseInMemoryDatabase("art-" + db));
        builder.Services.AddDbContext<ContentBlockContext>(o => o.UseInMemoryDatabase("cb-" + db));
        builder.Services.AddDbContext<ContentZoneContext>(o => o.UseInMemoryDatabase("cz-" + db));
        builder.Services.AddDbContext<PageContext>(o => o.UseInMemoryDatabase("pg-" + db));

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
}