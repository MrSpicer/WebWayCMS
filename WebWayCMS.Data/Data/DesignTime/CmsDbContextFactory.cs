using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

using WebWayCMS.Data.DbContexts;

namespace WebWayCMS.Data.DesignTime;

[ExcludeFromCodeCoverage]
public sealed class CmsDbContextFactory : IDesignTimeDbContextFactory<CmsDbContext>
{
    public CmsDbContext CreateDbContext(string[] args)
    {
        var appServices = new ServiceCollection()
            .Configure<IdentityOptions>(o => o.Stores.MaxLengthForKeys = 128)
            .BuildServiceProvider();

        var options = new DbContextOptionsBuilder<CmsDbContext>()
            .UseNpgsql(
                DesignTimeConnection.String,
                b => b.MigrationsHistoryTable("__EFMigrationsHistory"))
            .UseApplicationServiceProvider(appServices)
            .Options;
        return new CmsDbContext(options);
    }
}
