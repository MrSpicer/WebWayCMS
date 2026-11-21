using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Services.Caching;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class ScopeRunnerTests
{
    [Test]
    public void Ctor_NullScopeFactory_Throws()
        => Assert.Throws<ArgumentNullException>(() => new ScopeRunner(null!));

    [Test]
    public async Task RunAsync_RunsTheActionOnAContextFromItsOwnScope()
    {
        var services = new ServiceCollection();
        services.AddDbContext<CmsDbContext>(options => options.UseInMemoryDatabase(TestContexts.NewDb()));
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var result = await new ScopeRunner(scopeFactory)
            .RunAsync((context, _) => Task.FromResult(context.Database.IsInMemory()));

        Assert.That(result, Is.True);
    }
}
