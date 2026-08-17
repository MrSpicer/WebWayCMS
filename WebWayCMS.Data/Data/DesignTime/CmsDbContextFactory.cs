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
            .Configure<IdentityOptions>(o =>
            {
                o.Stores.MaxLengthForKeys = 128;
                o.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            })
            .BuildServiceProvider();

        var options = new DbContextOptionsBuilder<CmsDbContext>()
            .UseNpgsql(
                DesignTimeConnection.String,
                b => b.MigrationsHistoryTable("__EFMigrationsHistory"))
            .UseApplicationServiceProvider(appServices)
            .Options;
        // CMS-owned migrations must stay CMS-only (no host model extensions) — this keeps
        // RebuildEFMigrations.sh valid and prevents host entities leaking into CMS migrations.
        return new CmsDbContext(options, Array.Empty<ICmsModelExtension>());
    }
}
